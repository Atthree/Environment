using UnityEditor;
using UnityEngine;

namespace Atthree.Environments.Editor
{
    [CustomEditor(typeof(EnvironmentController))]
    public class EnvironmentControllerEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            var controller = (EnvironmentController)target;

            if (controller.Driver != null)
                EditorGUILayout.HelpBox($"Driven by {controller.Driver.GetType().Name}: the look comes from its keys, not from the Preset field.", MessageType.Info);

            DrawDefaultInspector();

            EditorGUILayout.Space();
            EditorGUILayout.LabelField(Application.isPlaying ? "Transition to (1.5 s)" : "Apply preset", EditorStyles.boldLabel);
            var presets = EnvironmentEditorUtility.FindAllPresets();
            int columns = Mathf.Max(1, Mathf.FloorToInt((EditorGUIUtility.currentViewWidth - 30f) / 90f));
            for (int i = 0; i < presets.Count; i += columns)
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    for (int c = 0; c < columns && i + c < presets.Count; c++)
                    {
                        var p = presets[i + c];
                        bool current = controller.Preset == p;
                        using (new EditorGUI.DisabledScope(current && !Application.isPlaying))
                            if (GUILayout.Button(current ? $"● {p.name}" : p.name))
                                EnvironmentEditorUtility.ApplyPreset(controller, p);
                    }
                }
            }

            if (Application.isPlaying && controller.IsTransitioning)
            {
                var r = GUILayoutUtility.GetRect(18, 18);
                EditorGUI.ProgressBar(r, controller.TransitionProgress, "Transitioning…");
                Repaint();
            }

            EditorGUILayout.Space();
            using (new EditorGUILayout.HorizontalScope())
            {
                if (!Application.isPlaying && GUILayout.Button("Reapply")) EnvironmentEditorUtility.ApplyPreset(controller, controller.Preset);
                if (GUILayout.Button("Open Environment Window")) EnvironmentWindow.Open();
            }
        }
    }

    [CustomEditor(typeof(EnvironmentPreset))]
    public class EnvironmentPresetEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            var preset = (EnvironmentPreset)target;
            bool readOnly = EnvironmentEditorUtility.IsReadOnly(preset);
            if (readOnly)
            {
                EditorGUILayout.HelpBox("Built-in preset (read-only, inside the package). Copy it to your project to edit.", MessageType.Info);
                if (GUILayout.Button("Copy to Project")) { EnvironmentEditorUtility.CopyToProject(preset); GUIUtility.ExitGUI(); }
                EditorGUILayout.Space();
            }

            using (new EditorGUI.DisabledScope(readOnly))
                DrawDefaultInspector();

            EditorGUILayout.Space();
            var controller = Object.FindAnyObjectByType<EnvironmentController>();
            if (controller != null)
            {
                string label = Application.isPlaying ? "Transition Scene to This (1.5 s)" : "Apply to Scene";
                if (GUILayout.Button(label, GUILayout.Height(26))) EnvironmentEditorUtility.ApplyPreset(controller, preset);
                if (!Application.isPlaying && controller.Preset == preset && controller.Driver == null)
                    EditorGUILayout.LabelField("The scene uses this preset: edits show up live.", EditorStyles.miniLabel);
            }
            else if (!Application.isPlaying)
            {
                if (GUILayout.Button("Build Environment with This Preset", GUILayout.Height(26)))
                    EnvironmentEditorUtility.BuildInActiveScene(preset, EnvironmentBuildOptions.Default);
            }
        }
    }
}
