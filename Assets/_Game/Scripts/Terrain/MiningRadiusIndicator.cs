using System.Collections.Generic;
using IslandGame.Data.Items;
using IslandGame.Inventory;
using UnityEngine;
using UnityEngine.Rendering;

namespace IslandGame.Terrain
{
    /// <summary>
    /// The sphere-of-effect mining highlight (organic-terrain phase 3),
    /// replacing the old single-block wireframe: a translucent overlay mesh
    /// that hugs EXACTLY the terrain volume the next completed mining hit
    /// will carve — tools and bare hands alike.
    ///
    /// WHY A VOLUME OVERLAY AND NOT A FLOATING SPHERE: a ghost sphere at the
    /// hit point mostly shows air (half of any surface bite) and lies about
    /// partially-carved or mixed-material regions. This overlay is built from
    /// VoxelWorld.CollectCarvePreview — the read-only twin of CarveSphere,
    /// same sub-cell center-in-sphere test, same per-cell permission filter,
    /// same quarter-radius center bias from the shared MiningProfile — so the
    /// glowing shape IS the bite, sub-cell for sub-cell, hugging the real
    /// surface. Only the removed volume's boundary faces are emitted (offset
    /// 4 mm off the terrain so nothing z-fights).
    ///
    /// STATES: valid tint when the aimed block is minable by the active
    /// profile; blocked tint (and the unfiltered sphere∩terrain shape, since
    /// nothing would actually carve) when it is tier-blocked or unbreakable —
    /// the permission verdict comes from PlayerBlockInteraction's
    /// AimedBlockMinable, i.e. the very check that gates real mining.
    ///
    /// COST CONTROL: rebuilds only when something the shape depends on
    /// changed — the equip event (HotbarSelector.EquippedItemChanged, the
    /// same event the held-item system uses — no inventory polling), the
    /// carve center quantized to half a sub-cell, the permission state, or
    /// VoxelWorld.TerrainVersion (terrain edited under the crosshair).
    /// Radius-less authored tools preview their whole aimed cell instead
    /// (real occupancy via CollectCellPreview).
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(PlayerBlockInteraction))]
    public sealed class MiningRadiusIndicator : MonoBehaviour
    {
        // Boundary faces float this far off the terrain surface: above the
        // z-buffer epsilon at interaction range, below anything readable.
        private const float FaceOffset = 0.004f;

        [Tooltip("Overlay tint while the aimed block is minable with the active profile.")]
        [SerializeField] private Color validColor = new Color(1f, 1f, 1f, 0.27f);

        [Tooltip("Overlay tint while the aimed block is unbreakable or above the active tier — nothing will carve.")]
        [SerializeField] private Color blockedColor = new Color(1f, 0.28f, 0.22f, 0.3f);

        private PlayerBlockInteraction interaction;
        private HotbarSelector selector;
        private VoxelWorld world;

        private GameObject overlayObject;
        private Mesh overlayMesh;
        private Material overlayMaterial;

        private readonly List<Vector3Int> subCells = new List<Vector3Int>(1024);
        private readonly HashSet<Vector3Int> occupancy = new HashSet<Vector3Int>();
        private readonly List<Vector3> vertices = new List<Vector3>(2048);
        private readonly List<int> triangles = new List<int>(4096);

        // Rebuild-when-changed cache keys (see COST CONTROL above).
        private bool equipDirty = true;
        private Vector3Int lastQuantizedCenter;
        private Vector3Int lastCell;
        private bool lastBlocked;
        private bool lastWholeCell;
        private int lastTerrainVersion = -1;
        private bool lastAppliedBlockedColor;
        private bool hasMesh;

        // Face basis in BlockFace order (Top,Bottom,North,South,East,West) —
        // same tables the mesher uses, local because its copies are private.
        private static readonly Vector3Int[] FaceDirections =
        {
            new Vector3Int(0, 1, 0), new Vector3Int(0, -1, 0),
            new Vector3Int(0, 0, 1), new Vector3Int(0, 0, -1),
            new Vector3Int(1, 0, 0), new Vector3Int(-1, 0, 0),
        };

        private static readonly Vector3[] FaceN =
        {
            Vector3.up, Vector3.down, Vector3.forward, Vector3.back, Vector3.right, Vector3.left,
        };

        private static readonly Vector3[] FaceU =
        {
            Vector3.right, Vector3.left, Vector3.left, Vector3.right, Vector3.forward, Vector3.back,
        };

        private static readonly Vector3[] FaceV =
        {
            Vector3.forward, Vector3.forward, Vector3.up, Vector3.up, Vector3.up, Vector3.up,
        };

        private void Awake()
        {
            interaction = GetComponent<PlayerBlockInteraction>();
            selector = GetComponent<HotbarSelector>();
        }

        private void Start()
        {
            world = FindFirstObjectByType<VoxelWorld>();
            if (world == null)
            {
                Debug.LogError("MiningRadiusIndicator: no VoxelWorld in the scene — indicator disabled.", this);
                enabled = false;
                return;
            }

            BuildOverlayObject();
        }

        private void OnEnable()
        {
            if (selector != null)
                selector.EquippedItemChanged += OnEquippedItemChanged;
        }

        private void OnDisable()
        {
            if (selector != null)
                selector.EquippedItemChanged -= OnEquippedItemChanged;
            if (overlayObject != null)
                overlayObject.SetActive(false);
        }

        private void OnDestroy()
        {
            if (overlayObject != null)
                Destroy(overlayObject);
            if (overlayMesh != null)
                Destroy(overlayMesh);
            if (overlayMaterial != null)
                Destroy(overlayMaterial);
        }

        /// <summary>Equip changed (tool swap, stack ran out, pickup into the hand) — the shape's radius/tier inputs are stale.</summary>
        private void OnEquippedItemChanged(ItemDefinition item)
        {
            equipDirty = true;
        }

        // LateUpdate: PlayerBlockInteraction's Update has already refreshed
        // aim state and ActiveProfile this frame — never work from last frame.
        private void LateUpdate()
        {
            if (world == null || overlayObject == null)
                return;

            if (!interaction.HasAimedBlock)
            {
                if (overlayObject.activeSelf)
                    overlayObject.SetActive(false);
                return;
            }

            MiningProfile profile = interaction.ActiveProfile;
            bool blocked = !interaction.AimedBlockMinable;
            bool wholeCell = profile.Radius <= 0f;

            Vector3 center = default;
            var quantizedCenter = new Vector3Int(int.MinValue, 0, 0);
            if (!wholeCell)
            {
                center = profile.CarveCenter(interaction.AimedHitPoint, interaction.AimedHitNormal);
                // Half-sub-cell quantization: finer motion cannot change which
                // sub-cell centers the sphere contains by more than a sliver.
                quantizedCenter = Vector3Int.FloorToInt(center * (world.SubVoxelResolution * 2f));
            }

            bool dirty = equipDirty
                         || blocked != lastBlocked
                         || wholeCell != lastWholeCell
                         || world.TerrainVersion != lastTerrainVersion
                         || (wholeCell ? interaction.AimedCell != lastCell : quantizedCenter != lastQuantizedCenter);

            if (dirty)
            {
                RebuildOverlay(profile, center, blocked, wholeCell);
                equipDirty = false;
                lastBlocked = blocked;
                lastWholeCell = wholeCell;
                lastTerrainVersion = world.TerrainVersion;
                lastQuantizedCenter = quantizedCenter;
                lastCell = interaction.AimedCell;
            }

            if (overlayObject.activeSelf != hasMesh)
                overlayObject.SetActive(hasMesh);
        }

        // ------------------------------------------------------------------
        // Mesh building
        // ------------------------------------------------------------------

        private void RebuildOverlay(MiningProfile profile, Vector3 center, bool blocked, bool wholeCell)
        {
            subCells.Clear();

            // Valid: exactly the cells the bite will clear (profile filter).
            // Blocked: nothing will carve — show the whole sphere∩terrain
            // region red so the denial is as legible as the promise.
            if (wholeCell)
                world.CollectCellPreview(interaction.AimedCell, subCells);
            else
                world.CollectCarvePreview(center, profile.Radius, blocked ? null : profile.CanCarve, subCells);

            hasMesh = subCells.Count > 0;
            if (!hasMesh)
                return;

            occupancy.Clear();
            for (int i = 0; i < subCells.Count; i++)
                occupancy.Add(subCells[i]);

            vertices.Clear();
            triangles.Clear();
            float subSize = 1f / world.SubVoxelResolution;

            for (int i = 0; i < subCells.Count; i++)
            {
                Vector3Int subCell = subCells[i];
                for (int face = 0; face < 6; face++)
                {
                    if (occupancy.Contains(subCell + FaceDirections[face]))
                        continue; // interior — only the volume's skin renders

                    EmitFace(subCell, face, subSize);
                }
            }

            overlayMesh.Clear();
            overlayMesh.indexFormat = IndexFormat.UInt32;
            overlayMesh.SetVertices(vertices);
            overlayMesh.SetTriangles(triangles, 0);

            if (blocked != lastAppliedBlockedColor)
            {
                lastAppliedBlockedColor = blocked;
                Color color = blocked ? blockedColor : validColor;
                if (overlayMaterial.HasProperty("_BaseColor"))
                    overlayMaterial.SetColor("_BaseColor", color);
                else
                    overlayMaterial.color = color;
            }
        }

        private void EmitFace(Vector3Int subCell, int face, float subSize)
        {
            Vector3 n = FaceN[face];
            Vector3 u = FaceU[face];
            Vector3 v = FaceV[face];

            Vector3 center = (Vector3)subCell * subSize + new Vector3(subSize, subSize, subSize) * 0.5f;
            Vector3 offsetCenter = center + n * FaceOffset;
            float half = subSize * 0.5f;

            int baseIndex = vertices.Count;
            vertices.Add(offsetCenter + (n - u - v) * half);
            vertices.Add(offsetCenter + (n - u + v) * half);
            vertices.Add(offsetCenter + (n + u + v) * half);
            vertices.Add(offsetCenter + (n + u - v) * half);

            triangles.Add(baseIndex);
            triangles.Add(baseIndex + 1);
            triangles.Add(baseIndex + 2);
            triangles.Add(baseIndex);
            triangles.Add(baseIndex + 2);
            triangles.Add(baseIndex + 3);
        }

        // ------------------------------------------------------------------
        // Scene objects
        // ------------------------------------------------------------------

        private void BuildOverlayObject()
        {
            overlayObject = new GameObject("MiningRadiusOverlay");
            overlayObject.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);

            overlayMesh = new Mesh { name = "MiningRadiusOverlay" };
            overlayMesh.MarkDynamic();

            var filter = overlayObject.AddComponent<MeshFilter>();
            filter.sharedMesh = overlayMesh;

            var meshRenderer = overlayObject.AddComponent<MeshRenderer>();
            meshRenderer.shadowCastingMode = ShadowCastingMode.Off;
            meshRenderer.receiveShadows = false;

            // Same URP-unlit transparent recipe as the crack overlay; queued
            // just below it so cracks stay readable on top of the highlight.
            Shader unlit = Shader.Find("Universal Render Pipeline/Unlit");
            if (unlit == null)
                unlit = Shader.Find("Unlit/Transparent");
            overlayMaterial = new Material(unlit);
            if (overlayMaterial.HasProperty("_Surface"))
            {
                overlayMaterial.SetFloat("_Surface", 1f);
                overlayMaterial.SetOverrideTag("RenderType", "Transparent");
                overlayMaterial.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                overlayMaterial.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
                overlayMaterial.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
                overlayMaterial.SetFloat("_ZWrite", 0f);
            }

            overlayMaterial.renderQueue = (int)RenderQueue.Transparent + 9;
            if (overlayMaterial.HasProperty("_BaseColor"))
                overlayMaterial.SetColor("_BaseColor", validColor);
            else
                overlayMaterial.color = validColor;
            lastAppliedBlockedColor = false;

            meshRenderer.sharedMaterial = overlayMaterial;
            overlayObject.SetActive(false);
        }
    }
}
