using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace IslandGame.Terrain
{
    /// <summary>
    /// BREAK-PROGRESS feedback for CLASSIC whole-block mining, driven by
    /// PlayerBlockInteraction's mining state: a crack overlay on the mined
    /// block stepping through procedurally generated stages (cumulative
    /// cracks) as MiningProgress01 advances — the hook reserved in Phase 6.
    /// RADIUS profiles never show the cube: their bite is a sub-voxel sphere,
    /// so a full-block crack cube would paint faces the bite never touches —
    /// their progress renders on the bite shape itself (MiningRadiusIndicator
    /// deepens the overlay tint with the same MiningProgress01).
    ///
    /// The SELECTION wireframe this component used to draw was removed in the
    /// organic-terrain phase: MiningRadiusIndicator now shows the true
    /// sphere-of-effect (including the blocked/red permission state) instead
    /// of a single-block box, so only one aim highlight ever renders.
    ///
    /// The overlay is a plain scene object created at Start (axis-aligned,
    /// unparented so player rotation can't tilt it) and torn down with the
    /// component. No materials or textures to author.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(PlayerBlockInteraction))]
    public sealed class BlockTargetIndicator : MonoBehaviour
    {
        private const float CrackInflation = 1.004f; // hair above the block surface — no z-fighting

        [Header("Break Cracks")]
        [Range(2, 8)]
        [SerializeField] private int crackStages = 5;

        [SerializeField] private Color crackColor = new Color(0.03f, 0.03f, 0.03f, 0.85f);

        private PlayerBlockInteraction interaction;

        private GameObject crackObject;
        private Mesh crackMesh;
        private Material crackMaterial;
        private Texture2D[] crackTextures;
        private int currentStage = -1;

        private void Awake()
        {
            interaction = GetComponent<PlayerBlockInteraction>();
        }

        private void Start()
        {
            BuildCrackOverlay();
        }

        private void OnDestroy()
        {
            if (crackObject != null)
                Destroy(crackObject);
            if (crackMaterial != null)
                Destroy(crackMaterial);
            if (crackMesh != null)
                Destroy(crackMesh);

            if (crackTextures != null)
            {
                foreach (Texture2D texture in crackTextures)
                {
                    if (texture != null)
                        Destroy(texture);
                }
            }
        }

        private void LateUpdate()
        {
            bool showCracks = interaction.HasMiningTarget
                              && interaction.MiningProgress01 > 0f
                              && interaction.ActiveProfile.Radius <= 0f;
            if (crackObject.activeSelf != showCracks)
                crackObject.SetActive(showCracks);

            if (showCracks)
            {
                crackObject.transform.position = interaction.MiningCell + new Vector3(0.5f, 0.5f, 0.5f);

                int stage = Mathf.Clamp(
                    (int)(interaction.MiningProgress01 * crackStages), 0, crackStages - 1);
                if (stage != currentStage)
                {
                    currentStage = stage;
                    crackMaterial.mainTexture = crackTextures[stage];
                }
            }
        }

        // ------------------------------------------------------------------
        // Crack overlay
        // ------------------------------------------------------------------

        private void BuildCrackOverlay()
        {
            crackObject = new GameObject("BlockCrackOverlay");
            crackObject.transform.rotation = Quaternion.identity;

            crackTextures = GenerateCrackTextures();

            crackMesh = BuildCubeMesh(CrackInflation);
            var filter = crackObject.AddComponent<MeshFilter>();
            filter.sharedMesh = crackMesh;

            var meshRenderer = crackObject.AddComponent<MeshRenderer>();
            meshRenderer.shadowCastingMode = ShadowCastingMode.Off;
            meshRenderer.receiveShadows = false;

            Shader unlit = Shader.Find("Universal Render Pipeline/Unlit");
            if (unlit == null)
                unlit = Shader.Find("Unlit/Transparent");
            crackMaterial = new Material(unlit) { mainTexture = crackTextures[0] };
            if (crackMaterial.HasProperty("_Surface"))
            {
                crackMaterial.SetFloat("_Surface", 1f);
                crackMaterial.SetOverrideTag("RenderType", "Transparent");
                crackMaterial.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                crackMaterial.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
                crackMaterial.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
                crackMaterial.SetFloat("_ZWrite", 0f);
            }

            // Above the terrain's transparent submesh so cracks show on glass/water-adjacent faces too.
            crackMaterial.renderQueue = (int)RenderQueue.Transparent + 10;
            meshRenderer.sharedMaterial = crackMaterial;

            crackObject.SetActive(false);
        }

        /// <summary>
        /// Cumulative crack stages: one fixed-seed set of jagged polylines
        /// radiating from the center; stage N draws the first few, each later
        /// stage draws more of the same set — so cracks GROW instead of
        /// reshuffling every stage.
        /// </summary>
        private Texture2D[] GenerateCrackTextures()
        {
            const int size = 32;
            var random = new System.Random(9137);

            // Pre-roll every crack polyline once, shared by all stages.
            int totalCracks = 2 * crackStages;
            var cracks = new List<List<Vector2>>(totalCracks);
            for (int i = 0; i < totalCracks; i++)
            {
                var points = new List<Vector2>();
                var position = new Vector2(
                    size * 0.5f + (float)(random.NextDouble() - 0.5) * 8f,
                    size * 0.5f + (float)(random.NextDouble() - 0.5) * 8f);
                points.Add(position);

                float angle = (float)random.NextDouble() * Mathf.PI * 2f;
                int segments = 3 + random.Next(3);
                for (int s = 0; s < segments; s++)
                {
                    angle += (float)(random.NextDouble() - 0.5) * 1.6f;
                    float length = 3f + (float)random.NextDouble() * 5f;
                    position += new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * length;
                    points.Add(position);
                }

                cracks.Add(points);
            }

            var textures = new Texture2D[crackStages];
            var clear = new Color32(0, 0, 0, 0);
            Color32 ink = crackColor;

            for (int stage = 0; stage < crackStages; stage++)
            {
                var texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
                {
                    name = $"CrackStage{stage}",
                    filterMode = FilterMode.Point,
                    wrapMode = TextureWrapMode.Clamp,
                };

                var pixels = new Color32[size * size];
                for (int i = 0; i < pixels.Length; i++)
                    pixels[i] = clear;

                int visibleCracks = Mathf.Min(cracks.Count, 2 + stage * 2);
                for (int crack = 0; crack < visibleCracks; crack++)
                {
                    List<Vector2> points = cracks[crack];
                    for (int p = 0; p < points.Count - 1; p++)
                        DrawLine(pixels, size, points[p], points[p + 1], ink);
                }

                texture.SetPixels32(pixels);
                texture.Apply(false, false);
                textures[stage] = texture;
            }

            return textures;
        }

        private static void DrawLine(Color32[] pixels, int size, Vector2 from, Vector2 to, Color32 color)
        {
            int steps = Mathf.CeilToInt((to - from).magnitude * 2f) + 1;
            for (int i = 0; i <= steps; i++)
            {
                Vector2 point = Vector2.Lerp(from, to, (float)i / steps);
                int x = Mathf.RoundToInt(point.x);
                int y = Mathf.RoundToInt(point.y);
                if (x < 0 || x >= size || y < 0 || y >= size)
                    continue;

                pixels[y * size + x] = color;
                if (x + 1 < size)
                    pixels[y * size + x + 1] = color; // 2px thick so it reads at a distance
            }
        }

        /// <summary>Unit cube centered on the origin, one 0-1 UV quad per face (same basis as the terrain mesher).</summary>
        private static Mesh BuildCubeMesh(float scale)
        {
            Vector3[] normals = { Vector3.up, Vector3.down, Vector3.forward, Vector3.back, Vector3.right, Vector3.left };
            Vector3[] ups = { Vector3.forward, Vector3.forward, Vector3.up, Vector3.up, Vector3.up, Vector3.up };

            var vertices = new Vector3[24];
            var meshNormals = new Vector3[24];
            var uvs = new Vector2[24];
            var triangles = new int[36];

            for (int face = 0; face < 6; face++)
            {
                Vector3 n = normals[face];
                Vector3 v = ups[face];
                Vector3 u = Vector3.Cross(n, v);
                int b = face * 4;

                vertices[b + 0] = (n - u - v) * (0.5f * scale);
                vertices[b + 1] = (n - u + v) * (0.5f * scale);
                vertices[b + 2] = (n + u + v) * (0.5f * scale);
                vertices[b + 3] = (n + u - v) * (0.5f * scale);

                uvs[b + 0] = new Vector2(0f, 0f);
                uvs[b + 1] = new Vector2(0f, 1f);
                uvs[b + 2] = new Vector2(1f, 1f);
                uvs[b + 3] = new Vector2(1f, 0f);

                for (int i = 0; i < 4; i++)
                    meshNormals[b + i] = n;

                int t = face * 6;
                triangles[t + 0] = b;
                triangles[t + 1] = b + 1;
                triangles[t + 2] = b + 2;
                triangles[t + 3] = b;
                triangles[t + 4] = b + 2;
                triangles[t + 5] = b + 3;
            }

            return new Mesh
            {
                name = "BlockCrackCube",
                vertices = vertices,
                normals = meshNormals,
                uv = uvs,
                triangles = triangles,
            };
        }
    }
}
