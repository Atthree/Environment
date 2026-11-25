using System;
using UnityEngine;
using UnityEngine.Rendering;
#if ATTHREE_URP
using UnityEngine.Rendering.Universal;
#endif
using Object = UnityEngine.Object;

namespace Atthree.Environments
{
    /// <summary>Sahneye nelerin kurulacağı.</summary>
    [Serializable]
    public struct EnvironmentBuildOptions
    {
        [Tooltip("Directional Light (sahnede varsa onu kullanır).")]
        public bool sun;
        [Tooltip("URP Global Volume (post-processing).")]
        public bool postProcessing;
        [Tooltip("200x200 m düz zemin.")]
        public bool ground;
        [Tooltip("Sahnede Main Camera yoksa bir tane oluşturur.")]
        public bool camera;
        [Tooltip("Gün döngüsü (TimeOfDay) bileşeni.")]
        public bool timeOfDay;

        public static EnvironmentBuildOptions Default => new EnvironmentBuildOptions
        {
            sun = true, postProcessing = true, ground = true, camera = true, timeOfDay = false,
        };
    }

    /// <summary>
    /// Kurulumda kullanılacak, projede kayıtlı asset'ler. Editör penceresi bunları oluşturup verir;
    /// boş bırakılanlar çalışma anında geçici olarak oluşturulur.
    /// </summary>
    public struct EnvironmentAssets
    {
        public Material skybox;
        public Material ground;
        /// <summary>URP VolumeProfile (URP yoksa yok sayılır).</summary>
        public ScriptableObject volumeProfile;
    }

    /// <summary>
    /// Sahneye ortam kurar: "[Environment]" objesi altında güneş, Global Volume, zemin;
    /// gerekirse kamera. Tekrar çağırmak kopya oluşturmaz, eksikleri tamamlar.
    /// </summary>
    public static class EnvironmentBuilder
    {
        public const string RootName = "[Environment]";
        public const string SunName = "Sun";
        public const string VolumeName = "Global Volume";
        public const string GroundName = "Ground";

        /// <summary>
        /// Kurar ve preset'i uygular. <paramref name="onCreated"/> yeni oluşturulan her obje için çağrılır (editörde Undo için).
        /// <paramref name="days"/> doluysa TimeOfDay için gündüz/gün batımı/gece preset'leri olarak kullanılır.
        /// </summary>
        public static EnvironmentController Build(EnvironmentPreset preset, EnvironmentBuildOptions options,
            EnvironmentAssets assets = default, Action<Object> onCreated = null,
            (EnvironmentPreset day, EnvironmentPreset sunset, EnvironmentPreset night)? days = null)
        {
            var controller = Object.FindAnyObjectByType<EnvironmentController>();
            GameObject root;
            if (controller != null)
            {
                root = controller.gameObject;
            }
            else
            {
                root = new GameObject(RootName);
                onCreated?.Invoke(root);
                controller = root.AddComponent<EnvironmentController>();
            }
            var rootT = root.transform;

            if (assets.skybox != null) controller.skyboxMaterial = assets.skybox;

            // --- Sun
            if (options.sun && controller.sun == null)
            {
                Light sun = null;
                foreach (var l in Object.FindObjectsByType<Light>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
                    if (l.type == LightType.Directional) { sun = l; break; }

                if (sun == null)
                {
                    var go = new GameObject(SunName);
                    onCreated?.Invoke(go);
                    go.transform.SetParent(rootT, false);
                    sun = go.AddComponent<Light>();
                    sun.type = LightType.Directional;
                    sun.shadows = LightShadows.Soft;
                }
                controller.sun = sun;
            }

            // --- Post-processing
#if ATTHREE_URP
            if (options.postProcessing && controller.volume == null)
            {
                var go = new GameObject(VolumeName);
                onCreated?.Invoke(go);
                go.transform.SetParent(rootT, false);
                var volume = go.AddComponent<Volume>();
                volume.isGlobal = true;
                volume.priority = 10f; // URP şablonundaki varsayılan Global Volume'un üstünde
                if (assets.volumeProfile is VolumeProfile vp) volume.sharedProfile = vp;
                controller.volume = volume;
            }
#endif

            // --- Ground
            if (options.ground && rootT.Find(GroundName) == null)
            {
                var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
                ground.name = GroundName;
                onCreated?.Invoke(ground);
                ground.transform.SetParent(rootT, false);
                ground.transform.localScale = new Vector3(20f, 1f, 20f);
                ground.GetComponent<Renderer>().sharedMaterial = assets.ground != null ? assets.ground : CreateGroundMaterial();
            }

            // --- Camera
            if (options.camera)
            {
                var cam = Camera.main;
                if (cam == null)
                {
                    var go = new GameObject("Main Camera");
                    onCreated?.Invoke(go);
                    go.tag = "MainCamera";
                    cam = go.AddComponent<Camera>();
                    go.transform.position = new Vector3(0f, 4f, -12f);
                    go.transform.LookAt(new Vector3(0f, 1f, 0f));
                    if (Object.FindAnyObjectByType<AudioListener>() == null) go.AddComponent<AudioListener>();
                }
                cam.farClipPlane = Mathf.Max(cam.farClipPlane, 1000f);
#if ATTHREE_URP
                if (options.postProcessing)
                {
                    var data = cam.GetUniversalAdditionalCameraData();
                    if (data != null) data.renderPostProcessing = true;
                }
#endif
            }

            // --- Time of day
            if (options.timeOfDay && !root.TryGetComponent<TimeOfDay>(out _))
            {
                var tod = root.AddComponent<TimeOfDay>();
                if (days.HasValue)
                {
                    var d = days.Value;
                    tod.keys = TimeOfDay.CreateDefaultKeys(d.day, d.sunset, d.night);
                    tod.MarkKeysDirty();
                }
                tod.Evaluate();
            }

            if (preset != null && controller.Driver == null) controller.SetPreset(preset);
            else if (preset != null) SetPresetFieldOnly(controller, preset);
            return controller;
        }

        /// <summary>Yeni bir zemin materyali (aktif render pipeline'ın varsayılan shader'ı ile).</summary>
        public static Material CreateGroundMaterial()
        {
            var rp = GraphicsSettings.currentRenderPipeline;
            Material mat = rp != null && rp.defaultMaterial != null
                ? new Material(rp.defaultMaterial)
                : new Material(Shader.Find("Standard"));
            mat.name = "Environment Ground";
            var color = new Color(0.42f, 0.44f, 0.42f);
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", color);
            if (mat.HasProperty("_Color")) mat.SetColor("_Color", color);
            if (mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", 0.15f);
            if (mat.HasProperty("_Glossiness")) mat.SetFloat("_Glossiness", 0.15f);
            return mat;
        }

        /// <summary>Procedural gökyüzü materyali (Skybox/Procedural).</summary>
        public static Material CreateSkyboxMaterial()
        {
            var shader = Shader.Find("Skybox/Procedural");
            return shader != null ? new Material(shader) { name = "Environment Sky" } : null;
        }

        static void SetPresetFieldOnly(EnvironmentController controller, EnvironmentPreset preset)
        {
            // TimeOfDay sürüyorsa sadece alanı doldur; görünüm TimeOfDay'den gelir.
            var driver = controller.Driver;
            controller.Driver = null;
            controller.SetPreset(preset);
            controller.Driver = driver;
            if (driver is TimeOfDay tod) tod.Evaluate();
        }
    }
}
