using IslandGame.Player;
using UnityEngine;

namespace IslandGame.Sky
{
    /// <summary>
    /// Everything you SEE and HEAR of the weather (the DayNightVisuals of the
    /// weather system — pure presentation over WeatherController's signals):
    ///
    ///   RAIN — one runtime-built particle system, a camera-relative volume
    ///   (a ~26 m box riding above the player, NOT world-wide emission).
    ///   World-space simulation so falling drops don't smear with camera
    ///   turns; stretched billboards read as streaks; world collision (low
    ///   quality, kill-on-contact) makes roofs visibly keep the rain out —
    ///   which is the shelter mechanic made legible. Emission follows
    ///   Precipitation01, the volume tilts with the wind.
    ///
    ///   SKY — per-frame push of darken/fog/flash into the existing
    ///   DayNightVisuals hook (SetWeatherAtmosphere). One sky system.
    ///
    ///   LIGHTNING (storm only) — random 4-14 s cadence: a dedicated
    ///   shadowless directional light spikes and decays over ~0.4 s while
    ///   the same envelope feeds the sky flash; thunder follows 0.4-2.5 s
    ///   later (distance fake) at randomized pitch. Audio is synthesized at
    ///   startup (ProceduralWeatherAudio — no binary assets).
    ///
    ///   STORM CAMERA SWAY — a few centimeters of Perlin drift pushed through
    ///   PlayerCameraEffects.SetExternalSway. Visual-only by design:
    ///   PlayerLocomotion's tuned movement math is deliberately untouched
    ///   (the movement phase's feel work must not be fought from here).
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class WeatherVisuals : MonoBehaviour
    {
        [Header("References (wired by the Weather builder; auto-resolved when empty)")]
        [SerializeField] private WeatherController weather;
        [SerializeField] private DayNightVisuals dayNightVisuals;

        [Header("Rain Volume")]
        [Tooltip("Horizontal size of the camera-relative emission box, meters.")]
        [Range(10f, 60f)]
        [SerializeField] private float rainAreaSize = 26f;

        [Tooltip("Height above the camera the drops spawn at.")]
        [Range(6f, 30f)]
        [SerializeField] private float rainHeight = 13f;

        [Tooltip("Particles per second at full storm precipitation.")]
        [Range(100f, 4000f)]
        [SerializeField] private float maxEmissionRate = 1700f;

        [Header("Sky")]
        [Tooltip("How much full precipitation darkens the sky (storm adds more on top).")]
        [Range(0f, 1f)]
        [SerializeField] private float rainDarkening = 0.45f;

        [Range(0f, 1f)]
        [SerializeField] private float stormExtraDarkening = 0.25f;

        [Tooltip("How much full precipitation closes the fog in.")]
        [Range(0f, 1f)]
        [SerializeField] private float rainFogBoost = 0.55f;

        [Header("Lightning (storm)")]
        [Tooltip("Seconds between flashes at full storm intensity (randomized around this).")]
        [SerializeField] private Vector2 flashIntervalRange = new Vector2(4f, 14f);

        [Tooltip("Peak intensity of the flash light.")]
        [Range(0f, 8f)]
        [SerializeField] private float flashPeakIntensity = 2.2f;

        [Tooltip("How fast the flash decays (per second — higher = snappier).")]
        [Range(1f, 20f)]
        [SerializeField] private float flashDecay = 7f;

        [Header("Audio")]
        [Range(0f, 1f)]
        [SerializeField] private float rainVolume = 0.45f;

        [Range(0f, 1f)]
        [SerializeField] private float thunderVolume = 0.8f;

        [Header("Storm Camera Sway (visual-only)")]
        [Tooltip("Max camera drift at full storm, meters. Keep it a whisper.")]
        [Range(0f, 0.08f)]
        [SerializeField] private float swayAmplitude = 0.022f;

        private ParticleSystem rainParticles;
        private ParticleSystem.EmissionModule rainEmission;
        private Transform rainTransform;
        private Light flashLight;
        private AudioSource rainSource;
        private AudioSource thunderSource;
        private AudioClip thunderClip;

        private PlayerReferences player;
        private PlayerCameraEffects cameraEffects;
        private Transform cameraTransform;

        private float flashIntensity01;
        private float nextFlashTime;
        private float pendingThunderTime = float.PositiveInfinity;
        private System.Random audioRandom;

        private void Start()
        {
            if (weather == null)
                weather = GetComponent<WeatherController>();
            if (dayNightVisuals == null)
                dayNightVisuals = GetComponent<DayNightVisuals>();

            if (weather == null)
            {
                Debug.LogError("WeatherVisuals: no WeatherController — run Island Game/World/Add Weather System.", this);
                enabled = false;
                return;
            }

            audioRandom = new System.Random(System.Environment.TickCount);

            BuildRainVolume();
            BuildFlashLight();
            BuildAudioSources();
        }

        private void LateUpdate()
        {
            float precipitation = weather.Precipitation01;
            float storm = weather.StormIntensity01;
            float wind = weather.WindStrength01;

            ResolvePlayer();
            UpdateRain(precipitation, wind);
            UpdateLightning(storm);
            UpdateAudio(precipitation);
            UpdateCameraSway(storm, wind);

            // The one write into the day/night sky (0s = untouched visuals).
            dayNightVisuals?.SetWeatherAtmosphere(
                precipitation * rainDarkening + storm * stormExtraDarkening,
                precipitation * rainFogBoost + storm * 0.2f,
                flashIntensity01);
        }

        // ------------------------------------------------------------------
        // Rain
        // ------------------------------------------------------------------

        private void BuildRainVolume()
        {
            var rainObject = new GameObject("RainVolume");
            rainObject.transform.SetParent(transform, false);
            rainTransform = rainObject.transform;

            rainParticles = rainObject.AddComponent<ParticleSystem>();
            rainParticles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            ParticleSystem.MainModule main = rainParticles.main;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.startLifetime = 1.5f;
            main.startSpeed = 19f;
            main.startSize = new ParticleSystem.MinMaxCurve(0.015f, 0.032f);
            main.startColor = new Color(0.62f, 0.70f, 0.82f, 0.38f);
            main.gravityModifier = 0.35f;
            main.maxParticles = 5000;
            main.loop = true;
            main.playOnAwake = false;

            // Box emitter firing along its local +Z; the volume is pitched 90°
            // in UpdateRain so +Z is world-down (plus wind tilt).
            ParticleSystem.ShapeModule shape = rainParticles.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = new Vector3(rainAreaSize, rainAreaSize, 0.5f);

            ParticleSystem.EmissionModule emission = rainParticles.emission;
            emission.rateOverTime = 0f;
            rainEmission = emission;

            // Kill drops on world contact: rain visibly stops at roofs and
            // terrain — the shelter check made visible. Low quality uses
            // Unity's cached collision voxels, cheap enough for thousands.
            ParticleSystem.CollisionModule collision = rainParticles.collision;
            collision.enabled = true;
            collision.type = ParticleSystemCollisionType.World;
            collision.mode = ParticleSystemCollisionMode.Collision3D;
            collision.quality = ParticleSystemCollisionQuality.Low;
            collision.lifetimeLoss = 1f;
            collision.bounce = 0f;

            var renderer = rainObject.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Stretch;
            renderer.velocityScale = 0f;
            renderer.lengthScale = 6f;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.material = new Material(Shader.Find("Sprites/Default"))
            {
                name = "Rain (runtime)",
            };

            rainParticles.Play();
        }

        private void UpdateRain(float precipitation, float wind)
        {
            rainEmission.rateOverTime = precipitation * maxEmissionRate;

            if (cameraTransform == null)
                return;

            // Ride above the camera, led slightly along the view so sprinting
            // never outruns the volume; pitch past 90° tilts the fall with wind.
            Vector3 forwardFlat = cameraTransform.forward;
            forwardFlat.y = 0f;
            forwardFlat = forwardFlat.sqrMagnitude > 0.001f ? forwardFlat.normalized : Vector3.forward;

            rainTransform.position = cameraTransform.position + Vector3.up * rainHeight + forwardFlat * 5f;
            rainTransform.rotation = Quaternion.Euler(90f + wind * 12f, 0f, 0f);
        }

        // ------------------------------------------------------------------
        // Lightning & thunder
        // ------------------------------------------------------------------

        private void BuildFlashLight()
        {
            var flashObject = new GameObject("LightningFlash");
            flashObject.transform.SetParent(transform, false);
            flashLight = flashObject.AddComponent<Light>();
            flashLight.type = LightType.Directional;
            flashLight.shadows = LightShadows.None;
            flashLight.color = new Color(0.82f, 0.87f, 1f);
            flashLight.intensity = 0f;
            flashLight.enabled = false;
        }

        private void UpdateLightning(float storm)
        {
            // Decay whatever flash is in flight regardless of state.
            if (flashIntensity01 > 0f)
            {
                flashIntensity01 = Mathf.Max(0f, flashIntensity01 - flashDecay * Time.deltaTime * flashIntensity01 - 0.4f * Time.deltaTime);
                flashLight.intensity = flashPeakIntensity * flashIntensity01;
                flashLight.enabled = flashIntensity01 > 0.01f;
            }

            // Delayed thunder from the last strike.
            if (Time.time >= pendingThunderTime)
            {
                pendingThunderTime = float.PositiveInfinity;
                if (thunderSource != null && thunderClip != null)
                {
                    thunderSource.pitch = 0.8f + (float)audioRandom.NextDouble() * 0.4f;
                    thunderSource.PlayOneShot(thunderClip, thunderVolume);
                }
            }

            if (storm < 0.5f)
            {
                nextFlashTime = 0f; // re-arm when the next storm ramps in
                return;
            }

            if (nextFlashTime <= 0f)
            {
                nextFlashTime = Time.time + Random.Range(flashIntervalRange.x, flashIntervalRange.y);
                return;
            }

            if (Time.time >= nextFlashTime)
            {
                flashIntensity01 = 1f;
                flashLight.transform.rotation = Quaternion.Euler(55f, Random.Range(0f, 360f), 0f);

                // Thunder trails the light — a random 0.4-2.5 s of faked distance.
                pendingThunderTime = Time.time + Random.Range(0.4f, 2.5f);
                nextFlashTime = Time.time + Random.Range(flashIntervalRange.x, flashIntervalRange.y);
            }
        }

        // ------------------------------------------------------------------
        // Audio
        // ------------------------------------------------------------------

        private void BuildAudioSources()
        {
            int seed = 4242;

            rainSource = gameObject.AddComponent<AudioSource>();
            rainSource.clip = ProceduralWeatherAudio.CreateRainLoop(seed);
            rainSource.loop = true;
            rainSource.spatialBlend = 0f; // ambience, not a point source
            rainSource.volume = 0f;
            rainSource.Play();

            thunderSource = gameObject.AddComponent<AudioSource>();
            thunderSource.spatialBlend = 0f;
            thunderClip = ProceduralWeatherAudio.CreateThunder(seed + 1);
        }

        private void UpdateAudio(float precipitation)
        {
            if (rainSource != null)
                rainSource.volume = precipitation * rainVolume;
        }

        // ------------------------------------------------------------------
        // Camera sway (through PlayerCameraEffects' designed hook)
        // ------------------------------------------------------------------

        private void ResolvePlayer()
        {
            if (player != null)
                return;

            player = FindFirstObjectByType<PlayerReferences>();
            if (player == null)
                return;

            cameraEffects = player.GetComponent<PlayerCameraEffects>();
            cameraTransform = player.PlayerCamera != null ? player.PlayerCamera.transform : null;
        }

        private void UpdateCameraSway(float storm, float wind)
        {
            if (cameraEffects == null)
                return;

            float amplitude = swayAmplitude * storm * wind;
            if (amplitude <= 0.0001f)
            {
                cameraEffects.SetExternalSway(Vector3.zero);
                return;
            }

            float t = Time.time;
            cameraEffects.SetExternalSway(new Vector3(
                (Mathf.PerlinNoise(t * 1.7f, 0.3f) - 0.5f) * 2f * amplitude,
                (Mathf.PerlinNoise(0.6f, t * 1.3f) - 0.5f) * 2f * amplitude * 0.6f,
                0f));
        }
    }
}
