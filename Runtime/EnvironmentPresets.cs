using UnityEngine;
using UnityEngine.Rendering;

namespace Atthree.Environments
{
    /// <summary>Paketle gelen hazır ortamlar.</summary>
    public enum BuiltInEnvironment { Day, Sunset, Night, Dungeon, Foggy }

    /// <summary>
    /// Hazır ortamların değerleri. Paketteki Presets/*.asset dosyaları bu değerlerden üretildi;
    /// koddan da kullanabilirsin: <c>EnvironmentPreset.Create(BuiltInEnvironment.Night)</c>.
    /// </summary>
    public static class EnvironmentPresets
    {
        public static EnvironmentSettings Get(BuiltInEnvironment env)
        {
            switch (env)
            {
                case BuiltInEnvironment.Sunset: return Sunset();
                case BuiltInEnvironment.Night: return Night();
                case BuiltInEnvironment.Dungeon: return Dungeon();
                case BuiltInEnvironment.Foggy: return Foggy();
                default: return Day();
            }
        }

        public static string Describe(BuiltInEnvironment env)
        {
            switch (env)
            {
                case BuiltInEnvironment.Sunset: return "Low warm sun, long shadows, orange haze.";
                case BuiltInEnvironment.Night: return "Cool moonlight, dark blue sky, soft bloom.";
                case BuiltInEnvironment.Dungeon: return "Indoor: no sun, dark warm ambient, thick fog. Add your own torches.";
                case BuiltInEnvironment.Foggy: return "Overcast, soft shadows, dense grey fog that hides the horizon.";
                default: return "Clear midday sun, blue sky, light haze.";
            }
        }

        static Color C(float r, float g, float b) => new Color(r, g, b, 1f);

        public static EnvironmentSettings Day() => new EnvironmentSettings
        {
            sun = new SunSettings
            {
                enabled = true, pitch = 50f, yaw = -30f,
                color = C(1f, 0.957f, 0.839f), intensity = 1.3f,
                shadows = LightShadows.Soft, shadowStrength = 0.9f,
            },
            sky = new SkySettings
            {
                mode = SkyMode.Procedural,
                skyTint = C(0.5f, 0.5f, 0.5f), groundColor = C(0.37f, 0.35f, 0.34f),
                exposure = 1.3f, sunSize = 0.04f, atmosphereThickness = 1f,
                solidColor = C(0.55f, 0.7f, 0.9f),
            },
            ambient = new AmbientSettings
            {
                mode = AmbientMode.Trilight,
                skyColor = C(0.62f, 0.72f, 0.86f), equatorColor = C(0.55f, 0.58f, 0.6f), groundColor = C(0.3f, 0.28f, 0.25f),
                intensity = 1f, reflectionIntensity = 1f,
            },
            fog = new FogSettings
            {
                enabled = true, mode = FogMode.ExponentialSquared,
                color = C(0.72f, 0.8f, 0.9f), density = 0.004f, start = 50f, end = 300f,
            },
            post = new PostSettings
            {
                enabled = true, tonemapping = TonemapMode.Neutral,
                postExposure = 0f, contrast = 10f, saturation = 10f, colorFilter = Color.white,
                temperature = 0f, tint = 0f,
                bloomIntensity = 0.4f, bloomThreshold = 1f, bloomScatter = 0.7f, bloomTint = Color.white,
                vignetteIntensity = 0.2f, vignetteSmoothness = 0.4f, vignetteColor = Color.black,
            },
        };

        public static EnvironmentSettings Sunset() => new EnvironmentSettings
        {
            sun = new SunSettings
            {
                enabled = true, pitch = 8f, yaw = -60f,
                color = C(1f, 0.56f, 0.3f), intensity = 1.2f,
                shadows = LightShadows.Soft, shadowStrength = 0.85f,
            },
            sky = new SkySettings
            {
                mode = SkyMode.Procedural,
                skyTint = C(0.62f, 0.42f, 0.38f), groundColor = C(0.3f, 0.22f, 0.2f),
                exposure = 1.1f, sunSize = 0.06f, atmosphereThickness = 1.7f,
                solidColor = C(0.85f, 0.5f, 0.35f),
            },
            ambient = new AmbientSettings
            {
                mode = AmbientMode.Trilight,
                skyColor = C(0.5f, 0.42f, 0.5f), equatorColor = C(0.62f, 0.4f, 0.28f), groundColor = C(0.14f, 0.1f, 0.1f),
                intensity = 1f, reflectionIntensity = 0.8f,
            },
            fog = new FogSettings
            {
                enabled = true, mode = FogMode.ExponentialSquared,
                color = C(0.85f, 0.56f, 0.42f), density = 0.008f, start = 30f, end = 200f,
            },
            post = new PostSettings
            {
                enabled = true, tonemapping = TonemapMode.ACES,
                postExposure = 0.3f, contrast = 15f, saturation = 12f, colorFilter = Color.white,
                temperature = 20f, tint = 0f,
                bloomIntensity = 0.8f, bloomThreshold = 0.9f, bloomScatter = 0.7f, bloomTint = C(1f, 0.85f, 0.7f),
                vignetteIntensity = 0.3f, vignetteSmoothness = 0.45f, vignetteColor = C(0.1f, 0.03f, 0f),
            },
        };

        public static EnvironmentSettings Night() => new EnvironmentSettings
        {
            sun = new SunSettings
            {
                enabled = true, pitch = 40f, yaw = 140f,
                color = C(0.55f, 0.66f, 1f), intensity = 0.3f,
                shadows = LightShadows.Soft, shadowStrength = 0.7f,
            },
            sky = new SkySettings
            {
                mode = SkyMode.Procedural,
                skyTint = C(0.12f, 0.15f, 0.25f), groundColor = C(0.03f, 0.03f, 0.04f),
                exposure = 0.15f, sunSize = 0.025f, atmosphereThickness = 0.6f,
                solidColor = C(0.02f, 0.03f, 0.06f),
            },
            ambient = new AmbientSettings
            {
                mode = AmbientMode.Trilight,
                skyColor = C(0.1f, 0.13f, 0.24f), equatorColor = C(0.06f, 0.08f, 0.13f), groundColor = C(0.02f, 0.02f, 0.03f),
                intensity = 1f, reflectionIntensity = 0.5f,
            },
            fog = new FogSettings
            {
                enabled = true, mode = FogMode.ExponentialSquared,
                color = C(0.04f, 0.05f, 0.09f), density = 0.012f, start = 20f, end = 150f,
            },
            post = new PostSettings
            {
                enabled = true, tonemapping = TonemapMode.ACES,
                postExposure = 0.4f, contrast = 10f, saturation = -20f, colorFilter = Color.white,
                temperature = -20f, tint = 0f,
                bloomIntensity = 1f, bloomThreshold = 0.8f, bloomScatter = 0.75f, bloomTint = C(0.8f, 0.85f, 1f),
                vignetteIntensity = 0.35f, vignetteSmoothness = 0.45f, vignetteColor = Color.black,
            },
        };

        public static EnvironmentSettings Dungeon() => new EnvironmentSettings
        {
            sun = new SunSettings
            {
                enabled = false, pitch = 60f, yaw = 0f,
                color = C(1f, 0.8f, 0.6f), intensity = 0f,
                shadows = LightShadows.Soft, shadowStrength = 1f,
            },
            sky = new SkySettings
            {
                mode = SkyMode.SolidColor,
                skyTint = C(0.2f, 0.18f, 0.16f), groundColor = C(0.05f, 0.04f, 0.04f),
                exposure = 0.2f, sunSize = 0.04f, atmosphereThickness = 1f,
                solidColor = C(0.03f, 0.025f, 0.02f),
            },
            ambient = new AmbientSettings
            {
                mode = AmbientMode.Trilight,
                skyColor = C(0.16f, 0.14f, 0.13f), equatorColor = C(0.13f, 0.11f, 0.1f), groundColor = C(0.06f, 0.05f, 0.05f),
                intensity = 1f, reflectionIntensity = 0.3f,
            },
            fog = new FogSettings
            {
                enabled = true, mode = FogMode.ExponentialSquared,
                color = C(0.03f, 0.025f, 0.02f), density = 0.045f, start = 5f, end = 40f,
            },
            post = new PostSettings
            {
                enabled = true, tonemapping = TonemapMode.ACES,
                postExposure = 0.6f, contrast = 20f, saturation = -10f, colorFilter = Color.white,
                temperature = 15f, tint = 0f,
                bloomIntensity = 1.2f, bloomThreshold = 0.8f, bloomScatter = 0.7f, bloomTint = C(1f, 0.8f, 0.6f),
                vignetteIntensity = 0.45f, vignetteSmoothness = 0.5f, vignetteColor = Color.black,
            },
        };

        public static EnvironmentSettings Foggy() => new EnvironmentSettings
        {
            sun = new SunSettings
            {
                enabled = true, pitch = 35f, yaw = -20f,
                color = C(0.85f, 0.88f, 0.9f), intensity = 0.6f,
                shadows = LightShadows.Soft, shadowStrength = 0.4f,
            },
            sky = new SkySettings
            {
                mode = SkyMode.SolidColor,
                skyTint = C(0.6f, 0.62f, 0.65f), groundColor = C(0.5f, 0.5f, 0.5f),
                exposure = 0.9f, sunSize = 0.02f, atmosphereThickness = 2.5f,
                solidColor = C(0.62f, 0.65f, 0.68f),
            },
            ambient = new AmbientSettings
            {
                mode = AmbientMode.Trilight,
                skyColor = C(0.62f, 0.66f, 0.7f), equatorColor = C(0.55f, 0.57f, 0.6f), groundColor = C(0.3f, 0.3f, 0.3f),
                intensity = 1f, reflectionIntensity = 0.6f,
            },
            fog = new FogSettings
            {
                enabled = true, mode = FogMode.ExponentialSquared,
                color = C(0.62f, 0.65f, 0.68f), density = 0.035f, start = 5f, end = 60f,
            },
            post = new PostSettings
            {
                enabled = true, tonemapping = TonemapMode.Neutral,
                postExposure = 0f, contrast = -5f, saturation = -25f, colorFilter = Color.white,
                temperature = -5f, tint = 0f,
                bloomIntensity = 0.3f, bloomThreshold = 1f, bloomScatter = 0.8f, bloomTint = Color.white,
                vignetteIntensity = 0.2f, vignetteSmoothness = 0.5f, vignetteColor = Color.black,
            },
        };
    }
}
