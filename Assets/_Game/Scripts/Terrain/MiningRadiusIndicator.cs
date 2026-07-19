using System.Collections.Generic;
using IslandGame.Data.Items;
using IslandGame.Inventory;
using IslandGame.Player;
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
    /// profile, blending toward the progress tint as the current bite's
    /// MiningProgress01 completes (radius mining's break-progress feedback —
    /// the whole-block crack cube stayed with radius-less profiles, where the
    /// block IS the bite); blocked tint (and the unfiltered sphere∩terrain
    /// shape, since nothing would actually carve) when it is tier-blocked or
    /// unbreakable — the permission verdict comes from
    /// PlayerBlockInteraction's AimedBlockMinable, i.e. the very check that
    /// gates real mining.
    ///
    /// PLACE MODE: a block item with PlaceRadius > 0 flips the overlay to
    /// the FILL preview (VoxelWorld.CollectFillPreview — the read-only twin
    /// of FillSphere, sharing its center bias, eligibility rule and
    /// player-capsule exclusion): the shape shown is exactly the material the
    /// next place click adds, tinted with placeColor.
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

        [Tooltip("Tint the valid overlay blends toward as the current bite's mining progress completes — the radius-mode replacement for the whole-block crack cube, on the exact bite shape.")]
        [SerializeField] private Color progressColor = new Color(1f, 0.55f, 0.15f, 0.5f);

        [Tooltip("Overlay tint while previewing a radius PLACEMENT fill (block item with Place Radius > 0) — the material the next place click adds.")]
        [SerializeField] private Color placeColor = new Color(0.35f, 0.85f, 1f, 0.3f);

        private PlayerBlockInteraction interaction;
        private HotbarSelector selector;
        private PlayerReferences references;
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
        private bool lastPlaceMode;
        private Vector3Int lastQuantizedPlayer;
        private int lastTerrainVersion = -1;
        private Color lastAppliedColor;
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
            references = GetComponent<PlayerReferences>();
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

            // PLACE MODE: a block item with a fill radius previews what the
            // next place click ADDS (VoxelWorld's fill preview) instead of
            // what a mining bite removes.
            ItemDefinition equipped = selector != null ? selector.EquippedItem : null;
            bool placeMode = equipped != null && equipped.PlacedBlock != null && equipped.PlaceRadius > 0f
                             && (equipped.Category == ItemCategory.Block || equipped.Category == ItemCategory.Placeable);

            bool blocked = !placeMode && !interaction.AimedBlockMinable;
            bool wholeCell = !placeMode && profile.Radius <= 0f;

            Vector3 center = default;
            var quantizedCenter = new Vector3Int(int.MinValue, 0, 0);
            if (!wholeCell)
            {
                center = placeMode
                    ? VoxelWorld.FillSphereCenter(interaction.AimedHitPoint, interaction.AimedHitNormal, equipped.PlaceRadius)
                    : profile.CarveCenter(interaction.AimedHitPoint, interaction.AimedHitNormal);
                // Half-sub-cell quantization: finer motion cannot change which
                // sub-cell centers the sphere contains by more than a sliver.
                quantizedCenter = Vector3Int.FloorToInt(center * (world.SubVoxelResolution * 2f));
            }

            // The fill skips cells overlapping the player, so the preview must
            // refresh as the player moves, not only as the aim moves.
            Vector3Int quantizedPlayer = placeMode
                ? Vector3Int.FloorToInt(references.Controller.bounds.center * 4f)
                : default;

            bool dirty = equipDirty
                         || blocked != lastBlocked
                         || wholeCell != lastWholeCell
                         || placeMode != lastPlaceMode
                         || quantizedPlayer != lastQuantizedPlayer
                         || world.TerrainVersion != lastTerrainVersion
                         || (wholeCell ? interaction.AimedCell != lastCell : quantizedCenter != lastQuantizedCenter);

            if (dirty)
            {
                RebuildOverlay(profile, equipped, center, blocked, wholeCell, placeMode);
                equipDirty = false;
                lastBlocked = blocked;
                lastWholeCell = wholeCell;
                lastPlaceMode = placeMode;
                lastQuantizedPlayer = quantizedPlayer;
                lastTerrainVersion = world.TerrainVersion;
                lastQuantizedCenter = quantizedCenter;
                lastCell = interaction.AimedCell;
            }

            if (overlayObject.activeSelf != hasMesh)
                overlayObject.SetActive(hasMesh);

            if (!hasMesh)
                return;

            if (placeMode)
            {
                ApplyColor(placeColor);
                return;
            }

            // Radius-mode break progress lives on the bite shape itself (the
            // crack cube only serves radius-less profiles — a full-block cube
            // would paint faces this bite never touches): the overlay deepens
            // toward the progress tint as the bite completes.
            float progress = interaction.HasMiningTarget ? interaction.MiningProgress01 : 0f;
            ApplyColor(blocked ? blockedColor : Color.Lerp(validColor, progressColor, progress));
        }

        private void ApplyColor(Color color)
        {
            if (color == lastAppliedColor)
                return;

            lastAppliedColor = color;
            if (overlayMaterial.HasProperty("_BaseColor"))
                overlayMaterial.SetColor("_BaseColor", color);
            else
                overlayMaterial.color = color;
        }

        // ------------------------------------------------------------------
        // Mesh building
        // ------------------------------------------------------------------

        private void RebuildOverlay(
            MiningProfile profile, ItemDefinition equipped, Vector3 center, bool blocked, bool wholeCell, bool placeMode)
        {
            subCells.Clear();

            // Place mode: exactly the material the fill will add. Valid:
            // exactly the cells the bite will clear (profile filter).
            // Blocked: nothing will carve — show the whole sphere∩terrain
            // region red so the denial is as legible as the promise.
            if (placeMode)
            {
                ushort placedId = world.Palette.GetId(equipped.PlacedBlock);
                Bounds? exclude = equipped.PlacedBlock.IsSolid ? references.Controller.bounds : (Bounds?)null;
                world.CollectFillPreview(center, equipped.PlaceRadius, placedId, exclude, subCells);
            }
            else if (wholeCell)
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
            lastAppliedColor = validColor;

            meshRenderer.sharedMaterial = overlayMaterial;
            overlayObject.SetActive(false);
        }
    }
}
