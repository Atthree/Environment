using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.TestTools;
#if ATTHREE_URP
using UnityEngine.Rendering.Universal;
#endif

namespace Atthree.Environments.Tests
{
    public class EnvironmentTests
    {
        readonly List<Object> created = new List<Object>();

        T Track<T>(T o) where T : Object { created.Add(o); return o; }

        [TearDown]
        public void TearDown()
        {
            foreach (var o in created)
                if (o != null) Object.DestroyImmediate(o);
            created.Clear();
            foreach (var c in Object.FindObjectsByType<EnvironmentController>(FindObjectsSortMode.None)) Object.DestroyImmediate(c.gameObject);
            var cam = Camera.main;
            if (cam != null) Object.DestroyImmediate(cam.gameObject);
        }

        EnvironmentPreset Preset(BuiltInEnvironment env) => Track(EnvironmentPreset.Create(env));

        EnvironmentController MakeController(out Light sun)
        {
            var go = Track(new GameObject("Env"));
            go.SetActive(false);
            var c = go.AddComponent<EnvironmentController>();
            sun = new GameObject("Sun").AddComponent<Light>();
            Track(sun.gameObject);
            sun.type = LightType.Directional;
            c.sun = sun;
            go.SetActive(true);
            return c;
        }

        static void AssertColor(Color expected, Color actual, string what, float tol = 0.01f)
        {
            Assert.AreEqual(expected.r, actual.r, tol, what + ".r");
            Assert.AreEqual(expected.g, actual.g, tol, what + ".g");
            Assert.AreEqual(expected.b, actual.b, tol, what + ".b");
        }

        // ------------------------------------------------------------ apply

        [Test]
        public void SetPreset_AppliesSunFogAndAmbient()
        {
            var c = MakeController(out var sun);
            var day = Preset(BuiltInEnvironment.Day);
            c.SetPreset(day);

            var s = day.settings;
            Assert.IsTrue(sun.enabled);
            Assert.AreEqual(s.sun.intensity, sun.intensity, 1e-4f);
            AssertColor(s.sun.color, sun.color, "sun color");
            Assert.Less(Quaternion.Angle(s.sun.Rotation, sun.transform.rotation), 0.1f);
            Assert.AreSame(sun, RenderSettings.sun);

            Assert.AreEqual(s.fog.enabled, RenderSettings.fog);
            Assert.AreEqual(s.fog.density, RenderSettings.fogDensity, 1e-5f);
            AssertColor(s.fog.color, RenderSettings.fogColor, "fog");
            Assert.AreEqual(s.ambient.mode, RenderSettings.ambientMode);
            AssertColor(s.ambient.skyColor, RenderSettings.ambientSkyColor, "ambient sky");
            Assert.AreSame(day, c.Preset);
        }

        [Test]
        public void ProceduralSky_UsesProceduralSkyboxMaterial()
        {
            var c = MakeController(out _);
            var day = Preset(BuiltInEnvironment.Day);
            c.SetPreset(day);

            Assert.IsNotNull(RenderSettings.skybox);
            Assert.AreEqual("Skybox/Procedural", RenderSettings.skybox.shader.name);
            Assert.AreEqual(day.settings.sky.exposure, RenderSettings.skybox.GetFloat("_Exposure"), 1e-4f);
        }

        [Test]
        public void SolidColorSky_SetsCameraBackground()
        {
            var cam = Track(new GameObject("Cam")).AddComponent<Camera>();
            var c = MakeController(out _);
            c.targetCamera = cam;
            var dungeon = Preset(BuiltInEnvironment.Dungeon);
            c.SetPreset(dungeon);

            Assert.IsNull(RenderSettings.skybox);
            Assert.AreEqual(CameraClearFlags.SolidColor, cam.clearFlags);
            AssertColor(dungeon.settings.sky.solidColor, cam.backgroundColor, "background");

            c.SetPreset(Preset(BuiltInEnvironment.Day));
            Assert.AreEqual(CameraClearFlags.Skybox, cam.clearFlags);
        }

        [Test]
        public void SkyReflections_SetCustomReflectionCubemap()
        {
            var c = MakeController(out _);
            c.SetPreset(Preset(BuiltInEnvironment.Foggy));
            Assert.AreEqual(DefaultReflectionMode.Custom, RenderSettings.defaultReflectionMode);
            var tex = RenderSettings.customReflectionTexture;
            Assert.IsNotNull(tex);
            Assert.AreEqual(TextureDimension.Cube, tex.dimension);

            Object.DestroyImmediate(c.gameObject);
            Assert.IsNull(RenderSettings.customReflectionTexture, "Controller yok olunca yansıma geri bırakılmalı.");
            Assert.AreEqual(DefaultReflectionMode.Skybox, RenderSettings.defaultReflectionMode);
        }

        [Test]
        public void DisabledSun_TurnsLightOff()
        {
            var c = MakeController(out var sun);
            c.SetPreset(Preset(BuiltInEnvironment.Dungeon));
            Assert.IsFalse(sun.enabled);
        }

        [Test]
        public void PlayMode_DoesNotModifySkyboxAsset()
        {
            var asset = Track(new Material(Shader.Find("Skybox/Procedural")));
            asset.SetFloat("_Exposure", 5f);
            var c = MakeController(out _);
            c.skyboxMaterial = asset;
            c.SetPreset(Preset(BuiltInEnvironment.Night));

            Assert.AreNotSame(asset, RenderSettings.skybox, "Play modunda asset'in kopyası kullanılmalı.");
            Assert.AreEqual(5f, asset.GetFloat("_Exposure"), 1e-4f, "Asset değişmemeli.");
        }

        [Test]
        public void NoSceneReferences_DoesNotThrow()
        {
            var go = Track(new GameObject("Env"));
            var c = go.AddComponent<EnvironmentController>();
            c.controlCamera = false;
            Assert.DoesNotThrow(() => c.SetPreset(Preset(BuiltInEnvironment.Sunset)));
            Assert.DoesNotThrow(() => c.SetPreset(null));
        }

        [Test]
        public void ApplyOnEnable_AppliesAssignedPreset()
        {
            var night = Preset(BuiltInEnvironment.Night);
            var go = Track(new GameObject("Env"));
            go.SetActive(false);
            var c = go.AddComponent<EnvironmentController>();
            var sun = Track(new GameObject("Sun")).AddComponent<Light>();
            c.sun = sun;
            c.SetPreset(night); // inaktifken alanı doldurur
            sun.intensity = 99f;
            go.SetActive(true);
            Assert.AreEqual(night.settings.sun.intensity, sun.intensity, 1e-4f);
        }

        // ------------------------------------------------------------ lerp

        [Test]
        public void Lerp_EndpointsAndMiddle()
        {
            var a = EnvironmentPresets.Day();
            var b = EnvironmentPresets.Night();

            var at0 = EnvironmentSettings.Lerp(a, b, 0f);
            var at1 = EnvironmentSettings.Lerp(a, b, 1f);
            var mid = EnvironmentSettings.Lerp(a, b, 0.5f);

            Assert.AreEqual(a.sun.intensity, at0.sun.intensity, 1e-5f);
            Assert.AreEqual(b.sun.intensity, at1.sun.intensity, 1e-5f);
            Assert.AreEqual((a.sun.intensity + b.sun.intensity) / 2f, mid.sun.intensity, 1e-5f);
            AssertColor(Color.Lerp(a.fog.color, b.fog.color, 0.5f), mid.fog.color, "fog");
            Assert.AreEqual((a.post.bloomIntensity + b.post.bloomIntensity) / 2f, mid.post.bloomIntensity, 1e-5f);
            Assert.AreEqual(b.post.tonemapping, at1.post.tonemapping);
        }

        [Test]
        public void Lerp_SunTurningOff_FadesIntensityKeepsDirection()
        {
            var a = EnvironmentPresets.Day();
            var b = EnvironmentPresets.Dungeon(); // güneş kapalı
            var mid = EnvironmentSettings.Lerp(a, b, 0.5f);

            Assert.IsTrue(mid.sun.enabled);
            Assert.AreEqual(a.sun.intensity * 0.5f, mid.sun.intensity, 1e-4f);
            Assert.AreEqual(a.sun.pitch, mid.sun.pitch, 1e-4f, "Kapanan güneş yön değiştirmemeli.");

            var end = EnvironmentSettings.Lerp(a, b, 1f);
            Assert.AreEqual(0f, end.sun.intensity, 1e-5f);
        }

        [Test]
        public void Lerp_FogTurningOn_FadesDensityFromZero()
        {
            var a = EnvironmentPresets.Day();
            a.fog.enabled = false;
            var b = EnvironmentPresets.Foggy();
            var q = EnvironmentSettings.Lerp(a, b, 0.25f);

            Assert.IsTrue(q.fog.enabled);
            Assert.AreEqual(b.fog.density * 0.25f, q.fog.density, 1e-5f);
            AssertColor(b.fog.color, q.fog.color, "fog color comes from the enabled side");
        }

        [Test]
        public void Lerp_YawTakesShortestPath()
        {
            var a = EnvironmentPresets.Day();
            var b = a;
            a.sun.yaw = 170f;
            b.sun.yaw = -170f;
            var mid = EnvironmentSettings.Lerp(a, b, 0.5f);
            Assert.AreEqual(180f, Mathf.Abs(Mathf.DeltaAngle(0f, mid.sun.yaw)), 0.01f);
        }

        // ------------------------------------------------------------ transitions

        [UnityTest]
        public IEnumerator TransitionTo_BlendsThenArrives()
        {
            var c = MakeController(out var sun);
            var day = Preset(BuiltInEnvironment.Day);
            var night = Preset(BuiltInEnvironment.Night);
            c.SetPreset(day);

            PresetChangedCounter counter = new PresetChangedCounter(c);
            c.TransitionTo(night, 0.4f);
            Assert.IsTrue(c.IsTransitioning);
            Assert.AreSame(night, c.Preset, "Preset geçişin hedefini göstermeli.");

            yield return new WaitForSeconds(0.2f);
            Assert.IsTrue(c.IsTransitioning);
            Assert.Less(sun.intensity, day.settings.sun.intensity);
            Assert.Greater(sun.intensity, night.settings.sun.intensity);

            yield return new WaitForSeconds(0.35f);
            Assert.IsFalse(c.IsTransitioning);
            Assert.AreEqual(night.settings.sun.intensity, sun.intensity, 1e-4f);
            Assert.AreEqual(night.settings.fog.density, RenderSettings.fogDensity, 1e-5f);
            Assert.AreEqual(1, counter.count);
        }

        [UnityTest]
        public IEnumerator TransitionTo_DuringTransition_ContinuesFromCurrentLook()
        {
            var c = MakeController(out var sun);
            c.SetPreset(Preset(BuiltInEnvironment.Day));
            c.TransitionTo(Preset(BuiltInEnvironment.Night), 1f);
            yield return new WaitForSeconds(0.5f);

            float before = sun.intensity;
            var sunset = Preset(BuiltInEnvironment.Sunset);
            c.TransitionTo(sunset, 1f);
            yield return null;
            Assert.AreEqual(before, sun.intensity, 0.05f, "Yeni geçiş o anki görünümden başlamalı, zıplamamalı.");

            c.CompleteTransition();
            Assert.AreEqual(sunset.settings.sun.intensity, sun.intensity, 1e-4f);
            Assert.AreSame(sunset, c.Preset);
        }

        [Test]
        public void TransitionTo_ZeroDuration_IsInstant()
        {
            var c = MakeController(out var sun);
            var night = Preset(BuiltInEnvironment.Night);
            c.TransitionTo(night, 0f);
            Assert.IsFalse(c.IsTransitioning);
            Assert.AreEqual(night.settings.sun.intensity, sun.intensity, 1e-4f);
        }

        class PresetChangedCounter
        {
            public int count;
            public PresetChangedCounter(EnvironmentController c) { c.PresetChanged += _ => count++; }
        }

        // ------------------------------------------------------------ time of day

        TimeOfDay MakeCycle(out EnvironmentController c, out Light sun, out EnvironmentPreset day, out EnvironmentPreset night)
        {
            c = MakeController(out sun);
            day = Preset(BuiltInEnvironment.Day);
            night = Preset(BuiltInEnvironment.Night);
            var tod = c.gameObject.AddComponent<TimeOfDay>();
            tod.running = false;
            tod.keys = new List<TimeOfDay.Key> { new TimeOfDay.Key(8f, day), new TimeOfDay.Key(20f, night) };
            tod.MarkKeysDirty();
            return tod;
        }

        [Test]
        public void TimeOfDay_SunHighAtNoon_FadesAtHorizon()
        {
            var tod = MakeCycle(out _, out var sun, out _, out _);
            tod.maxElevation = 90f;

            tod.Hour = 12f;
            Assert.Greater(Vector3.Dot(sun.transform.forward, Vector3.down), 0.99f, "Öğlen güneş tepede olmalı.");
            Assert.IsTrue(sun.enabled);

            tod.Hour = 6f;
            Assert.IsFalse(sun.enabled, "Ufuktayken ışık sönmeli.");
        }

        [Test]
        public void TimeOfDay_BlendsBetweenKeys_AndWrapsPastMidnight()
        {
            var tod = MakeCycle(out var c, out _, out var day, out var night);

            tod.Hour = 8f;
            Assert.AreEqual(day.settings.fog.density, c.CurrentSettings.fog.density, 1e-5f);

            tod.Hour = 14f; // 8 ile 20'nin tam ortası (smoothstep(0.5) = 0.5)
            float mid = (day.settings.fog.density + night.settings.fog.density) / 2f;
            Assert.AreEqual(mid, c.CurrentSettings.fog.density, 1e-5f);

            tod.Hour = 2f; // 20 → 8 (ertesi gün) arasının ortası: gece yarısını sarmalı
            Assert.AreEqual(mid, c.CurrentSettings.fog.density, 1e-5f);
            Assert.IsTrue(tod.IsNight);
        }

        [Test]
        public void TimeOfDay_DrivesController_AndReleasesOnDisable()
        {
            var tod = MakeCycle(out var c, out _, out _, out var night);
            Assert.AreSame(tod, c.Driver);
            tod.enabled = false;
            Assert.IsNull(c.Driver);
        }

        [UnityTest]
        public IEnumerator TimeOfDay_Running_AdvancesHour()
        {
            var tod = MakeCycle(out _, out _, out _, out _);
            tod.Hour = 10f;
            tod.dayLengthMinutes = 0.1f; // 6 saniyede bir gün → saniyede 4 saat
            tod.running = true;
            yield return new WaitForSeconds(0.25f);
            Assert.Greater(tod.Hour, 10.5f);
            Assert.Less(tod.Hour, 13f);
        }

        // ------------------------------------------------------------ builder

        [Test]
        public void Builder_CreatesEverything_AndDoesNotDuplicate()
        {
            var day = Preset(BuiltInEnvironment.Day);
            var c = EnvironmentBuilder.Build(day, EnvironmentBuildOptions.Default);
            Track(c.gameObject);
            if (Camera.main != null) Track(Camera.main.gameObject);

            Assert.AreEqual(EnvironmentBuilder.RootName, c.gameObject.name);
            Assert.IsNotNull(c.sun);
            Assert.IsNotNull(c.transform.Find(EnvironmentBuilder.GroundName));
            Assert.IsNotNull(Camera.main);
#if ATTHREE_URP
            Assert.IsNotNull(c.volume);
            Assert.IsTrue(c.volume.isGlobal);
#endif
            Assert.AreSame(day, c.Preset);

            var again = EnvironmentBuilder.Build(Preset(BuiltInEnvironment.Night), EnvironmentBuildOptions.Default);
            Assert.AreSame(c, again);
            Assert.AreEqual(1, Object.FindObjectsByType<EnvironmentController>(FindObjectsSortMode.None).Length);
            int grounds = 0;
            foreach (Transform t in c.transform) if (t.name == EnvironmentBuilder.GroundName) grounds++;
            Assert.AreEqual(1, grounds);
        }

        [Test]
        public void Builder_ReusesExistingDirectionalLight()
        {
            var existing = Track(new GameObject("My Sun")).AddComponent<Light>();
            existing.type = LightType.Directional;
            var options = EnvironmentBuildOptions.Default;
            options.camera = false;
            var c = EnvironmentBuilder.Build(Preset(BuiltInEnvironment.Day), options);
            Track(c.gameObject);
            Assert.AreSame(existing, c.sun);
        }

        [Test]
        public void Builder_WithTimeOfDay_AddsCycle()
        {
            var options = EnvironmentBuildOptions.Default;
            options.camera = false;
            options.timeOfDay = true;
            var c = EnvironmentBuilder.Build(Preset(BuiltInEnvironment.Day), options, default, null,
                (Preset(BuiltInEnvironment.Day), Preset(BuiltInEnvironment.Sunset), Preset(BuiltInEnvironment.Night)));
            Track(c.gameObject);
            var tod = c.GetComponent<TimeOfDay>();
            Assert.IsNotNull(tod);
            Assert.AreEqual(6, tod.keys.Count);
            Assert.AreSame(tod, c.Driver);
        }

#if ATTHREE_URP
        // ------------------------------------------------------------ URP post

        [Test]
        public void Post_WritesVolumeOverrides()
        {
            var c = MakeController(out _);
            var volume = Track(new GameObject("Volume")).AddComponent<Volume>();
            volume.isGlobal = true;
            c.volume = volume;
            var sunset = Preset(BuiltInEnvironment.Sunset);
            c.SetPreset(sunset);

            var p = volume.profile;
            Assert.IsTrue(p.TryGet(out Bloom bloom));
            Assert.AreEqual(sunset.settings.post.bloomIntensity, bloom.intensity.value, 1e-4f);
            Assert.IsTrue(bloom.intensity.overrideState);
            Assert.IsTrue(p.TryGet(out Tonemapping tone));
            Assert.AreEqual(TonemappingMode.ACES, tone.mode.value);
            Assert.IsTrue(p.TryGet(out Vignette vignette));
            Assert.AreEqual(sunset.settings.post.vignetteIntensity, vignette.intensity.value, 1e-4f);
            Assert.IsTrue(p.TryGet(out WhiteBalance wb));
            Assert.AreEqual(sunset.settings.post.temperature, wb.temperature.value, 1e-4f);
            Assert.AreEqual(1f, volume.weight);
        }

        [Test]
        public void Post_Disabled_ZeroesVolumeWeight()
        {
            var c = MakeController(out _);
            var volume = Track(new GameObject("Volume")).AddComponent<Volume>();
            c.volume = volume;
            var preset = Preset(BuiltInEnvironment.Day);
            preset.settings.post.enabled = false;
            c.SetPreset(preset);
            Assert.AreEqual(0f, volume.weight);
        }

        [Test]
        public void Post_DoesNotModifySharedProfileAsset()
        {
            var shared = Track(ScriptableObject.CreateInstance<VolumeProfile>());
            var c = MakeController(out _);
            var volume = Track(new GameObject("Volume")).AddComponent<Volume>();
            volume.sharedProfile = shared;
            c.volume = volume;
            c.SetPreset(Preset(BuiltInEnvironment.Night));
            Assert.IsFalse(shared.Has<Bloom>(), "Play modunda paylaşılan profil değil, kopyası değişmeli.");
        }
#endif
    }
}
