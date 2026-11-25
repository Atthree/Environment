using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
#if ATTHREE_URP
using UnityEngine.Rendering.Universal;
#endif

namespace Atthree.Environments.Editor
{
    /// <summary>Editörde ortam kurma işleri: asset oluşturma, preset bulma, Undo ile uygulama.</summary>
    public static class EnvironmentEditorUtility
    {
        public const string PackagePresetFolder = "Packages/com.atthree.environment/Presets";
        public const string DefaultAssetFolder = "Assets/Settings/Environment";

        public static bool HasUrp
        {
            get
            {
#if ATTHREE_URP
                return true;
#else
                return false;
#endif
            }
        }

        public static bool UsingUrpPipeline
        {
            get
            {
#if ATTHREE_URP
                return GraphicsSettings.currentRenderPipeline is UniversalRenderPipelineAsset;
#else
                return false;
#endif
            }
        }

        /// <summary>Paketle gelen preset asset'i (yoksa null).</summary>
        public static EnvironmentPreset LoadBuiltIn(BuiltInEnvironment env)
        {
            return AssetDatabase.LoadAssetAtPath<EnvironmentPreset>($"{PackagePresetFolder}/{env}.asset");
        }

        static List<EnvironmentPreset> presetCache;

        [InitializeOnLoadMethod]
        static void HookProjectChanges() => EditorApplication.projectChanged += () => presetCache = null;

        /// <summary>Projedeki ve paketlerdeki tüm preset'ler: önce paketle gelenler (sırasıyla), sonra seninkiler. Proje değişene kadar önbellekte tutulur.</summary>
        public static List<EnvironmentPreset> FindAllPresets()
        {
            if (presetCache != null && !presetCache.Exists(p => p == null)) return presetCache;
            presetCache = SearchPresets();
            return presetCache;
        }

        static List<EnvironmentPreset> SearchPresets()
        {
            var builtIn = new List<EnvironmentPreset>();
            foreach (BuiltInEnvironment env in System.Enum.GetValues(typeof(BuiltInEnvironment)))
            {
                var p = LoadBuiltIn(env);
                if (p != null) builtIn.Add(p);
            }

            var own = AssetDatabase.FindAssets("t:" + nameof(EnvironmentPreset))
                .Select(AssetDatabase.GUIDToAssetPath)
                .Where(path => !path.StartsWith(PackagePresetFolder))
                .Select(AssetDatabase.LoadAssetAtPath<EnvironmentPreset>)
                .Where(p => p != null)
                .OrderBy(p => p.name);

            return builtIn.Concat(own).ToList();
        }

        public static bool IsReadOnly(Object asset)
        {
            var path = AssetDatabase.GetAssetPath(asset);
            if (string.IsNullOrEmpty(path) || !path.StartsWith("Packages/")) return false;
            var info = UnityEditor.PackageManager.PackageInfo.FindForAssetPath(path);
            // Git/registry paketleri salt-okunur; file: ile bağlı (local/embedded) paketler düzenlenebilir.
            return info == null || (info.source != UnityEditor.PackageManager.PackageSource.Local &&
                                    info.source != UnityEditor.PackageManager.PackageSource.Embedded);
        }

        /// <summary>Preset'in düzenlenebilir bir kopyasını <paramref name="folder"/>/Presets altına kaydeder ve seçer.</summary>
        public static EnvironmentPreset CopyToProject(EnvironmentPreset preset, string folder = DefaultAssetFolder)
        {
            var dir = folder + "/Presets";
            EnsureFolder(dir);
            var path = AssetDatabase.GenerateUniqueAssetPath($"{dir}/{preset.name}.asset");
            var copy = Object.Instantiate(preset);
            copy.name = Path.GetFileNameWithoutExtension(path);
            AssetDatabase.CreateAsset(copy, path);
            AssetDatabase.SaveAssets();
            presetCache = null;
            Selection.activeObject = copy;
            EditorGUIUtility.PingObject(copy);
            return copy;
        }

        /// <summary>Aktif sahneye ortamı kurar (ya da var olanı günceller). Ctrl+Z ile geri alınabilir.</summary>
        public static EnvironmentController BuildInActiveScene(EnvironmentPreset preset, EnvironmentBuildOptions options, string assetFolder = DefaultAssetFolder)
        {
            Undo.IncrementCurrentGroup();
            Undo.SetCurrentGroupName("Build Environment");
            int group = Undo.GetCurrentGroup();

            var existing = Object.FindAnyObjectByType<EnvironmentController>();
            RecordSceneState(existing);

            var assets = GetOrCreateSceneAssets(assetFolder, existing, options);
            var controller = EnvironmentBuilder.Build(preset, options, assets, o => Undo.RegisterCreatedObjectUndo(o, "Build Environment"),
                (LoadBuiltIn(BuiltInEnvironment.Day), LoadBuiltIn(BuiltInEnvironment.Sunset), LoadBuiltIn(BuiltInEnvironment.Night)));

            AssetDatabase.SaveAssets();
            EditorSceneManager.MarkSceneDirty(controller.gameObject.scene);
            Undo.CollapseUndoOperations(group);
            Selection.activeGameObject = controller.gameObject;
            return controller;
        }

        /// <summary>Var olan controller'a preset'i Undo ile uygular.</summary>
        public static void ApplyPreset(EnvironmentController controller, EnvironmentPreset preset)
        {
            if (controller == null || preset == null) return;
            if (Application.isPlaying)
            {
                controller.TransitionTo(preset, 1.5f);
                return;
            }

            Undo.IncrementCurrentGroup();
            Undo.SetCurrentGroupName("Apply Environment Preset");
            int group = Undo.GetCurrentGroup();
            RecordSceneState(controller);
            controller.SetPreset(preset);
            if (controller.Driver is TimeOfDay tod) tod.Evaluate();
            EditorSceneManager.MarkSceneDirty(controller.gameObject.scene);
            Undo.CollapseUndoOperations(group);
        }

        /// <summary>Yeni boş sahne açıp ortamı kurar (kaydedilmemiş sahne varsa önce sorar).</summary>
        public static EnvironmentController BuildInNewScene(EnvironmentPreset preset, EnvironmentBuildOptions options, string assetFolder = DefaultAssetFolder)
        {
            if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return null;
            // NewScene kullanılmayan asset'leri bellekten atar; preset'i yoldan yeniden yükle.
            string presetPath = AssetDatabase.GetAssetPath(preset);
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            if (preset == null && !string.IsNullOrEmpty(presetPath)) preset = AssetDatabase.LoadAssetAtPath<EnvironmentPreset>(presetPath);
            options.camera = true;
            return BuildInActiveScene(preset, options, assetFolder);
        }

        static void RecordSceneState(EnvironmentController controller)
        {
            var objs = new List<Object>();
            var rs = GetRenderSettingsObject();
            if (rs != null) objs.Add(rs);
            if (controller != null)
            {
                objs.Add(controller);
                if (controller.sun != null) { objs.Add(controller.sun); objs.Add(controller.sun.transform); }
                if (controller.skyboxMaterial != null) objs.Add(controller.skyboxMaterial);
#if ATTHREE_URP
                if (controller.volume != null)
                {
                    objs.Add(controller.volume);
                    if (controller.volume.sharedProfile != null)
                    {
                        objs.Add(controller.volume.sharedProfile);
                        objs.AddRange(controller.volume.sharedProfile.components);
                    }
                }
#endif
            }
            var cam = Camera.main;
            if (cam != null) objs.Add(cam);
            if (objs.Count > 0) Undo.RecordObjects(objs.ToArray(), "Environment");
        }

        /// <summary>RenderSettings'in sahnedeki gizli objesi (Undo için).</summary>
        static Object GetRenderSettingsObject()
        {
            var m = typeof(RenderSettings).GetMethod("GetRenderSettings", BindingFlags.NonPublic | BindingFlags.Static);
            return m?.Invoke(null, null) as Object;
        }

        /// <summary>Sahneye özel skybox materyali, zemin materyali ve Volume Profile'ı oluşturur (varsa kullanır).</summary>
        static EnvironmentAssets GetOrCreateSceneAssets(string folder, EnvironmentController existing, EnvironmentBuildOptions options)
        {
            var assets = new EnvironmentAssets();
            var scene = EditorSceneManager.GetActiveScene();
            string sceneName = string.IsNullOrEmpty(scene.name) ? "NewScene" : scene.name;
            string dir = $"{folder}/{sceneName}";
            // Kaydedilmemiş sahneler aynı adı paylaşır; her yeni kurulum kendi klasörünü alsın.
            if (string.IsNullOrEmpty(scene.path) && existing == null && AssetDatabase.IsValidFolder(dir))
            {
                EnsureFolder(folder);
                dir = AssetDatabase.GenerateUniqueAssetPath(dir);
                sceneName = Path.GetFileName(dir);
            }

            // Skybox
            if (existing != null && existing.skyboxMaterial != null && AssetDatabase.Contains(existing.skyboxMaterial))
            {
                assets.skybox = existing.skyboxMaterial;
            }
            else
            {
                assets.skybox = LoadOrCreate($"{dir}/{sceneName}_Sky.mat", EnvironmentBuilder.CreateSkyboxMaterial);
            }

            // Ground
            if (options.ground)
                assets.ground = LoadOrCreate($"{dir}/{sceneName}_Ground.mat", EnvironmentBuilder.CreateGroundMaterial);

#if ATTHREE_URP
            if (options.postProcessing)
            {
                if (existing != null && existing.volume != null && existing.volume.sharedProfile != null && AssetDatabase.Contains(existing.volume.sharedProfile))
                    assets.volumeProfile = existing.volume.sharedProfile;
                else
                {
                    var profile = LoadOrCreate($"{dir}/{sceneName}_Volume.asset", () => ScriptableObject.CreateInstance<VolumeProfile>());
                    assets.volumeProfile = profile;
                    if (existing != null && existing.volume != null && existing.volume.sharedProfile == null) existing.volume.sharedProfile = profile;
                }
            }
#endif
            return assets;
        }

        static T LoadOrCreate<T>(string path, System.Func<T> create) where T : Object
        {
            var asset = AssetDatabase.LoadAssetAtPath<T>(path);
            if (asset != null) return asset;
            var created = create();
            if (created == null) return null;
            EnsureFolder(Path.GetDirectoryName(path).Replace('\\', '/'));
            AssetDatabase.CreateAsset(created, path);
            return created;
        }

        public static void EnsureFolder(string folder)
        {
            if (AssetDatabase.IsValidFolder(folder)) return;
            var parent = Path.GetDirectoryName(folder).Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(folder));
        }

        // ---------------------------------------------------------------- menus

        [MenuItem("GameObject/Atthree/Environment (Day)", false, 10)]
        static void CreateFromHierarchyMenu()
        {
            var preset = LoadBuiltIn(BuiltInEnvironment.Day);
            BuildInActiveScene(preset, EnvironmentBuildOptions.Default);
        }
    }
}
