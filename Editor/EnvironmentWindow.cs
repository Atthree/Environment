using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Atthree.Environments.Editor
{
    /// <summary>Window > Atthree > Environment: preset seç, neleri kuracağını işaretle, tek tıkla sahneye uygula.</summary>
    public class EnvironmentWindow : EditorWindow
    {
        const string PrefPrefix = "Atthree.Environment.";

        List<EnvironmentPreset> presets = new List<EnvironmentPreset>();
        EnvironmentPreset selected;
        EnvironmentBuildOptions options;
        string assetFolder;
        Vector2 scroll;
        GUIStyle cardStyle, cardSelectedStyle, wrapLabel;

        [MenuItem("Window/Atthree/Environment", false, 100)]
        public static void Open()
        {
            var w = GetWindow<EnvironmentWindow>();
            w.titleContent = new GUIContent("Environment", EditorGUIUtility.IconContent("d_SceneViewLighting").image);
            w.minSize = new Vector2(340, 420);
            w.Show();
        }

        void OnEnable()
        {
            options = new EnvironmentBuildOptions
            {
                sun = EditorPrefs.GetBool(PrefPrefix + "sun", true),
                postProcessing = EditorPrefs.GetBool(PrefPrefix + "post", true),
                ground = EditorPrefs.GetBool(PrefPrefix + "ground", true),
                camera = EditorPrefs.GetBool(PrefPrefix + "camera", true),
                timeOfDay = EditorPrefs.GetBool(PrefPrefix + "tod", false),
            };
            assetFolder = EditorPrefs.GetString(PrefPrefix + "folder", EnvironmentEditorUtility.DefaultAssetFolder);
            Refresh();
            var lastPath = EditorPrefs.GetString(PrefPrefix + "selected", "");
            var last = string.IsNullOrEmpty(lastPath) ? null : AssetDatabase.LoadAssetAtPath<EnvironmentPreset>(lastPath);
            selected = last != null ? last : (presets.Count > 0 ? presets[0] : null);
        }

        void OnDisable()
        {
            EditorPrefs.SetBool(PrefPrefix + "sun", options.sun);
            EditorPrefs.SetBool(PrefPrefix + "post", options.postProcessing);
            EditorPrefs.SetBool(PrefPrefix + "ground", options.ground);
            EditorPrefs.SetBool(PrefPrefix + "camera", options.camera);
            EditorPrefs.SetBool(PrefPrefix + "tod", options.timeOfDay);
            EditorPrefs.SetString(PrefPrefix + "folder", assetFolder);
            EditorPrefs.SetString(PrefPrefix + "selected", selected != null ? AssetDatabase.GetAssetPath(selected) : "");
        }

        void OnFocus() => Refresh();
        void OnProjectChange() { Refresh(); Repaint(); }
        void OnHierarchyChange() => Repaint();

        void Refresh()
        {
            presets = EnvironmentEditorUtility.FindAllPresets();
            if (selected == null && presets.Count > 0) selected = presets[0];
        }

        void InitStyles()
        {
            if (cardStyle != null) return;
            cardStyle = new GUIStyle("Button") { alignment = TextAnchor.LowerCenter, fixedHeight = 64, fontStyle = FontStyle.Bold, padding = new RectOffset(4, 4, 4, 6) };
            cardSelectedStyle = new GUIStyle(cardStyle);
            cardSelectedStyle.normal = cardSelectedStyle.active;
            wrapLabel = new GUIStyle(EditorStyles.label) { wordWrap = true };
        }

        void OnGUI()
        {
            InitStyles();
            scroll = EditorGUILayout.BeginScrollView(scroll);

            DrawStatus();
            EditorGUILayout.Space(6);
            DrawPresetGrid();
            EditorGUILayout.Space(6);
            DrawOptions();
            EditorGUILayout.Space(8);
            DrawActions();

            EditorGUILayout.EndScrollView();
        }

        void DrawStatus()
        {
            var controller = Object.FindAnyObjectByType<EnvironmentController>();
            using (new EditorGUILayout.HorizontalScope(EditorStyles.helpBox))
            {
                if (controller == null)
                {
                    EditorGUILayout.LabelField("This scene has no environment yet.", EditorStyles.miniLabel);
                }
                else
                {
                    string p = controller.Preset != null ? controller.Preset.name : "none";
                    string driven = controller.Driver != null ? "  (driven by Time Of Day)" : "";
                    EditorGUILayout.LabelField($"Scene environment: {p}{driven}", EditorStyles.miniLabel);
                    if (GUILayout.Button("Select", EditorStyles.miniButton, GUILayout.Width(52))) Selection.activeGameObject = controller.gameObject;
                }
            }

            if (!EnvironmentEditorUtility.HasUrp)
                EditorGUILayout.HelpBox("URP is not installed: post-processing is skipped. Sun, sky, ambient light and fog still work.", MessageType.Info);
            else if (!EnvironmentEditorUtility.UsingUrpPipeline)
                EditorGUILayout.HelpBox("URP is installed but not the active render pipeline (Project Settings > Graphics). Post-processing will not be visible.", MessageType.Warning);
        }

        void DrawPresetGrid()
        {
            EditorGUILayout.LabelField("Preset", EditorStyles.boldLabel);
            if (presets.Count == 0)
            {
                EditorGUILayout.HelpBox("No presets found. Create one: Project window > right click > Create > Atthree > Environment > Preset.", MessageType.Info);
                return;
            }

            int columns = Mathf.Max(1, Mathf.FloorToInt((position.width - 20f) / 104f));
            for (int i = 0; i < presets.Count; i += columns)
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    for (int c = 0; c < columns; c++)
                    {
                        int idx = i + c;
                        if (idx >= presets.Count) { GUILayout.FlexibleSpace(); continue; }
                        DrawCard(presets[idx]);
                    }
                }
            }

            if (selected != null && !string.IsNullOrEmpty(selected.description))
                EditorGUILayout.LabelField(selected.description, wrapLabel);
        }

        void DrawCard(EnvironmentPreset p)
        {
            bool isSel = p == selected;
            var rect = GUILayoutUtility.GetRect(new GUIContent(p.name), isSel ? cardSelectedStyle : cardStyle, GUILayout.MinWidth(96));
            if (GUI.Button(rect, p.name, isSel ? cardSelectedStyle : cardStyle))
            {
                selected = p;
                if (Event.current.clickCount == 2) EditorGUIUtility.PingObject(p);
            }

            // Renk önizlemesi: üst yarı gökyüzü, ortada güneş, alt şerit sis.
            var s = p.settings;
            var inner = new Rect(rect.x + 5, rect.y + 5, rect.width - 10, rect.height - 26);
            Color sky = s.sky.mode == SkyMode.SolidColor ? s.sky.solidColor : Color.Lerp(s.sky.skyTint * Mathf.Clamp(s.sky.exposure, 0.2f, 1.6f), s.ambient.skyColor, 0.5f);
            Color horizon = s.fog.enabled ? s.fog.color : s.ambient.equatorColor;
            EditorGUI.DrawRect(new Rect(inner.x, inner.y, inner.width, inner.height * 0.65f), Opaque(sky));
            EditorGUI.DrawRect(new Rect(inner.x, inner.y + inner.height * 0.65f, inner.width, inner.height * 0.35f), Opaque(horizon));
            if (s.sun.enabled && s.sun.intensity > 0f)
            {
                float size = 8f;
                float y = inner.y + inner.height * 0.65f - size - Mathf.Clamp01(s.sun.pitch / 90f) * (inner.height * 0.5f);
                EditorGUI.DrawRect(new Rect(inner.center.x - size / 2, y, size, size), Opaque(s.sun.color));
            }
            if (EnvironmentEditorUtility.IsReadOnly(p))
                GUI.Label(new Rect(inner.xMax - 14, inner.y, 14, 14), new GUIContent("•", "Built-in (read-only). Use 'Copy to project' to edit."), EditorStyles.whiteMiniLabel);
        }

        static Color Opaque(Color c) { c.a = 1f; return c; }

        void DrawOptions()
        {
            EditorGUILayout.LabelField("Build", EditorStyles.boldLabel);
            options.sun = EditorGUILayout.ToggleLeft(new GUIContent("Sun (Directional Light)", "Sahnede varsa mevcut Directional Light kullanılır."), options.sun);
            using (new EditorGUI.DisabledScope(!EnvironmentEditorUtility.HasUrp))
                options.postProcessing = EditorGUILayout.ToggleLeft(new GUIContent("Post-processing (URP Global Volume)", "Bloom, tonemapping, renk, vinyet."), options.postProcessing && EnvironmentEditorUtility.HasUrp);
            options.ground = EditorGUILayout.ToggleLeft(new GUIContent("Ground (200 × 200 m)", "Kendi zeminin/haritan varsa kapat."), options.ground);
            options.camera = EditorGUILayout.ToggleLeft(new GUIContent("Camera (if the scene has none)", "Main Camera yoksa oluşturur; varsa post-processing'i açar."), options.camera);
            options.timeOfDay = EditorGUILayout.ToggleLeft(new GUIContent("Day/night cycle (Time Of Day)", "Gece → şafak → gündüz → gün batımı döngüsü. Paketteki Day/Sunset/Night preset'lerini kullanır."), options.timeOfDay);

            using (new EditorGUILayout.HorizontalScope())
            {
                assetFolder = EditorGUILayout.TextField(new GUIContent("Asset folder", "Sahneye özel skybox/zemin materyali ve Volume Profile buraya kaydedilir."), assetFolder);
                if (GUILayout.Button("…", GUILayout.Width(24)))
                {
                    var abs = EditorUtility.OpenFolderPanel("Environment asset folder", "Assets", "");
                    if (!string.IsNullOrEmpty(abs) && abs.StartsWith(Application.dataPath))
                        assetFolder = "Assets" + abs.Substring(Application.dataPath.Length);
                }
            }
        }

        void DrawActions()
        {
            bool canBuild = selected != null && assetFolder.StartsWith("Assets");
            using (new EditorGUI.DisabledScope(!canBuild || Application.isPlaying))
            {
                if (GUILayout.Button(new GUIContent("Apply to Current Scene", "Ortamı kurar ya da var olanı günceller. Ctrl+Z ile geri alınır."), GUILayout.Height(30)))
                    EnvironmentEditorUtility.BuildInActiveScene(selected, options, assetFolder);

                if (GUILayout.Button(new GUIContent("New Scene with This Environment", "Boş yeni sahne açar ve kurar.")))
                    EnvironmentEditorUtility.BuildInNewScene(selected, options, assetFolder);
            }

            if (Application.isPlaying)
            {
                var controller = Object.FindAnyObjectByType<EnvironmentController>();
                using (new EditorGUI.DisabledScope(controller == null || selected == null))
                    if (GUILayout.Button("Transition to Preset (Play mode, 1.5 s)"))
                        controller.TransitionTo(selected, 1.5f);
            }

            EditorGUILayout.Space(4);
            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUI.DisabledScope(selected == null))
                {
                    if (GUILayout.Button(new GUIContent("Copy to Project", "Seçili preset'in düzenlenebilir kopyasını oluşturur.")))
                    {
                        selected = EnvironmentEditorUtility.CopyToProject(selected, assetFolder);
                        Refresh();
                    }
                    if (GUILayout.Button("Edit Preset")) { Selection.activeObject = selected; EditorGUIUtility.PingObject(selected); }
                }
            }
        }
    }
}
