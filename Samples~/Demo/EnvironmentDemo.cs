using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Atthree.Environments.Samples
{
    /// <summary>
    /// Kendini kuran demo. Boş bir sahnede boş bir objeye ekle ve Play'e bas:
    ///  • Sol üstteki düğmelerle Day / Sunset / Night / Dungeon / Foggy arasında 2 saniyelik geçiş.
    ///  • "Day/Night Cycle" ile gün döngüsünü aç; saat kaydırıcısı ve hız ayarı.
    ///  • Kamera sahnenin etrafında yavaşça döner. Metal küre gökyüzünü yansıtır, zindan ve gecede meşaleler yanar.
    /// </summary>
    public class EnvironmentDemo : MonoBehaviour
    {
        public float transitionSeconds = 2f;
        public bool orbitCamera = true;

        EnvironmentController controller;
        TimeOfDay cycle;
        readonly Dictionary<BuiltInEnvironment, EnvironmentPreset> presets = new Dictionary<BuiltInEnvironment, EnvironmentPreset>();
        readonly List<Light> torches = new List<Light>();
        Transform cam;
        float orbitAngle;

        void Start()
        {
            foreach (BuiltInEnvironment env in System.Enum.GetValues(typeof(BuiltInEnvironment)))
                presets[env] = EnvironmentPreset.Create(env);

            var options = EnvironmentBuildOptions.Default;
            controller = EnvironmentBuilder.Build(presets[BuiltInEnvironment.Day], options);
            cam = Camera.main.transform;

            BuildProps();

            cycle = controller.gameObject.AddComponent<TimeOfDay>();
            cycle.keys = TimeOfDay.CreateDefaultKeys(presets[BuiltInEnvironment.Day], presets[BuiltInEnvironment.Sunset], presets[BuiltInEnvironment.Night]);
            cycle.MarkKeysDirty();
            cycle.dayLengthMinutes = 1f;
            cycle.hour = 9f;
            cycle.enabled = false; // düğmeyle açılır

            controller.PresetChanged += p => Debug.Log($"[EnvironmentDemo] Environment is now {p.name}");
        }

        void Update()
        {
            // Meşaleler sadece karanlıkta yansın ve titresin.
            float darkness = 1f - Mathf.Clamp01(controller.CurrentSettings.sun.enabled ? controller.CurrentSettings.sun.intensity : 0f);
            foreach (var t in torches)
            {
                t.intensity = darkness * (4f + Mathf.PerlinNoise(Time.time * 6f, t.GetInstanceID()) * 2.5f);
                t.enabled = t.intensity > 0.05f;
            }

            if (orbitCamera && cam != null)
            {
                orbitAngle += Time.deltaTime * 6f;
                var rot = Quaternion.Euler(0f, orbitAngle, 0f);
                cam.position = rot * new Vector3(0f, 4.5f, -14f);
                cam.LookAt(new Vector3(0f, 1.2f, 0f));
            }
        }

        void OnGUI()
        {
            GUILayout.BeginArea(new Rect(12, 12, 230, 400), GUI.skin.box);
            GUILayout.Label("<b>Environment</b>", new GUIStyle(GUI.skin.label) { richText = true, fontSize = 14 });

            GUI.enabled = !cycle.enabled;
            foreach (var kv in presets)
            {
                bool current = controller.Preset == kv.Value;
                if (GUILayout.Button(current ? $"● {kv.Key}" : kv.Key.ToString()))
                    controller.TransitionTo(kv.Value, transitionSeconds);
            }
            GUI.enabled = true;

            if (controller.IsTransitioning)
                GUILayout.Label($"Transition… {controller.TransitionProgress * 100f:0}%");

            GUILayout.Space(8);
            bool on = GUILayout.Toggle(cycle.enabled, " Day/Night Cycle");
            if (on != cycle.enabled)
            {
                cycle.enabled = on;
                if (!on) controller.TransitionTo(presets[BuiltInEnvironment.Day], transitionSeconds);
            }

            if (cycle.enabled)
            {
                int hh = Mathf.FloorToInt(cycle.Hour);
                int mm = Mathf.FloorToInt((cycle.Hour - hh) * 60f);
                GUILayout.Label($"Time {hh:00}:{mm:00}  {(cycle.IsNight ? "(night)" : "")}");
                float h = GUILayout.HorizontalSlider(cycle.Hour, 0f, 23.99f);
                if (!Mathf.Approximately(h, cycle.Hour)) cycle.Hour = h;
                GUILayout.Space(6);
                GUILayout.Label($"Day length: {cycle.dayLengthMinutes:0.0} min");
                cycle.dayLengthMinutes = GUILayout.HorizontalSlider(cycle.dayLengthMinutes, 0.2f, 5f);
                cycle.running = GUILayout.Toggle(cycle.running, " Running");
            }

            GUILayout.Space(8);
            orbitCamera = GUILayout.Toggle(orbitCamera, " Orbit camera");
            GUILayout.EndArea();
        }

        void BuildProps()
        {
            Material Mat(Color c, float metallic, float smoothness)
            {
                var rp = GraphicsSettings.currentRenderPipeline;
                var m = rp != null && rp.defaultMaterial != null ? new Material(rp.defaultMaterial) : new Material(Shader.Find("Standard"));
                if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
                if (m.HasProperty("_Color")) m.SetColor("_Color", c);
                if (m.HasProperty("_Metallic")) m.SetFloat("_Metallic", metallic);
                if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", smoothness);
                if (m.HasProperty("_Glossiness")) m.SetFloat("_Glossiness", smoothness);
                return m;
            }

            GameObject Prim(PrimitiveType type, Vector3 pos, Vector3 scale, Material m, string name)
            {
                var go = GameObject.CreatePrimitive(type);
                go.name = name;
                go.transform.SetParent(transform, false);
                go.transform.position = pos;
                go.transform.localScale = scale;
                go.GetComponent<Renderer>().sharedMaterial = m;
                return go;
            }

            var stone = Mat(new Color(0.68f, 0.66f, 0.6f), 0f, 0.2f);
            Prim(PrimitiveType.Cube, new Vector3(-3.5f, 1f, 0f), new Vector3(2f, 2f, 2f), Mat(new Color(0.8f, 0.25f, 0.2f), 0f, 0.35f), "Red Cube");
            Prim(PrimitiveType.Sphere, new Vector3(0f, 1.1f, 0f), Vector3.one * 2.2f, Mat(new Color(0.95f, 0.95f, 0.95f), 1f, 0.92f), "Chrome Sphere");
            Prim(PrimitiveType.Capsule, new Vector3(3.5f, 1f, 0f), Vector3.one, Mat(new Color(0.3f, 0.5f, 0.85f), 0f, 0.55f), "Blue Capsule");

            // Uzaklaşan sütunlar: sis ve gölge yönü burada belli olur.
            for (int i = 0; i < 12; i++)
            {
                float a = i / 12f * Mathf.PI * 2f;
                Prim(PrimitiveType.Cube, new Vector3(Mathf.Cos(a) * 11f, 2.5f, Mathf.Sin(a) * 11f), new Vector3(1f, 5f, 1f), stone, "Pillar");
                Prim(PrimitiveType.Cube, new Vector3(Mathf.Cos(a) * 28f, 4f, Mathf.Sin(a) * 28f), new Vector3(2f, 8f, 2f), stone, "Far Pillar");
            }

            // Meşaleler
            for (int i = 0; i < 4; i++)
            {
                float a = (i + 0.5f) / 4f * Mathf.PI * 2f;
                var pos = new Vector3(Mathf.Cos(a) * 6f, 0f, Mathf.Sin(a) * 6f);
                Prim(PrimitiveType.Cylinder, pos + Vector3.up * 0.75f, new Vector3(0.15f, 0.75f, 0.15f), Mat(new Color(0.25f, 0.18f, 0.12f), 0f, 0.1f), "Torch Pole");
                var light = new GameObject("Torch Light").AddComponent<Light>();
                light.transform.SetParent(transform, false);
                light.transform.position = pos + Vector3.up * 1.8f;
                light.type = LightType.Point;
                light.color = new Color(1f, 0.6f, 0.3f);
                light.range = 9f;
                light.shadows = LightShadows.None;
                torches.Add(light);
            }
        }
    }
}
