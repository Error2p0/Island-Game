using IslandGame.Sky;
using UnityEngine;

namespace IslandGame.Foliage
{
    /// <summary>
    /// Transform-based wind sway for scattered foliage, driven by
    /// WeatherController.WindStrength01: a Perlin tilt around the plant's
    /// base pivot (foliage roots sit at ground level, so tilting the root IS
    /// bending the plant). Attached at spawn by FoliageScatterSystem — no
    /// prefab edits, so pre-weather prefabs sway too.
    ///
    /// Transform-based (not a vertex shader) by explicit choice: the
    /// bushes/reeds are ordinary GameObjects where a root tilt is free,
    /// while the TREES are voxel geometry sharing the terrain's chunk
    /// material — a sway there would either bend the terrain with them or
    /// require forking the chunk shader and de-syncing render from
    /// collision. Rain, fog and audio carry the storm on the treeline.
    ///
    /// Calm costs one property read per instance; the base rotation is
    /// re-captured on every enable, so pooled reuse with a new scatter yaw
    /// always sways around the fresh orientation.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class FoliageWindSway : MonoBehaviour
    {
        [Tooltip("Degrees of tilt at full wind.")]
        [Range(0f, 20f)]
        [SerializeField] private float maxTiltDegrees = 7f;

        [Tooltip("Sway speed multiplier.")]
        [Range(0.1f, 4f)]
        [SerializeField] private float swaySpeed = 1.2f;

        private Quaternion baseRotation;
        private float phase;
        private bool atRest = true;

        private void OnEnable()
        {
            // The scatter system positions/rotates BEFORE activating, so this
            // captures the final spawn yaw — for hand-placed foliage it's
            // simply the authored rotation.
            baseRotation = transform.localRotation;
            Vector3 p = transform.position;
            phase = (p.x * 0.73f + p.z * 1.31f) % 32f; // desync neighbors
        }

        private void Update()
        {
            WeatherController weather = WeatherController.Instance;
            float wind = weather != null ? weather.WindStrength01 : 0f;

            if (wind <= 0.03f)
            {
                if (!atRest)
                {
                    transform.localRotation = baseRotation;
                    atRest = true;
                }

                return;
            }

            atRest = false;
            float t = Time.time * swaySpeed + phase;
            float tiltX = (Mathf.PerlinNoise(t * 1.3f, 0.31f) - 0.5f) * 2f;
            float tiltZ = (Mathf.PerlinNoise(0.67f, t * 1.1f) - 0.5f) * 2f;

            float amplitude = maxTiltDegrees * wind;
            transform.localRotation = baseRotation * Quaternion.Euler(tiltX * amplitude, 0f, tiltZ * amplitude);
        }
    }
}
