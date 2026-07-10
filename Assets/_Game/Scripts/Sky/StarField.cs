using UnityEngine;

namespace IslandGame.Sky
{
    /// <summary>
    /// The night sky's stars: a procedurally scattered mesh of small emissive
    /// quads on a dome (NOT a flat texture — each star is real geometry at a
    /// jittered direction with its own size/brightness/tint, so the field
    /// parallaxes correctly against terrain silhouettes and never tiles).
    /// Built once in Awake from a fixed seed (~900 stars ≈ 3.6k verts — one
    /// draw call, trivial). The dome follows the CAMERA's position (never its
    /// rotation) each LateUpdate and spans most of the far plane, so stars
    /// behave as if at infinity; the additive Stars shader depth-tests
    /// against terrain and draws after the skybox (see Stars.shader).
    ///
    /// DayNightVisuals drives SetFade — 0 by day (renderer disabled), rising
    /// to 1 as the sun sinks. The material asset is instantiated so the
    /// per-frame fade never dirties the asset.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(MeshFilter))]
    [RequireComponent(typeof(MeshRenderer))]
    public sealed class StarField : MonoBehaviour
    {
        private static readonly int FadeId = Shader.PropertyToID("_Fade");

        [Header("Field")]
        [Tooltip("Star count. ~900 reads as the reference games' dense-but-not-noisy night sky.")]
        [Range(100, 3000)]
        [SerializeField] private int starCount = 900;

        [SerializeField] private int seed = 90210;

        [Tooltip("Dome radius as a fraction of the camera's far clip plane.")]
        [Range(0.3f, 0.95f)]
        [SerializeField] private float farPlaneFraction = 0.85f;

        [Header("Star Look")]
        [Tooltip("Angular size range in radians (~0.001 rad ≈ a bright point at any distance).")]
        [SerializeField] private float minAngularSize = 0.0011f;

        [SerializeField] private float maxAngularSize = 0.0032f;

        [Tooltip("Assigned by the Day/Night builder (IslandGame/Stars shader).")]
        [SerializeField] private Material starMaterial;

        private MeshRenderer starRenderer;
        private Material runtimeMaterial;
        private Transform followCamera;
        private float currentFade = -1f;

        private void Awake()
        {
            starRenderer = GetComponent<MeshRenderer>();
            starRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            starRenderer.receiveShadows = false;
            starRenderer.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;

            if (starMaterial != null)
            {
                runtimeMaterial = new Material(starMaterial) { name = "Stars (runtime)" };
                starRenderer.sharedMaterial = runtimeMaterial;
            }
            else
            {
                Debug.LogError("StarField: no star material — run Island Game/World/Create Day Night Cycle.", this);
            }

            GetComponent<MeshFilter>().sharedMesh = BuildDome();
            SetFade(0f);
        }

        private void Start()
        {
            Camera camera = Camera.main;
            if (camera != null)
            {
                followCamera = camera.transform;
                transform.localScale = Vector3.one * (camera.farClipPlane * farPlaneFraction);
            }
        }

        private void LateUpdate()
        {
            if (followCamera != null)
                transform.position = followCamera.position; // position only — stars must not turn with the head
        }

        /// <summary>0 = invisible (renderer off), 1 = full night sky. Driven by DayNightVisuals.</summary>
        public void SetFade(float fade)
        {
            fade = Mathf.Clamp01(fade);
            if (Mathf.Approximately(fade, currentFade))
                return;

            currentFade = fade;
            bool visible = fade > 0.005f && runtimeMaterial != null;
            if (starRenderer.enabled != visible)
                starRenderer.enabled = visible;

            if (runtimeMaterial != null)
                runtimeMaterial.SetFloat(FadeId, fade);
        }

        private void OnDestroy()
        {
            if (runtimeMaterial != null)
                Destroy(runtimeMaterial);
        }

        // ------------------------------------------------------------------
        // Mesh
        // ------------------------------------------------------------------

        /// <summary>
        /// Unit dome (radius 1, scaled by transform): one quad per star,
        /// tangent to the dome at a seeded random direction. Brightness and a
        /// slight warm/cool tint ride in vertex colors so the shader stays a
        /// single multiply.
        /// </summary>
        private Mesh BuildDome()
        {
            var random = new System.Random(seed);

            var vertices = new Vector3[starCount * 4];
            var colors = new Color[starCount * 4];
            var triangles = new int[starCount * 6];

            int placed = 0;
            while (placed < starCount)
            {
                // Uniform direction, kept above a slightly-sub-horizon band so
                // stars meet the horizon glow instead of stopping short.
                var direction = new Vector3(
                    (float)(random.NextDouble() * 2.0 - 1.0),
                    (float)(random.NextDouble() * 2.0 - 1.0),
                    (float)(random.NextDouble() * 2.0 - 1.0));
                if (direction.sqrMagnitude < 0.001f || direction.sqrMagnitude > 1f)
                    continue;

                direction.Normalize();
                if (direction.y < -0.08f)
                    continue;

                Vector3 tangentU = Vector3.Cross(direction, Vector3.up);
                if (tangentU.sqrMagnitude < 0.001f)
                    tangentU = Vector3.Cross(direction, Vector3.right);
                tangentU.Normalize();
                Vector3 tangentV = Vector3.Cross(direction, tangentU);

                float size = Mathf.Lerp(minAngularSize, maxAngularSize, (float)random.NextDouble());

                // Brightness biased toward dim (real skies: few bright stars).
                float brightness = 0.25f + 0.75f * Mathf.Pow((float)random.NextDouble(), 2.2f);

                // Subtle temperature variation: a touch of blue or amber.
                float warmth = (float)(random.NextDouble() * 2.0 - 1.0) * 0.12f;
                var color = new Color(
                    Mathf.Clamp01(brightness * (1f + warmth)),
                    brightness,
                    Mathf.Clamp01(brightness * (1f - warmth)),
                    1f);

                int v = placed * 4;
                vertices[v + 0] = direction + (-tangentU - tangentV) * size;
                vertices[v + 1] = direction + (-tangentU + tangentV) * size;
                vertices[v + 2] = direction + (tangentU + tangentV) * size;
                vertices[v + 3] = direction + (tangentU - tangentV) * size;
                colors[v + 0] = color;
                colors[v + 1] = color;
                colors[v + 2] = color;
                colors[v + 3] = color;

                int t = placed * 6;
                triangles[t + 0] = v;
                triangles[t + 1] = v + 1;
                triangles[t + 2] = v + 2;
                triangles[t + 3] = v;
                triangles[t + 4] = v + 2;
                triangles[t + 5] = v + 3;

                placed++;
            }

            var mesh = new Mesh { name = "StarDome" };
            mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            mesh.vertices = vertices;
            mesh.colors = colors;
            mesh.triangles = triangles;

            // The dome surrounds the camera; normal culling would drop it the
            // moment its center goes off screen. A huge explicit bound keeps
            // it always rendered (it IS always visible at night).
            mesh.bounds = new Bounds(Vector3.zero, Vector3.one * 4f);
            return mesh;
        }
    }
}
