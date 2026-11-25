using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace Atthree.Environments
{
    /// <summary>Gökyüzü nasıl çizilecek.</summary>
    public enum SkyMode
    {
        /// <summary>Unity'nin Skybox/Procedural shader'ı (güneş diski, atmosfer).</summary>
        Procedural,
        /// <summary>Düz renk arka plan (kapalı mekan, yoğun sis).</summary>
        SolidColor,
        /// <summary>Kendi skybox materyalin (HDRI, cubemap...).</summary>
        CustomMaterial
    }

    /// <summary>URP Tonemapping modu (URP yoksa yok sayılır).</summary>
    public enum TonemapMode { None, Neutral, ACES }

    [Serializable]
    public struct SunSettings
    {
        public bool enabled;
        [Tooltip("Ufkun üstündeki açı. 90 = tam tepede.")]
        [Range(-90f, 90f)] public float pitch;
        [Tooltip("Güneşin geldiği yön (Y ekseni etrafında).")]
        [Range(-180f, 180f)] public float yaw;
        public Color color;
        [Min(0f)] public float intensity;
        public LightShadows shadows;
        [Range(0f, 1f)] public float shadowStrength;

        /// <summary>Işığın bakacağı yön.</summary>
        public Quaternion Rotation => Quaternion.Euler(pitch, yaw, 0f);
    }

    [Serializable]
    public struct SkySettings
    {
        public SkyMode mode;
        [Header("Procedural")]
        public Color skyTint;
        public Color groundColor;
        [Range(0f, 8f)] public float exposure;
        [Range(0f, 1f)] public float sunSize;
        [Range(0f, 5f)] public float atmosphereThickness;
        [Header("Solid Color")]
        public Color solidColor;
        [Header("Custom Material")]
        public Material customMaterial;
    }

    [Serializable]
    public struct AmbientSettings
    {
        [Tooltip("Skybox: ışığı gökyüzünden alır. Trilight: gök/ufuk/zemin renkleri. Flat: tek renk.")]
        public AmbientMode mode;
        [ColorUsage(false, true)] public Color skyColor;
        [ColorUsage(false, true)] public Color equatorColor;
        [ColorUsage(false, true)] public Color groundColor;
        [Tooltip("Sadece Skybox modunda kullanılır.")]
        [Range(0f, 8f)] public float intensity;
        [Range(0f, 1f)] public float reflectionIntensity;
    }

    [Serializable]
    public struct FogSettings
    {
        public bool enabled;
        public FogMode mode;
        public Color color;
        [Tooltip("Exponential modlarda kullanılır.")]
        [Min(0f)] public float density;
        [Tooltip("Linear modda kullanılır.")]
        [Min(0f)] public float start;
        [Min(0f)] public float end;
    }

    [Serializable]
    public struct PostSettings
    {
        [Tooltip("URP Volume ile post-processing. URP yoksa yok sayılır.")]
        public bool enabled;
        public TonemapMode tonemapping;

        [Header("Color")]
        public float postExposure;
        [Range(-100f, 100f)] public float contrast;
        [Range(-100f, 100f)] public float saturation;
        [ColorUsage(false, true)] public Color colorFilter;
        [Range(-100f, 100f)] public float temperature;
        [Range(-100f, 100f)] public float tint;

        [Header("Bloom")]
        [Min(0f)] public float bloomIntensity;
        [Min(0f)] public float bloomThreshold;
        [Range(0f, 1f)] public float bloomScatter;
        public Color bloomTint;

        [Header("Vignette")]
        [Range(0f, 1f)] public float vignetteIntensity;
        [Range(0.01f, 1f)] public float vignetteSmoothness;
        public Color vignetteColor;

        /// <summary>Görüntüyü değiştirmeyen post ayarları (geçişlerde "kapalı" ucu).</summary>
        public static PostSettings Neutral(TonemapMode tonemapping) => new PostSettings
        {
            enabled = false,
            tonemapping = tonemapping,
            colorFilter = Color.white,
            bloomThreshold = 1f,
            bloomScatter = 0.7f,
            bloomTint = Color.white,
            vignetteSmoothness = 0.4f,
            vignetteColor = Color.black,
        };
    }

    /// <summary>
    /// Bir ortamın tüm ayarları: güneş, gökyüzü, ortam ışığı, sis, post-processing.
    /// Struct olduğu için kopyalamak ve <see cref="Lerp"/> ile karıştırmak bellek ayırmaz.
    /// </summary>
    [Serializable]
    public struct EnvironmentSettings
    {
        public SunSettings sun;
        public SkySettings sky;
        public AmbientSettings ambient;
        public FogSettings fog;
        public PostSettings post;

        /// <summary>
        /// İki ortam arasında karışım. t = 0 → a, t = 1 → b.
        /// Açık/kapalı olan şeyler (güneş, sis, post) yumuşakça söner/belirir; enum'lar (mod seçimleri) yarı yolda değişir.
        /// </summary>
        public static EnvironmentSettings Lerp(in EnvironmentSettings a, in EnvironmentSettings b, float t)
        {
            t = Mathf.Clamp01(t);
            return new EnvironmentSettings
            {
                sun = LerpSun(a.sun, b.sun, t),
                sky = LerpSky(a.sky, b.sky, t),
                ambient = LerpAmbient(a.ambient, b.ambient, t),
                fog = LerpFog(a.fog, b.fog, t),
                post = LerpPost(a.post, b.post, t),
            };
        }

        static SunSettings LerpSun(SunSettings a, SunSettings b, float t)
        {
            // Kapalı ucu "aynı ışık, şiddet 0" gibi düşün: yön/renk zıplamaz, sadece söner.
            if (!a.enabled && b.enabled) { a = Off(b); }
            else if (a.enabled && !b.enabled) { b = Off(a); }

            return new SunSettings
            {
                enabled = a.enabled || b.enabled,
                pitch = Mathf.Lerp(a.pitch, b.pitch, t),
                yaw = Mathf.LerpAngle(a.yaw, b.yaw, t),
                color = Color.Lerp(a.color, b.color, t),
                intensity = Mathf.Lerp(a.intensity, b.intensity, t),
                shadows = t < 0.5f ? a.shadows : b.shadows,
                shadowStrength = Mathf.Lerp(a.shadowStrength, b.shadowStrength, t),
            };

            static SunSettings Off(SunSettings like) { like.intensity = 0f; return like; }
        }

        static SkySettings LerpSky(SkySettings a, SkySettings b, float t)
        {
            var near = t < 0.5f ? a : b;
            return new SkySettings
            {
                mode = near.mode,
                skyTint = Color.Lerp(a.skyTint, b.skyTint, t),
                groundColor = Color.Lerp(a.groundColor, b.groundColor, t),
                exposure = Mathf.Lerp(a.exposure, b.exposure, t),
                sunSize = Mathf.Lerp(a.sunSize, b.sunSize, t),
                atmosphereThickness = Mathf.Lerp(a.atmosphereThickness, b.atmosphereThickness, t),
                solidColor = Color.Lerp(a.solidColor, b.solidColor, t),
                customMaterial = near.customMaterial,
            };
        }

        static AmbientSettings LerpAmbient(AmbientSettings a, AmbientSettings b, float t)
        {
            return new AmbientSettings
            {
                mode = t < 0.5f ? a.mode : b.mode,
                skyColor = Color.Lerp(a.skyColor, b.skyColor, t),
                equatorColor = Color.Lerp(a.equatorColor, b.equatorColor, t),
                groundColor = Color.Lerp(a.groundColor, b.groundColor, t),
                intensity = Mathf.Lerp(a.intensity, b.intensity, t),
                reflectionIntensity = Mathf.Lerp(a.reflectionIntensity, b.reflectionIntensity, t),
            };
        }

        static FogSettings LerpFog(FogSettings a, FogSettings b, float t)
        {
            if (!a.enabled && b.enabled) a = Off(b);
            else if (a.enabled && !b.enabled) b = Off(a);

            return new FogSettings
            {
                enabled = a.enabled || b.enabled,
                mode = t < 0.5f ? a.mode : b.mode,
                color = Color.Lerp(a.color, b.color, t),
                density = Mathf.Lerp(a.density, b.density, t),
                start = Mathf.Lerp(a.start, b.start, t),
                end = Mathf.Lerp(a.end, b.end, t),
            };

            // Sis "yok" = yoğunluk 0, linear modda çok uzakta başlıyor.
            static FogSettings Off(FogSettings like)
            {
                like.density = 0f;
                like.start = 1000f;
                like.end = 2000f;
                return like;
            }
        }

        static PostSettings LerpPost(PostSettings a, PostSettings b, float t)
        {
            if (!a.enabled && b.enabled) a = PostSettings.Neutral(b.tonemapping);
            else if (a.enabled && !b.enabled) b = PostSettings.Neutral(a.tonemapping);

            return new PostSettings
            {
                enabled = a.enabled || b.enabled,
                tonemapping = t < 0.5f ? a.tonemapping : b.tonemapping,
                postExposure = Mathf.Lerp(a.postExposure, b.postExposure, t),
                contrast = Mathf.Lerp(a.contrast, b.contrast, t),
                saturation = Mathf.Lerp(a.saturation, b.saturation, t),
                colorFilter = Color.Lerp(a.colorFilter, b.colorFilter, t),
                temperature = Mathf.Lerp(a.temperature, b.temperature, t),
                tint = Mathf.Lerp(a.tint, b.tint, t),
                bloomIntensity = Mathf.Lerp(a.bloomIntensity, b.bloomIntensity, t),
                bloomThreshold = Mathf.Lerp(a.bloomThreshold, b.bloomThreshold, t),
                bloomScatter = Mathf.Lerp(a.bloomScatter, b.bloomScatter, t),
                bloomTint = Color.Lerp(a.bloomTint, b.bloomTint, t),
                vignetteIntensity = Mathf.Lerp(a.vignetteIntensity, b.vignetteIntensity, t),
                vignetteSmoothness = Mathf.Lerp(a.vignetteSmoothness, b.vignetteSmoothness, t),
                vignetteColor = Color.Lerp(a.vignetteColor, b.vignetteColor, t),
            };
        }
    }
}
