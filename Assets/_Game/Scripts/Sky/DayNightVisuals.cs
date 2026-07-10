using UnityEngine;
using UnityEngine.Rendering;

namespace IslandGame.Sky
{
    /// <summary>
    /// Turns TimeOfDayController's clock into everything you SEE: sun and
    /// moon directional lights, the gradient skybox's colors and celestial
    /// disk, trilight ambient, and linear distance fog. All parameters are
    /// smooth functions of the sun's elevation, so every transition (dusk
    /// glow, star fade, fog thickening) is continuous — no keyframed snaps.
    ///
    /// LIGHTS: exactly one shadow-casting directional is active at a time —
    /// the sun by day, the dim blue moon at night (moon shadows disabled:
    /// soft moonlight shadows cost a full shadow map for barely-visible
    /// results). URP's main-light selection stays deterministic this way.
    ///
    /// PLAYABILITY EXPOSURE CHOICE (documented per the phase requirement):
    /// night ambient bottoms out at a moonlit blue ~8-10% gray instead of
    /// black, and moon intensity 0.22 — The Forest-style "dark and moody but
    /// navigable". Placed light sources (campfires) are what actually carve
    /// visibility out of the night, which is the point of building them.
    ///
    /// The skybox material asset is INSTANTIATED at runtime and the copy
    /// assigned to RenderSettings.skybox, so per-frame property writes never
    /// dirty the asset on disk; the original is restored on destroy.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class DayNightVisuals : MonoBehaviour
    {
        private static readonly int TopColorId = Shader.PropertyToID("_TopColor");
        private static readonly int HorizonColorId = Shader.PropertyToID("_HorizonColor");
        private static readonly int BottomColorId = Shader.PropertyToID("_BottomColor");
        private static readonly int CelestialDirId = Shader.PropertyToID("_CelestialDir");
        private static readonly int CelestialColorId = Shader.PropertyToID("_CelestialColor");
        private static readonly int DiskSizeId = Shader.PropertyToID("_DiskSize");
        private static readonly int GlowStrengthId = Shader.PropertyToID("_GlowStrength");

        [Header("Scene References (wired by the Day/Night builder)")]
        [SerializeField] private TimeOfDayController timeOfDay;
        [SerializeField] private Light sunLight;
        [SerializeField] private Light moonLight;
        [SerializeField] private StarField starField;

        [Tooltip("The SkyGradient material ASSET — instantiated at runtime, never written to.")]
        [SerializeField] private Material skyboxMaterial;

        [Header("Sun")]
        [Tooltip("Peak intensity at noon.")]
        [SerializeField] private float sunMaxIntensity = 1.15f;

        [SerializeField] private Color sunDayColor = new Color(1f, 0.96f, 0.88f);
        [SerializeField] private Color sunHorizonColor = new Color(1f, 0.50f, 0.22f);

        [Tooltip("Yaw tilt of the sun's path so shadows sweep diagonally instead of due east-west.")]
        [Range(-90f, 90f)]
        [SerializeField] private float sunPathYaw = 30f;

        [Header("Moon")]
        [SerializeField] private float moonIntensity = 0.22f;
        [SerializeField] private Color moonColor = new Color(0.58f, 0.66f, 0.95f);

        [Header("Sky Colors")]
        [SerializeField] private Color dayZenith = new Color(0.28f, 0.48f, 0.75f);
        [SerializeField] private Color dayHorizon = new Color(0.68f, 0.80f, 0.92f);
        [SerializeField] private Color duskZenith = new Color(0.17f, 0.14f, 0.30f);
        [SerializeField] private Color duskHorizon = new Color(0.98f, 0.46f, 0.20f);
        [SerializeField] private Color nightZenith = new Color(0.012f, 0.018f, 0.045f);
        [SerializeField] private Color nightHorizon = new Color(0.045f, 0.065f, 0.13f);

        [Header("Ambient (trilight)")]
        [SerializeField] private Color dayAmbientSky = new Color(0.56f, 0.63f, 0.72f);
        [SerializeField] private Color dayAmbientEquator = new Color(0.42f, 0.45f, 0.50f);
        [SerializeField] private Color dayAmbientGround = new Color(0.26f, 0.25f, 0.24f);

        [Tooltip("Night floor — deliberately ~8-10% gray-blue, not black (see class summary).")]
        [SerializeField] private Color nightAmbientSky = new Color(0.075f, 0.095f, 0.16f);
        [SerializeField] private Color nightAmbientEquator = new Color(0.05f, 0.06f, 0.11f);
        [SerializeField] private Color nightAmbientGround = new Color(0.02f, 0.025f, 0.05f);

        [Header("Fog (linear)")]
        [SerializeField] private float dayFogStart = 45f;
        [SerializeField] private float dayFogEnd = 130f;

        [Tooltip("Night fog closes in — moodier AND hides the darker far terrain.")]
        [SerializeField] private float nightFogStart = 16f;
        [SerializeField] private float nightFogEnd = 80f;

        private Material runtimeSky;
        private Material previousSkybox;
        private AmbientMode previousAmbientMode;

        private void Start()
        {
            if (timeOfDay == null)
                timeOfDay = GetComponent<TimeOfDayController>();

            if (timeOfDay == null || sunLight == null || skyboxMaterial == null)
            {
                Debug.LogError("DayNightVisuals: missing references — run Island Game/World/Create Day Night Cycle.", this);
                enabled = false;
                return;
            }

            previousSkybox = RenderSettings.skybox;
            previousAmbientMode = RenderSettings.ambientMode;

            runtimeSky = new Material(skyboxMaterial) { name = "SkyGradient (runtime)" };
            RenderSettings.skybox = runtimeSky;
            RenderSettings.sun = sunLight;
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;

            ApplyVisuals(); // correct first frame even while paused
        }

        private void OnDestroy()
        {
            RenderSettings.skybox = previousSkybox;
            RenderSettings.ambientMode = previousAmbientMode;
            if (runtimeSky != null)
                Destroy(runtimeSky);
        }

        private void LateUpdate()
        {
            ApplyVisuals();
        }

        private void ApplyVisuals()
        {
            float time = timeOfDay.TimeOfDay01;

            // Sun path: elevation angle 0° at 0.25 (sunrise), 90° at noon,
            // 180° at 0.75 (sunset), 270° at midnight. Its sine is the signed
            // "how high is the sun" that every other parameter derives from.
            float sunAngle = (time - 0.25f) * 360f;
            float elevation = Mathf.Sin(sunAngle * Mathf.Deg2Rad); // -1..1

            // 0 at/below horizon → 1 once the sun is ~8° up. The small
            // negative lead-in keeps pre-sunrise from being pitch black.
            float daylight = Mathf.InverseLerp(-0.04f, 0.14f, elevation);
            daylight = daylight * daylight * (3f - 2f * daylight); // smoothstep

            // Peaks when the sun sits ON the horizon, gone above ~15°/below ~-15°.
            float horizonGlow = Mathf.Clamp01(1f - Mathf.Abs(elevation) * 4f);

            // Moon fades in as the sun sinks well below the horizon.
            float moonFactor = Mathf.InverseLerp(-0.02f, -0.16f, elevation);

            var sunRotation = Quaternion.Euler(sunAngle, sunPathYaw, 0f);

            // --- Lights (one shadow caster at a time) ---------------------
            sunLight.transform.rotation = sunRotation;
            sunLight.intensity = sunMaxIntensity * daylight;
            sunLight.color = Color.Lerp(sunHorizonColor, sunDayColor, Mathf.Clamp01(elevation * 2.5f));
            sunLight.enabled = sunLight.intensity > 0.01f;

            if (moonLight != null)
            {
                moonLight.transform.rotation = Quaternion.Euler(sunAngle + 180f, sunPathYaw, 0f);
                moonLight.intensity = moonIntensity * moonFactor;
                moonLight.color = moonColor;
                moonLight.enabled = moonLight.intensity > 0.01f;
            }

            // --- Skybox ----------------------------------------------------
            Color zenith = Color.Lerp(nightZenith, dayZenith, daylight);
            Color horizon = Color.Lerp(nightHorizon, dayHorizon, daylight);
            zenith = Color.Lerp(zenith, duskZenith, horizonGlow * 0.6f);
            horizon = Color.Lerp(horizon, duskHorizon, horizonGlow);

            runtimeSky.SetColor(TopColorId, zenith);
            runtimeSky.SetColor(HorizonColorId, horizon);
            runtimeSky.SetColor(BottomColorId, Color.Lerp(horizon * 0.4f, horizon * 0.75f, daylight));

            // The disk is the sun while any daylight remains, the moon after.
            // (Light forward points AWAY from the body; negate the VECTOR.)
            bool sunVisible = daylight > 0.02f || elevation > -0.05f;
            Vector3 toBody = sunVisible
                ? -(sunRotation * Vector3.forward)
                : -(Quaternion.Euler(sunAngle + 180f, sunPathYaw, 0f) * Vector3.forward);
            Color diskColor = sunVisible
                ? Color.Lerp(sunHorizonColor, Color.white, Mathf.Clamp01(elevation * 2f)) * (0.6f + daylight)
                : moonColor * 0.85f * moonFactor;

            runtimeSky.SetVector(CelestialDirId, toBody);
            runtimeSky.SetColor(CelestialColorId, diskColor);
            runtimeSky.SetFloat(DiskSizeId, sunVisible ? 0.004f : 0.0022f);
            runtimeSky.SetFloat(GlowStrengthId, sunVisible ? 0.5f + horizonGlow * 1.6f : 0.15f);

            // --- Stars -------------------------------------------------------
            if (starField != null)
                starField.SetFade(Mathf.InverseLerp(-0.02f, -0.14f, elevation));

            // --- Ambient + fog ----------------------------------------------
            RenderSettings.ambientSkyColor = Color.Lerp(nightAmbientSky, dayAmbientSky, daylight);
            RenderSettings.ambientEquatorColor = Color.Lerp(
                Color.Lerp(nightAmbientEquator, dayAmbientEquator, daylight),
                duskHorizon * 0.35f, horizonGlow * 0.5f);
            RenderSettings.ambientGroundColor = Color.Lerp(nightAmbientGround, dayAmbientGround, daylight);

            RenderSettings.fogColor = horizon;
            RenderSettings.fogStartDistance = Mathf.Lerp(nightFogStart, dayFogStart, daylight);
            RenderSettings.fogEndDistance = Mathf.Lerp(nightFogEnd, dayFogEnd, daylight);
        }
    }
}
