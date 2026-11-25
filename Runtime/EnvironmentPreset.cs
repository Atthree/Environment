using System;
using UnityEngine;

namespace Atthree.Environments
{
    /// <summary>
    /// Kaydedilmiş bir ortam (Gündüz, Gece, Zindan...). Sahneye <see cref="EnvironmentController"/> ile uygulanır.
    /// Oluşturmak için: Project penceresinde sağ tık > Create > Atthree > Environment > Preset.
    /// </summary>
    [CreateAssetMenu(menuName = "Atthree/Environment/Preset", fileName = "EnvironmentPreset", order = 0)]
    public class EnvironmentPreset : ScriptableObject
    {
        [TextArea(1, 3)] public string description;
        public EnvironmentSettings settings = EnvironmentPresets.Day();

        /// <summary>Bir preset Inspector'da değiştirildiğinde (editörde canlı önizleme için).</summary>
        public static event Action<EnvironmentPreset> Edited;

        /// <summary>Koddan preset oluşturur (kaydedilmez).</summary>
        public static EnvironmentPreset Create(string presetName, in EnvironmentSettings settings, string description = "")
        {
            var preset = CreateInstance<EnvironmentPreset>();
            preset.name = presetName;
            preset.settings = settings;
            preset.description = description;
            return preset;
        }

        /// <summary>Paketle gelen hazır ayarlardan birini koddan oluşturur.</summary>
        public static EnvironmentPreset Create(BuiltInEnvironment builtIn)
        {
            return Create(builtIn.ToString(), EnvironmentPresets.Get(builtIn), EnvironmentPresets.Describe(builtIn));
        }

        /// <summary>Ayarları koddan değiştirdiysen, bu preset'i kullanan sahneleri güncellemek için çağır.</summary>
        public void NotifyEdited() => Edited?.Invoke(this);

        void OnValidate() => Edited?.Invoke(this);
    }
}
