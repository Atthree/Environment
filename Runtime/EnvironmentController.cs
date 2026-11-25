using System;
using UnityEngine;
using UnityEngine.Rendering;
#if ATTHREE_URP
using UnityEngine.Rendering.Universal;
#endif
using Object = UnityEngine.Object;

namespace Atthree.Environments
{
    /// <summary>
    /// Sahnenin ortamını yönetir: bir <see cref="EnvironmentPreset"/>'i güneşe, gökyüzüne, ortam ışığına, sise ve
    /// (URP varsa) post-processing Volume'una uygular. Oyun içinde <see cref="TransitionTo"/> ile yumuşak geçiş yapar.
    /// Sahnede bir tane olması yeterli; kurmanın en kolay yolu Window > Atthree > Environment.
    /// </summary>
    [ExecuteAlways]
    [DisallowMultipleComponent]
    [AddComponentMenu("Atthree/Environment/Environment Controller")]
    public class EnvironmentController : MonoBehaviour
    {
        [SerializeField] EnvironmentPreset preset;

        [Header("Scene references")]
        [Tooltip("Güneş (Directional Light). Boşsa güneş ayarları atlanır.")]
        public Light sun;
        [Tooltip("Arka planı (skybox/düz renk) ayarlanacak kamera. Boşsa Camera.main.")]
        public Camera targetCamera;
        [Tooltip("İsteğe bağlı: ortam değişince Play modunda yeniden çekilecek kendi Realtime reflection probe'un.")]
        public ReflectionProbe reflectionProbe;
#if ATTHREE_URP
        [Tooltip("Post-processing için Global Volume. Boşsa post ayarları atlanır.")]
        public Volume volume;
#endif
        [Tooltip("Procedural gökyüzü materyali (Skybox/Procedural). Boşsa çalışırken bir tane oluşturulur.")]
        public Material skyboxMaterial;

        [Header("Options")]
        [Tooltip("Açılınca (sahne yüklenince) preset'i uygula.")]
        public bool applyOnEnable = true;
        [Tooltip("Kameranın arka planını gökyüzü moduna göre ayarla (Skybox / düz renk).")]
        public bool controlCamera = true;
        [Tooltip("Gökyüzünü bir cubemap'e çizip sahnenin varsayılan yansıması yapar: metal/parlak yüzeyler gökyüzünü (ya da sisi) yansıtır. Editörde de çalışır.")]
        public bool skyReflections = true;
        [Tooltip("Yansıma cubemap'inin bir yüzünün boyutu.")]
        [Range(16, 512)] public int reflectionResolution = 128;
        [Tooltip("Geçişler Time.timeScale'den etkilenmesin (oyun duraklatılmışken de geçiş yapar).")]
        public bool unscaledTime;
        [Tooltip("Sürekli değişimde (geçiş, gün döngüsü) ortam ışığı ve yansıma en fazla bu sıklıkla yenilenir (saniye).")]
        [Min(0f)] public float lightingRefreshInterval = 0.5f;

        /// <summary>Sahnedeki etkin controller (en son açılan).</summary>
        public static EnvironmentController Current { get; private set; }

        /// <summary>Bir geçiş bittiğinde ya da preset anında değiştiğinde.</summary>
        public event Action<EnvironmentPreset> PresetChanged;

        /// <summary>Şu anki (ya da geçişin varacağı) preset.</summary>
        public EnvironmentPreset Preset => transitioning ? transitionTarget : preset;

        /// <summary>Sahneye en son uygulanan değerler (geçiş sırasında karışım).</summary>
        public EnvironmentSettings CurrentSettings => current;

        public bool IsTransitioning => transitioning;

        /// <summary>0..1 geçiş ilerlemesi (geçiş yoksa 1).</summary>
        public float TransitionProgress => transitioning ? Mathf.Clamp01(transitionElapsed / transitionDuration) : 1f;

        /// <summary>
        /// Ortamı başka bir sistem sürüyorsa (ör. <see cref="TimeOfDay"/>) o sistemi gösterir.
        /// Doluyken preset düzenlemeleri ve applyOnEnable sahneye uygulanmaz.
        /// </summary>
        public Object Driver { get; set; }

        EnvironmentSettings current;
        bool hasCurrent;

        bool transitioning;
        EnvironmentSettings transitionFrom;
        EnvironmentPreset transitionTarget;
        float transitionDuration;
        float transitionElapsed;

        Material runtimeSky;          // skyboxMaterial boşsa oluşturulan ya da Play'de asset'in kopyası
        Material runtimeSkySource;    // runtimeSky hangi asset'ten kopyalandı
        RenderTexture reflectionCube; // gökyüzü yansıması
        Camera captureCamera;         // gizli, sadece gökyüzünü çizer
        float lastRefreshTime = float.NegativeInfinity;
        bool refreshPending;

        // ---------------------------------------------------------------- lifecycle

        void OnEnable()
        {
            Current = this;
            EnvironmentPreset.Edited += OnPresetEdited;
            if (applyOnEnable && preset != null && Driver == null) Apply(preset.settings, true);
        }

        void OnDisable()
        {
            EnvironmentPreset.Edited -= OnPresetEdited;
            if (Current == this) Current = null;
            transitioning = false;
            ReleaseReflection();
        }

        void OnDestroy()
        {
            if (runtimeSky != null)
            {
                if (RenderSettings.skybox == runtimeSky) RenderSettings.skybox = null;
                DestroySafe(runtimeSky);
            }
            ReleaseReflection();
        }

        void Update()
        {
            if (transitioning)
            {
                transitionElapsed += unscaledTime ? Time.unscaledDeltaTime : Time.deltaTime;
                float t = Mathf.Clamp01(transitionElapsed / transitionDuration);
                if (t >= 1f)
                {
                    FinishTransition();
                }
                else
                {
                    float eased = t * t * (3f - 2f * t);
                    Apply(EnvironmentSettings.Lerp(transitionFrom, transitionTarget.settings, eased), false);
                }
            }

            // Sürekli değişim durunca son bir kez ışığı tam yenile.
            if (refreshPending && Now - lastRefreshTime >= lightingRefreshInterval) RefreshLighting(true);
        }

#if UNITY_EDITOR
        void OnValidate()
        {
            // Inspector'da preset alanı değişince editörde hemen göster.
            if (!isActiveAndEnabled || Application.isPlaying) return;
            UnityEditor.EditorApplication.delayCall += () =>
            {
                if (this != null && isActiveAndEnabled && preset != null && Driver == null) Apply(preset.settings, true);
            };
        }
#endif

        void OnPresetEdited(EnvironmentPreset edited)
        {
            if (Driver != null) return;
            if (edited == null || edited != preset || transitioning) return;
#if UNITY_EDITOR
            if (!Application.isPlaying)
            {
                // OnValidate içinden ışık/kamera değiştirmek uyarı verir; bir sonraki editör karesine ertele.
                UnityEditor.EditorApplication.delayCall += () =>
                {
                    if (this != null && isActiveAndEnabled && preset == edited && Driver == null) Apply(edited.settings, true);
                };
                return;
            }
#endif
            Apply(preset.settings, true);
        }

        // ---------------------------------------------------------------- public API

        /// <summary>Preset'i hemen uygular (geçiş yok).</summary>
        public void SetPreset(EnvironmentPreset newPreset)
        {
            transitioning = false;
            preset = newPreset;
            if (newPreset != null) Apply(newPreset.settings, true);
            PresetChanged?.Invoke(newPreset);
        }

        /// <summary>
        /// Şu anki görünümden yeni preset'e <paramref name="duration"/> saniyede yumuşak geçiş.
        /// Editörde (Play değilken) ya da süre 0 ise anında uygular. Geçiş sırasında çağırırsan o anki karışımdan devam eder.
        /// </summary>
        public void TransitionTo(EnvironmentPreset target, float duration)
        {
            if (target == null) return;
            if (duration <= 0f || !Application.isPlaying)
            {
                SetPreset(target);
                return;
            }

            transitionFrom = hasCurrent ? current : (preset != null ? preset.settings : target.settings);
            transitionTarget = target;
            transitionDuration = duration;
            transitionElapsed = 0f;
            transitioning = true;
        }

        /// <summary>Geçişi bitiş noktasına atlatır.</summary>
        public void CompleteTransition()
        {
            if (transitioning) FinishTransition();
        }

        /// <summary>Preset'i yeniden uygular (ör. sahneye elle dokunduysan).</summary>
        public void Reapply()
        {
            if (transitioning) return;
            if (preset != null) Apply(preset.settings, true);
        }

        /// <summary>
        /// Ham ayarları sahneye uygular. Kendi sistemini (hava durumu, gün döngüsü) yazarken kullan.
        /// <paramref name="refreshLightingNow"/> false ise ortam ışığı/yansıma yenilemesi <see cref="lightingRefreshInterval"/> ile seyreltilir.
        /// </summary>
        public void Apply(in EnvironmentSettings settings, bool refreshLightingNow = true)
        {
            current = settings;
            hasCurrent = true;

            ApplySun(settings.sun);
            ApplySky(settings.sky);
            ApplyAmbient(settings.ambient);
            ApplyFog(settings.fog);
            ApplyCamera(settings);
            ApplyPost(settings.post);

            RefreshLighting(refreshLightingNow);
        }

        // ---------------------------------------------------------------- apply parts

        void FinishTransition()
        {
            transitioning = false;
            preset = transitionTarget;
            Apply(preset.settings, true);
            PresetChanged?.Invoke(preset);
        }

        void ApplySun(in SunSettings s)
        {
            if (sun == null) return;
            sun.type = LightType.Directional;
            sun.enabled = s.enabled && s.intensity > 0f;
            sun.transform.rotation = s.Rotation;
            sun.color = s.color;
            sun.intensity = s.intensity;
            sun.shadows = s.shadows;
            sun.shadowStrength = s.shadowStrength;
            RenderSettings.sun = sun;
        }

        void ApplySky(in SkySettings s)
        {
            switch (s.mode)
            {
                case SkyMode.Procedural:
                    var mat = GetProceduralSky();
                    if (mat != null)
                    {
                        mat.SetColor(SkyTint, s.skyTint);
                        mat.SetColor(GroundColor, s.groundColor);
                        mat.SetFloat(Exposure, s.exposure);
                        mat.SetFloat(SunSize, s.sunSize);
                        mat.SetFloat(Atmosphere, s.atmosphereThickness);
                        MarkDirty(mat);
                    }
                    RenderSettings.skybox = mat;
                    break;
                case SkyMode.SolidColor:
                    RenderSettings.skybox = null;
                    break;
                case SkyMode.CustomMaterial:
                    RenderSettings.skybox = s.customMaterial;
                    break;
            }
        }

        static void ApplyAmbient(in AmbientSettings s)
        {
            RenderSettings.ambientMode = s.mode;
            RenderSettings.ambientSkyColor = s.skyColor;
            RenderSettings.ambientEquatorColor = s.equatorColor;
            RenderSettings.ambientGroundColor = s.groundColor;
            RenderSettings.ambientIntensity = s.intensity;
            RenderSettings.reflectionIntensity = s.reflectionIntensity;
        }

        static void ApplyFog(in FogSettings s)
        {
            RenderSettings.fog = s.enabled;
            RenderSettings.fogMode = s.mode;
            RenderSettings.fogColor = s.color;
            RenderSettings.fogDensity = s.density;
            RenderSettings.fogStartDistance = s.start;
            RenderSettings.fogEndDistance = s.end;
        }

        void ApplyCamera(in EnvironmentSettings s)
        {
            if (!controlCamera) return;
            var cam = targetCamera != null ? targetCamera : Camera.main;
            if (cam == null) return;

            bool solid = s.sky.mode == SkyMode.SolidColor || RenderSettings.skybox == null;
            cam.clearFlags = solid ? CameraClearFlags.SolidColor : CameraClearFlags.Skybox;
            if (solid) cam.backgroundColor = s.sky.solidColor;

#if ATTHREE_URP
            if (s.post.enabled && volume != null)
            {
                var data = cam.GetUniversalAdditionalCameraData();
                if (data != null && !data.renderPostProcessing)
                {
                    data.renderPostProcessing = true;
                    MarkDirty(data);
                }
            }
#endif
        }

        void ApplyPost(in PostSettings s)
        {
#if ATTHREE_URP
            if (volume == null) return;

            VolumeProfile profile;
            if (Application.isPlaying)
            {
                profile = volume.profile; // ilk erişimde asset'in kopyası oluşur, asset değişmez
            }
            else
            {
                if (volume.sharedProfile == null)
                {
                    volume.sharedProfile = ScriptableObject.CreateInstance<VolumeProfile>();
                    volume.sharedProfile.name = "Environment Profile (temporary)";
                    volume.sharedProfile.hideFlags = HideFlags.DontSave;
                }
                profile = volume.sharedProfile;
            }

            volume.weight = s.enabled ? 1f : 0f;
            if (!s.enabled) return;

            var tone = GetOrAdd<Tonemapping>(profile);
            tone.mode.value = s.tonemapping == TonemapMode.ACES ? TonemappingMode.ACES
                : s.tonemapping == TonemapMode.Neutral ? TonemappingMode.Neutral : TonemappingMode.None;

            var color = GetOrAdd<ColorAdjustments>(profile);
            color.postExposure.value = s.postExposure;
            color.contrast.value = s.contrast;
            color.saturation.value = s.saturation;
            color.colorFilter.value = s.colorFilter;

            var wb = GetOrAdd<WhiteBalance>(profile);
            wb.temperature.value = s.temperature;
            wb.tint.value = s.tint;

            var bloom = GetOrAdd<Bloom>(profile);
            bloom.intensity.value = s.bloomIntensity;
            bloom.threshold.value = s.bloomThreshold;
            bloom.scatter.value = s.bloomScatter;
            bloom.tint.value = s.bloomTint;

            var vignette = GetOrAdd<Vignette>(profile);
            vignette.intensity.value = s.vignetteIntensity;
            vignette.smoothness.value = s.vignetteSmoothness;
            vignette.color.value = s.vignetteColor;

            MarkDirty(profile);
#endif
        }

#if ATTHREE_URP
        static T GetOrAdd<T>(VolumeProfile profile) where T : VolumeComponent
        {
            if (!profile.TryGet(out T component))
            {
                component = profile.Add<T>(true);
#if UNITY_EDITOR
                // Profil bir asset ise bileşen de alt-asset olarak kaydedilmeli, yoksa sahne yeniden açılınca kaybolur.
                if (!Application.isPlaying && UnityEditor.AssetDatabase.Contains(profile))
                {
                    component.name = typeof(T).Name;
                    component.hideFlags = HideFlags.HideInInspector | HideFlags.HideInHierarchy;
                    UnityEditor.AssetDatabase.AddObjectToAsset(component, profile);
                }
#endif
            }
            component.active = true;
            component.SetAllOverridesTo(true);
            return component;
        }
#endif

        Material GetProceduralSky()
        {
            if (skyboxMaterial != null && !Application.isPlaying) return skyboxMaterial;

            if (skyboxMaterial != null)
            {
                // Play modunda asset'i değiştirmemek için kopyasıyla çalış.
                if (runtimeSky == null || runtimeSkySource != skyboxMaterial)
                {
                    if (runtimeSky != null) DestroySafe(runtimeSky);
                    runtimeSky = new Material(skyboxMaterial) { name = skyboxMaterial.name + " (Runtime)", hideFlags = HideFlags.DontSave };
                    runtimeSkySource = skyboxMaterial;
                }
                return runtimeSky;
            }

            if (runtimeSky == null || runtimeSkySource != null)
            {
                var shader = Shader.Find("Skybox/Procedural");
                if (shader == null)
                {
                    Debug.LogWarning("[Environment] 'Skybox/Procedural' shader bulunamadı. Controller'daki Skybox Material alanına bir materyal ata " +
                                     "(Window > Atthree > Environment otomatik oluşturur).", this);
                    return null;
                }
                if (runtimeSky != null) DestroySafe(runtimeSky);
                runtimeSky = new Material(shader) { name = "Environment Sky (Runtime)", hideFlags = HideFlags.DontSave };
                runtimeSkySource = null;
            }
            return runtimeSky;
        }

        /// <summary>
        /// Ortam ışığını (ambient probe) ve reflection probe'u yeniler. Pahalı olduğu için sürekli değişimde seyreltilir.
        /// </summary>
        public void RefreshLighting(bool force)
        {
            if (!force && Now - lastRefreshTime < lightingRefreshInterval)
            {
                refreshPending = true;
                return;
            }

            lastRefreshTime = Now;
            refreshPending = false;
            DynamicGI.UpdateEnvironment();
            UpdateSkyReflection();
            if (Application.isPlaying && reflectionProbe != null && reflectionProbe.isActiveAndEnabled && reflectionProbe.mode == ReflectionProbeMode.Realtime)
                reflectionProbe.RenderProbe();
        }

        /// <summary>Gökyüzünü (ya da düz arka plan rengini) cubemap'e çizip sahnenin varsayılan yansıması yapar.</summary>
        void UpdateSkyReflection()
        {
            if (!skyReflections || !hasCurrent) return;

            int res = Mathf.ClosestPowerOfTwo(Mathf.Clamp(reflectionResolution, 16, 512));
            if (reflectionCube == null || reflectionCube.width != res)
            {
                ReleaseReflection();
                reflectionCube = new RenderTexture(res, res, 16, RenderTextureFormat.DefaultHDR)
                {
                    name = "Environment Sky Reflection",
                    dimension = TextureDimension.Cube,
                    useMipMap = true,
                    autoGenerateMips = true,
                    hideFlags = HideFlags.DontSave,
                };
                reflectionCube.Create();
            }

            if (captureCamera == null)
            {
                var go = new GameObject("[Environment Reflection Capture]") { hideFlags = HideFlags.HideAndDontSave };
                captureCamera = go.AddComponent<Camera>();
                captureCamera.enabled = false;
                captureCamera.cullingMask = 0; // sadece gökyüzü
                captureCamera.nearClipPlane = 0.1f;
                captureCamera.farClipPlane = 100f;
            }

            bool solid = RenderSettings.skybox == null;
            captureCamera.clearFlags = solid ? CameraClearFlags.SolidColor : CameraClearFlags.Skybox;
            captureCamera.backgroundColor = current.sky.solidColor;

            if (captureCamera.RenderToCubemap(reflectionCube, 63))
            {
                RenderSettings.defaultReflectionMode = DefaultReflectionMode.Custom;
                RenderSettings.customReflectionTexture = reflectionCube;
            }
        }

        void ReleaseReflection()
        {
            if (reflectionCube != null)
            {
                if (RenderSettings.customReflectionTexture == reflectionCube)
                {
                    RenderSettings.customReflectionTexture = null;
                    RenderSettings.defaultReflectionMode = DefaultReflectionMode.Skybox;
                }
                reflectionCube.Release();
                DestroySafe(reflectionCube);
                reflectionCube = null;
            }
            if (captureCamera != null)
            {
                DestroySafe(captureCamera.gameObject);
                captureCamera = null;
            }
        }

        float Now => Application.isPlaying ? Time.realtimeSinceStartup : (float)NowEditor();

        static double NowEditor()
        {
#if UNITY_EDITOR
            return UnityEditor.EditorApplication.timeSinceStartup;
#else
            return Time.realtimeSinceStartup;
#endif
        }

        static void MarkDirty(Object o)
        {
#if UNITY_EDITOR
            if (!Application.isPlaying && o != null) UnityEditor.EditorUtility.SetDirty(o);
#endif
        }

        static void DestroySafe(Object o)
        {
            if (o == null) return;
            if (Application.isPlaying) Destroy(o);
            else DestroyImmediate(o);
        }

        static readonly int SkyTint = Shader.PropertyToID("_SkyTint");
        static readonly int GroundColor = Shader.PropertyToID("_GroundColor");
        static readonly int Exposure = Shader.PropertyToID("_Exposure");
        static readonly int SunSize = Shader.PropertyToID("_SunSize");
        static readonly int Atmosphere = Shader.PropertyToID("_AtmosphereThickness");
    }
}
