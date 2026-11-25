using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Atthree.Environments.Editor
{
    /// <summary>
    /// Controller'ın editörde oluşturduğu geçici (DontSave) objeler — gökyüzü yansıma dokusu, asset'siz skybox
    /// materyali, asset'siz Volume Profile — sahneye kaydedilmesin ve build'e girmesin.
    /// Sahne açılınca controller bunları zaten yeniden oluşturur.
    /// </summary>
    [InitializeOnLoad]
    static class EnvironmentSceneHooks
    {
        static EnvironmentSceneHooks()
        {
            EditorSceneManager.sceneSaving += (scene, path) => StripTemporary(scene);
            EditorSceneManager.sceneSaved += Restore;
        }

        static bool IsTemporary(Object o) => o != null && (o.hideFlags & HideFlags.DontSave) != 0 && !EditorUtility.IsPersistent(o);

        internal static void StripTemporary(Scene scene)
        {
            if (SceneManager.GetActiveScene() == scene)
            {
                if (IsTemporary(RenderSettings.customReflectionTexture))
                {
                    RenderSettings.customReflectionTexture = null;
                    RenderSettings.defaultReflectionMode = UnityEngine.Rendering.DefaultReflectionMode.Skybox;
                }
                if (IsTemporary(RenderSettings.skybox)) RenderSettings.skybox = null;
            }

#if ATTHREE_URP
            foreach (var root in scene.GetRootGameObjects())
                foreach (var c in root.GetComponentsInChildren<EnvironmentController>(true))
                    if (c.volume != null && IsTemporary(c.volume.sharedProfile)) c.volume.sharedProfile = null;
#endif
        }

        static void Restore(Scene scene)
        {
            foreach (var root in scene.GetRootGameObjects())
                foreach (var c in root.GetComponentsInChildren<EnvironmentController>(false))
                {
                    if (c.Driver is TimeOfDay tod) tod.Evaluate();
                    else c.Reapply();
                }
        }
    }

    /// <summary>Build sırasında da aynı temizlik.</summary>
    class EnvironmentBuildProcessor : IProcessSceneWithReport
    {
        public int callbackOrder => 0;

        public void OnProcessScene(Scene scene, BuildReport report)
        {
            if (report == null) return; // Play modunda da çağrılır; orada dokunma
            EnvironmentSceneHooks.StripTemporary(scene);
        }
    }
}
