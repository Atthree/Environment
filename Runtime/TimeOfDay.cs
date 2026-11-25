using System;
using System.Collections.Generic;
using UnityEngine;

namespace Atthree.Environments
{
    /// <summary>
    /// Gün döngüsü: saate göre preset'ler arasında karışım yapar ve güneşi gökyüzünde döndürür.
    /// Gece güneş ufkun altına inince ışık "ay" olarak karşı taraftan gelir (ufukta yumuşakça söner, zıplamaz).
    /// <see cref="EnvironmentController"/> ile aynı objede çalışır.
    /// </summary>
    [ExecuteAlways]
    [DisallowMultipleComponent]
    [RequireComponent(typeof(EnvironmentController))]
    [AddComponentMenu("Atthree/Environment/Time Of Day")]
    public class TimeOfDay : MonoBehaviour
    {
        [Serializable]
        public struct Key
        {
            [Range(0f, 24f)] public float hour;
            public EnvironmentPreset preset;

            public Key(float hour, EnvironmentPreset preset)
            {
                this.hour = hour;
                this.preset = preset;
            }
        }

        [Tooltip("Hangi saatte hangi ortam. Aradaki saatlerde iki komşu preset karıştırılır; gece yarısından sonra başa sarar.")]
        public List<Key> keys = new List<Key>();

        [Tooltip("Şu anki saat (0-24).")]
        [Range(0f, 24f)] public float hour = 12f;

        [Tooltip("Play modunda saat ilerlesin mi.")]
        public bool running = true;

        [Tooltip("Oyundaki bir günün gerçek dakika cinsinden uzunluğu.")]
        [Min(0.05f)] public float dayLengthMinutes = 10f;

        [Tooltip("Time.timeScale'den bağımsız ilerlesin.")]
        public bool unscaledTime;

        [Header("Sun path")]
        [Tooltip("Güneşin açısını saatten hesapla (preset'lerdeki pitch/yaw yok sayılır).")]
        public bool driveSun = true;
        [Tooltip("Güneşin doğduğu yön (Y ekseni etrafında, derece).")]
        [Range(-180f, 180f)] public float sunriseDirection = -90f;
        [Tooltip("Gökyüzündeki en yüksek açı. 90 = öğlen tam tepede.")]
        [Range(10f, 90f)] public float maxElevation = 65f;
        [Tooltip("Güneş/ay ufka bu açıdan daha yakınken ışık yumuşakça söner.")]
        [Range(0f, 20f)] public float horizonFade = 6f;

        /// <summary>Saat değiştiğinde (her karede ilerlerken de).</summary>
        public event Action<float> HourChanged;

        /// <summary>Gün doğumu (dawn) bu saatte; sun path hesabı için.</summary>
        public const float Sunrise = 6f;
        /// <summary>Gün batımı bu saatte.</summary>
        public const float Sunset = 18f;

        public bool IsNight => hour < Sunrise || hour >= Sunset;

        /// <summary>Gün kaçıncı kez başa sardı (Play başından beri).</summary>
        public int DayCount { get; private set; }

        public float Hour
        {
            get => hour;
            set
            {
                hour = Wrap(value);
                Evaluate();
            }
        }

        EnvironmentController controller;
        readonly List<Key> sorted = new List<Key>();
        bool dirtyKeys = true;
        int sourceCount = -1;
        float lastEvaluatedHour = -1f;

        /// <summary>
        /// Klasik bir gün: gece → şafak → gündüz → gün batımı → gece. dawn boşsa gün batımı preset'i şafak için de kullanılır.
        /// </summary>
        public static List<Key> CreateDefaultKeys(EnvironmentPreset day, EnvironmentPreset sunset, EnvironmentPreset night, EnvironmentPreset dawn = null)
        {
            if (dawn == null) dawn = sunset;
            return new List<Key>
            {
                new Key(4.5f, night),
                new Key(6.5f, dawn),
                new Key(9f, day),
                new Key(16f, day),
                new Key(18.3f, sunset),
                new Key(20.5f, night),
            };
        }

        void OnEnable()
        {
            controller = GetComponent<EnvironmentController>();
            controller.Driver = this;
            EnvironmentPreset.Edited += OnPresetEdited;
            dirtyKeys = true;
            Evaluate();
        }

        void OnDisable()
        {
            EnvironmentPreset.Edited -= OnPresetEdited;
            if (controller != null && controller.Driver == (UnityEngine.Object)this)
            {
                controller.Driver = null;
                controller.Reapply();
            }
        }

        void OnValidate()
        {
            dirtyKeys = true;
            DeferEvaluate();
        }

        void Update()
        {
            if (!Application.isPlaying) return;

            if (running)
            {
                float dt = unscaledTime ? Time.unscaledDeltaTime : Time.deltaTime;
                float next = hour + dt * 24f / (dayLengthMinutes * 60f);
                if (next >= 24f) DayCount++;
                hour = Wrap(next);
            }
            // Duraklatılmışken Inspector'dan saat değişirse de uygula.
            if (hour != lastEvaluatedHour) Evaluate(false);
        }

        void OnPresetEdited(EnvironmentPreset p)
        {
            for (int i = 0; i < keys.Count; i++)
                if (keys[i].preset == p) { DeferEvaluate(); return; }
        }

        void DeferEvaluate()
        {
#if UNITY_EDITOR
            if (!Application.isPlaying)
            {
                // OnValidate içinden ışık/kamera değiştirmek uyarı verir; bir sonraki editör karesine ertele.
                UnityEditor.EditorApplication.delayCall += () => { if (this != null && isActiveAndEnabled) Evaluate(); };
                return;
            }
#endif
            if (isActiveAndEnabled) Evaluate();
        }

        /// <summary>Anahtar listesini koddan değiştirdiysen çağır.</summary>
        public void MarkKeysDirty() => dirtyKeys = true;

        /// <summary>Şu anki saatin ayarlarını hesaplar (sahneye uygulamaz).</summary>
        public bool TryGetSettings(float atHour, out EnvironmentSettings settings)
        {
            settings = default;
            RebuildSorted();
            if (sorted.Count == 0) return false;

            if (sorted.Count == 1)
            {
                settings = sorted[0].preset.settings;
            }
            else
            {
                // atHour'dan önceki son anahtar (yoksa dünün son anahtarı) ve sonraki ilk anahtar (yoksa yarının ilki).
                int next = 0;
                for (int i = 0; i < sorted.Count; i++)
                    if (sorted[i].hour > atHour) { next = i; break; }
                int prev = (next - 1 + sorted.Count) % sorted.Count;

                float from = sorted[prev].hour;
                float to = sorted[next].hour;
                float span = to - from;
                if (span <= 0f) span += 24f;
                float into = atHour - from;
                if (into < 0f) into += 24f;
                float t = span > 0f ? into / span : 0f;
                t = t * t * (3f - 2f * t);

                settings = EnvironmentSettings.Lerp(sorted[prev].preset.settings, sorted[next].preset.settings, t);
            }

            if (driveSun) ApplySunPath(ref settings.sun, atHour);
            return true;
        }

        /// <summary>Şu anki saati sahneye uygular.</summary>
        public void Evaluate() => Evaluate(true);

        void Evaluate(bool refreshNow)
        {
            if (controller == null) controller = GetComponent<EnvironmentController>();
            if (controller == null) return;
            lastEvaluatedHour = hour;
            if (TryGetSettings(hour, out var s)) controller.Apply(s, refreshNow);
            HourChanged?.Invoke(hour);
        }

        void ApplySunPath(ref SunSettings sun, float atHour)
        {
            // Gündüz 6→18 arası güneş, gece 18→30 arası "ay" aynı yayı izler.
            float dayT = Wrap(atHour - Sunrise) / 12f;   // 0..2
            bool moon = dayT >= 1f;
            if (moon) dayT -= 1f;

            float elevation = Mathf.Sin(dayT * Mathf.PI) * maxElevation;     // 0 → max → 0
            float yaw = sunriseDirection + dayT * 180f;                       // doğudan batıya

            sun.pitch = elevation;
            sun.yaw = Mathf.DeltaAngle(0f, yaw);
            if (horizonFade > 0f) sun.intensity *= Mathf.Clamp01(elevation / horizonFade);
            if (sun.intensity <= 0f) sun.enabled = false;
        }

        void RebuildSorted()
        {
            if (!dirtyKeys && keys.Count == sourceCount) return;
            sourceCount = keys.Count;
            sorted.Clear();
            for (int i = 0; i < keys.Count; i++)
                if (keys[i].preset != null) sorted.Add(new Key(Wrap(keys[i].hour), keys[i].preset));
            sorted.Sort((a, b) => a.hour.CompareTo(b.hour));
            dirtyKeys = false;
        }

        static float Wrap(float h)
        {
            h %= 24f;
            if (h < 0f) h += 24f;
            return h;
        }
    }
}
