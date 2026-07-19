using System.Collections.Generic;
using IslandGame.Crafting;
using IslandGame.Data.Building;
using IslandGame.Data.Crafting;
using IslandGame.Data.Items;
using IslandGame.Inventory;
using IslandGame.Player;
using IslandGame.Terrain;
using UnityEngine;

namespace IslandGame.Building
{
    /// <summary>
    /// Valheim-style building placement, active whenever the equipped hotbar
    /// item has a PlacedPiece (the item→piece link on ItemDefinition — the
    /// hotbar IS the selection system, no parallel build menu state):
    ///
    ///   GHOST — a semi-transparent stripped clone of the piece prefab
    ///   (BuildingGhost) follows the shared camera aim ray (PlayerAimRaycast,
    ///   the same ray mining uses), green when placeable, red when blocked.
    ///
    ///   SNAPPING — nearby placed pieces (PlacedPieceRegistry.CollectNear)
    ///   offer their sockets; every compatible ghost-socket/target-socket pair
    ///   (SnapSocket.CanMate) within Snap Max Distance of the aim point is
    ///   solved with SnapSocket.SolveMatedPose and the NEAREST target socket
    ///   wins, ties broken by smallest angle to the player's desired yaw — so
    ///   the rotate key naturally cycles a snapped piece's legal orientations
    ///   (wall facing, roof ascending inward/outward) instead of fighting the
    ///   snap. No socket in range → free placement at the hit point, grid-
    ///   rounded (0.5 m XZ / 0.25 m Y) so free-built pieces still line up.
    ///   Holding the free-place modifier (Left Alt, or a "FreePlace" input
    ///   action) suppresses BOTH sockets and grid rounding for exact raw-hit
    ///   placement; validity (overlap + ground support) still applies to the
    ///   raw pose, and releasing the key restores snapping the same frame.
    ///
    ///   ROTATION — R steps the desired yaw in 45° increments. 45 (not 15 or
    ///   free) because every piece is authored on a rectilinear 2 m grid:
    ///   90° multiples keep grid alignment, the 45s allow diagonals, and
    ///   snapped pieces take their orientation from sockets anyway — finer
    ///   steps would only produce near-miss angles that read as misaligned.
    ///
    ///   VALIDITY — the prefab's actual BoxColliders (cached per prefab, with
    ///   a renderer-bounds fallback for prefabs without boxes) are overlap-
    ///   tested at the ghost pose, shrunk by a small skin so flush socket
    ///   contact never false-positives. Voxel TERRAIN overlap is deliberately
    ///   legal (foundations sink into uneven ground — the reference games'
    ///   behavior); anything else (placed pieces, the player, props) blocks.
    ///   Since the organic-terrain phase, FREE placement additionally requires
    ///   ground support: footprint probes against the real (sub-voxel) surface
    ///   must all find terrain/pieces below, within a slope tolerance — see
    ///   HasGroundSupport. Snapped placements are exempt (sockets = support).
    ///
    ///   CONFIRM — place button instantiates the real prefab under the
    ///   registry and calls BuildingPiece.Initialize (registry registration +
    ///   IFunctionalPlaceable.Init, the Phase 1 contract).
    ///
    ///   COST — what a placement consumes depends on how the piece was
    ///   selected, and each placement pays exactly once, at CONFIRM only:
    ///     • RECIPE-ARMED (crafting menu Build button): the piece's linked
    ///       building recipe (RecipeDatabase.FindForPiece) is the price — its
    ///       requirements are validated live (unaffordable/no-station turns
    ///       the ghost red) through CraftingSystem's shared validation, and
    ///       CraftingSystem.TryConsumeIngredientsFor pays atomically.
    ///       Recipe-less pieces place free (creative-style content).
    ///     • ITEM-DRIVEN (hotbar item with a PlacedPiece link): ONE unit of
    ///       the item is the price — the stack embodied its cost when it was
    ///       obtained, so the linked recipe is deliberately NOT charged on
    ///       top (that would bill the same piece twice and made item stacks
    ///       infinite blueprints before this rule existed).
    ///   A rejected or invalid placement can never consume anything.
    ///
    ///   SELECTION SOURCES — the hotbar item link (Phase 2) still works, and
    ///   the crafting menu's Build button arms a piece directly (ArmPiece).
    ///   An armed piece takes precedence and stays armed for repeat building;
    ///   deliberately switching hotbar slots hands control back to the hotbar.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(PlayerReferences))]
    public sealed class BuildingPlacementController : MonoBehaviour
    {
        [Header("Aim")]
        [Tooltip("Maximum placement distance from the camera, meters. Slightly beyond block reach — pieces are big.")]
        [SerializeField] private float reach = 6f;

        [Header("Snapping")]
        [Tooltip("Radius around the aim point in which placed pieces are considered snap candidates (their root position; generous because sockets sit up to a piece's extent away from its root).")]
        [SerializeField] private float pieceSearchRadius = 4f;

        [Tooltip("A target socket only snaps while it lies within this distance of the aim point.")]
        [SerializeField] private float snapMaxDistance = 1.5f;

        [Header("Free Placement")]
        [Tooltip("Free-place grid step on X/Z, meters.")]
        [SerializeField] private float freeGridXZ = 0.5f;

        [Tooltip("Free-place grid step on Y, meters.")]
        [SerializeField] private float freeGridY = 0.25f;

        [Tooltip("Yaw degrees per rotate-key press.")]
        [SerializeField] private float rotationStepDegrees = 45f;

        [Header("Validity")]
        [Tooltip("Meters shaved off every overlap box side so flush contact with the snapped-to piece never reads as overlap.")]
        [SerializeField] private float overlapSkin = 0.05f;

        [Header("Ground (Free Placement)")]
        [Tooltip("Every footprint probe of a FREE-placed piece must find support (voxel terrain or a placed piece) within this many meters below the piece base. Snapped pieces skip the check — their support comes through the socket.")]
        [SerializeField] private float groundProbeDepth = 2f;

        [Tooltip("Maximum height difference between the footprint's ground probes, meters. Organic terrain slopes — beyond this the ground is too steep/uneven to build on unprepared; mine it flat first, like the reference games.")]
        [SerializeField] private float maxGroundUnevenness = 0.75f;

        [SerializeField] private Color validGhostColor = new Color(0.25f, 0.9f, 0.35f, 0.4f);
        [SerializeField] private Color invalidGhostColor = new Color(0.95f, 0.25f, 0.2f, 0.4f);

        private PlayerReferences references;
        private HotbarSelector selector;
        private CraftingSystem craftingSystem;
        private InventorySystem inventory;
        private BuildingGhost ghost;

        private readonly RaycastHit[] rayBuffer = new RaycastHit[16];
        private readonly Collider[] overlapBuffer = new Collider[32];
        private readonly List<BuildingPiece> nearbyPieces = new List<BuildingPiece>(32);

        private struct OverlapShape
        {
            public Vector3 LocalCenter;
            public Vector3 LocalHalfExtents;
            public Quaternion LocalRotation;
        }

        // Per-prefab collider shapes; built once, reused every frame.
        private readonly Dictionary<GameObject, List<OverlapShape>> shapeCache =
            new Dictionary<GameObject, List<OverlapShape>>();

        private BuildingPieceDefinition activePiece;
        private BuildingPieceDefinition menuArmedPiece;
        private Vector3 ghostPosition;
        private Quaternion ghostRotation;
        private bool hasPose;
        private bool poseValid;
        private bool poseSnapped;
        private int yawSteps;

        // Cost cache: re-resolved only when the active piece changes.
        private BuildingPieceDefinition costRecipePiece;
        private RecipeDefinition costRecipe;
        private bool costValid = true;
        private string costReason = string.Empty;

        // The hotbar item driving the ghost, null when the piece is
        // menu-armed. Whichever is set determines what CONFIRM consumes.
        private ItemDefinition sourceItem;

        /// <summary>True while an equipped item is driving a ghost (HUD hooks read these).</summary>
        public bool IsBuildModeActive => activePiece != null;

        public BuildingPieceDefinition ActivePiece => activePiece;
        public bool GhostVisible => hasPose;
        public bool GhostValid => hasPose && poseValid && costValid;
        public bool GhostSnapped => hasPose && poseSnapped;

        /// <summary>The building recipe pricing the active piece (null while item-driven — the item is the price — or free/no recipe).</summary>
        public RecipeDefinition CostRecipe => sourceItem == null ? costRecipe : null;

        /// <summary>Why the cost check fails ("Missing 3 × Wood Plank.") — empty while payable. For the future HUD.</summary>
        public string CostReason => costValid ? string.Empty : costReason;

        private void Awake()
        {
            references = GetComponent<PlayerReferences>();
            selector = GetComponent<HotbarSelector>();
            craftingSystem = GetComponent<CraftingSystem>();
            inventory = GetComponent<InventorySystem>();
            ghost = new BuildingGhost(validGhostColor, invalidGhostColor);
        }

        private void OnEnable()
        {
            references.InputHandler.PlacePressed += TryPlace;
            references.InputHandler.RotatePiecePressed += StepRotation;
            if (selector != null)
                selector.SelectedSlotChanged += OnHotbarSlotChanged;
        }

        private void OnDisable()
        {
            references.InputHandler.PlacePressed -= TryPlace;
            references.InputHandler.RotatePiecePressed -= StepRotation;
            if (selector != null)
                selector.SelectedSlotChanged -= OnHotbarSlotChanged;
            ghost.SetVisible(false);
            hasPose = false;
        }

        /// <summary>
        /// Crafting-menu entry point: arms the given piece for placement (null
        /// disarms). Stays armed after each placement for repeat building;
        /// a deliberate hotbar slot switch hands selection back to the hotbar.
        /// </summary>
        public void ArmPiece(BuildingPieceDefinition piece)
        {
            menuArmedPiece = piece;
        }

        private void OnHotbarSlotChanged(int index)
        {
            menuArmedPiece = null;
        }

        private void OnDestroy()
        {
            ghost.Destroy();
        }

        private void Update()
        {
            BuildingPieceDefinition piece = ResolveActivePiece();
            if (piece == null || references.InputHandler.GameplayBlocked)
            {
                activePiece = piece;
                hasPose = false;
                ghost.SetVisible(false);
                return;
            }

            activePiece = piece;
            ghost.SetPrefab(piece.Prefab);

            if (!PlayerAimRaycast.Raycast(references.CameraPivot, transform, reach, rayBuffer, out RaycastHit hit))
            {
                hasPose = false;
                ghost.SetVisible(false);
                return;
            }

            Quaternion desiredRotation = Quaternion.Euler(0f, yawSteps * rotationStepDegrees, 0f);

            // Free-place override (hold Alt / "FreePlace"): the ghost tracks
            // the raw aim hit — no sockets, no grid rounding. Read every
            // frame, so releasing the key resumes snapping immediately.
            bool freePlace = references.InputHandler.FreePlaceHeld;

            poseSnapped = !freePlace
                          && TrySnapToSocket(piece, hit.point, desiredRotation, out ghostPosition, out ghostRotation);
            if (!poseSnapped)
            {
                ghostPosition = freePlace ? hit.point : SnapToGrid(hit.point);
                ghostRotation = desiredRotation;
            }

            poseValid = !HasBlockingOverlap(piece.Prefab, ghostPosition, ghostRotation)
                        && (poseSnapped || HasGroundSupport(piece.Prefab, ghostPosition, ghostRotation));
            RefreshCost(piece);
            hasPose = true;

            ghost.SetPose(ghostPosition, ghostRotation);
            ghost.SetValid(poseValid && costValid);
            ghost.SetVisible(true);
        }

        // ------------------------------------------------------------------
        // Cost (the Phase 3 recipe link)
        // ------------------------------------------------------------------

        private void RefreshCost(BuildingPieceDefinition piece)
        {
            // Item-driven placement: the stack is the price, so availability
            // of the item — not the recipe — is what gates the ghost.
            if (sourceItem != null)
            {
                costValid = inventory != null && inventory.GetItemCount(sourceItem) > 0;
                costReason = costValid ? string.Empty : $"No {sourceItem.DisplayName} left.";
                return;
            }

            if (piece != costRecipePiece)
            {
                costRecipePiece = piece;
                RecipeDatabase recipes = RecipeDatabase.Instance;
                costRecipe = recipes != null ? recipes.FindForPiece(piece) : null;
            }

            if (costRecipe == null || craftingSystem == null)
            {
                // No recipe = free content; no CraftingSystem = nothing can
                // ever be consumed, so don't block placement on it either.
                costValid = true;
                costReason = string.Empty;
                return;
            }

            costValid = craftingSystem.ValidateRequirements(costRecipe, out costReason);
        }

        // ------------------------------------------------------------------
        // Selection (the hotbar is the source of truth)
        // ------------------------------------------------------------------

        private BuildingPieceDefinition ResolveActivePiece()
        {
            // Menu-armed piece wins; the hotbar item link is the fallback.
            // sourceItem records WHICH path is active — it decides whether
            // CONFIRM consumes the item or the recipe.
            sourceItem = null;
            BuildingPieceDefinition piece = menuArmedPiece;
            if (piece == null)
            {
                var equipped = selector != null ? selector.EquippedItem : null;
                piece = equipped != null ? equipped.PlacedPiece : null;
                if (piece != null)
                    sourceItem = equipped;
            }

            if (piece == null)
                return null;

            if (piece.Prefab == null)
            {
                // Authoring error, not a runtime state — say it once per piece.
                if (activePiece != piece)
                    Debug.LogError($"Building piece '{piece.Id}' has no prefab — fix it in the Building Piece Editor.", piece);
                return null;
            }

            return piece;
        }

        // ------------------------------------------------------------------
        // Snapping
        // ------------------------------------------------------------------

        private bool TrySnapToSocket(
            BuildingPieceDefinition piece, Vector3 aimPoint, Quaternion desiredRotation,
            out Vector3 position, out Quaternion rotation)
        {
            position = default;
            rotation = default;

            IReadOnlyList<SnapSocket> ghostSockets = piece.Sockets;
            if (ghostSockets.Count == 0)
                return false;

            PlacedPieceRegistry registry = PlacedPieceRegistry.Instance;
            if (registry == null)
                return false;

            registry.CollectNear(aimPoint, pieceSearchRadius, nearbyPieces);

            // Nearest compatible target socket wins; among ghost sockets that
            // can mate it (equal distance by definition), the pose closest to
            // the player's desired yaw wins — that's what makes the rotate key
            // cycle orientations while snapped.
            const float distanceTie = 0.001f;
            float bestDistance = float.MaxValue;
            float bestAngle = float.MaxValue;
            bool found = false;

            for (int p = 0; p < nearbyPieces.Count; p++)
            {
                BuildingPiece placed = nearbyPieces[p];
                if (placed == null || placed.Definition == null)
                    continue;

                IReadOnlyList<SnapSocket> targetSockets = placed.Definition.Sockets;
                for (int t = 0; t < targetSockets.Count; t++)
                {
                    SnapSocket target = targetSockets[t];
                    placed.GetSocketWorldPose(target, out Vector3 targetPos, out Quaternion targetRot);

                    float distance = Vector3.Distance(targetPos, aimPoint);
                    if (distance > snapMaxDistance || distance > bestDistance + distanceTie)
                        continue;

                    for (int g = 0; g < ghostSockets.Count; g++)
                    {
                        SnapSocket ghostSocket = ghostSockets[g];
                        if (!SnapSocket.CanMate(ghostSocket, target))
                            continue;

                        ghostSocket.SolveMatedPose(targetPos, targetRot, out Vector3 solvedPos, out Quaternion solvedRot);
                        float angle = Quaternion.Angle(solvedRot, desiredRotation);

                        bool better = distance < bestDistance - distanceTie
                                      || (Mathf.Abs(distance - bestDistance) <= distanceTie && angle < bestAngle);
                        if (!better)
                            continue;

                        bestDistance = distance;
                        bestAngle = angle;
                        position = solvedPos;
                        rotation = solvedRot;
                        found = true;
                    }
                }
            }

            return found;
        }

        private Vector3 SnapToGrid(Vector3 point)
        {
            return new Vector3(
                Mathf.Round(point.x / freeGridXZ) * freeGridXZ,
                Mathf.Round(point.y / freeGridY) * freeGridY,
                Mathf.Round(point.z / freeGridXZ) * freeGridXZ);
        }

        private void StepRotation()
        {
            if (activePiece == null)
                return; // R stays free for other systems while not building

            int steps = Mathf.Max(1, Mathf.RoundToInt(360f / rotationStepDegrees));
            yawSteps = (yawSteps + 1) % steps;
        }

        // ------------------------------------------------------------------
        // Validity (real collider boxes, not a point check)
        // ------------------------------------------------------------------

        private bool HasBlockingOverlap(GameObject prefab, Vector3 position, Quaternion rotation)
        {
            List<OverlapShape> shapes = GetOverlapShapes(prefab);

            for (int s = 0; s < shapes.Count; s++)
            {
                OverlapShape shape = shapes[s];
                Vector3 worldCenter = position + rotation * shape.LocalCenter;
                Quaternion worldRotation = rotation * shape.LocalRotation;
                var halfExtents = new Vector3(
                    Mathf.Max(0.01f, shape.LocalHalfExtents.x - overlapSkin),
                    Mathf.Max(0.01f, shape.LocalHalfExtents.y - overlapSkin),
                    Mathf.Max(0.01f, shape.LocalHalfExtents.z - overlapSkin));

                int count = Physics.OverlapBoxNonAlloc(
                    worldCenter, halfExtents, overlapBuffer, worldRotation, ~0, QueryTriggerInteraction.Ignore);

                for (int i = 0; i < count; i++)
                {
                    Collider other = overlapBuffer[i];

                    // Terrain intersection is legal — foundations sink into
                    // uneven voxel ground exactly like the reference games.
                    if (other.GetComponentInParent<ChunkView>() != null)
                        continue;

                    // Everything else blocks: placed pieces, props, world items
                    // and the player (never build a wall inside yourself).
                    return true;
                }
            }

            return false;
        }

        private List<OverlapShape> GetOverlapShapes(GameObject prefab)
        {
            if (shapeCache.TryGetValue(prefab, out List<OverlapShape> cached))
                return cached;

            var shapes = new List<OverlapShape>();
            Matrix4x4 rootInverse = prefab.transform.worldToLocalMatrix;
            Quaternion rootRotationInverse = Quaternion.Inverse(prefab.transform.rotation);

            foreach (BoxCollider box in prefab.GetComponentsInChildren<BoxCollider>(true))
            {
                Vector3 lossyScale = box.transform.lossyScale;
                shapes.Add(new OverlapShape
                {
                    LocalCenter = rootInverse.MultiplyPoint3x4(box.transform.TransformPoint(box.center)),
                    LocalRotation = rootRotationInverse * box.transform.rotation,
                    LocalHalfExtents = new Vector3(
                        Mathf.Abs(box.size.x * lossyScale.x),
                        Mathf.Abs(box.size.y * lossyScale.y),
                        Mathf.Abs(box.size.z * lossyScale.z)) * 0.5f,
                });
            }

            // Prefabs without box colliders (future art) still get a real
            // volume test: one axis-aligned box around all mesh bounds.
            if (shapes.Count == 0)
            {
                var combined = new Bounds();
                bool hasBounds = false;

                foreach (MeshFilter filter in prefab.GetComponentsInChildren<MeshFilter>(true))
                {
                    if (filter.sharedMesh == null)
                        continue;

                    Matrix4x4 toRoot = rootInverse * filter.transform.localToWorldMatrix;
                    Bounds meshBounds = filter.sharedMesh.bounds;
                    for (int corner = 0; corner < 8; corner++)
                    {
                        var point = new Vector3(
                            (corner & 1) == 0 ? meshBounds.min.x : meshBounds.max.x,
                            (corner & 2) == 0 ? meshBounds.min.y : meshBounds.max.y,
                            (corner & 4) == 0 ? meshBounds.min.z : meshBounds.max.z);
                        Vector3 transformed = toRoot.MultiplyPoint3x4(point);

                        if (!hasBounds)
                        {
                            combined = new Bounds(transformed, Vector3.zero);
                            hasBounds = true;
                        }
                        else
                        {
                            combined.Encapsulate(transformed);
                        }
                    }
                }

                if (hasBounds)
                {
                    shapes.Add(new OverlapShape
                    {
                        LocalCenter = combined.center,
                        LocalHalfExtents = combined.extents,
                        LocalRotation = Quaternion.identity,
                    });
                }
            }

            shapeCache.Add(prefab, shapes);
            return shapes;
        }

        // ------------------------------------------------------------------
        // Ground support (organic-terrain phase)
        // ------------------------------------------------------------------

        // Probe corners sit at half the footprint extents so a piece may hang
        // slightly over a ledge without rejecting — the reference games allow
        // modest overhang as long as the body of the piece is grounded.
        private const float GroundProbeInset = 0.5f;
        private const float GroundProbeStartAbove = 0.5f;

        /// <summary>
        /// Free placement must rest on real ground: five probes across the
        /// piece footprint (center + inset corners at its base height) each
        /// raycast down and must find voxel terrain or a placed piece within
        /// Ground Probe Depth, and the spread between the highest and lowest
        /// support must stay within Max Ground Unevenness. Terrain is now
        /// organically sloped and the probes hit the ACTUAL sub-voxel carved
        /// collision, so this is the real surface, not the block grid. Snapped
        /// pieces never get here — sockets carry their support.
        /// </summary>
        private bool HasGroundSupport(GameObject prefab, Vector3 position, Quaternion rotation)
        {
            List<OverlapShape> shapes = GetOverlapShapes(prefab);
            if (shapes.Count == 0)
                return true; // no volume — nothing that could float or bury

            // Footprint = local AABB over every overlap box's rotated corners.
            var bounds = new Bounds(shapes[0].LocalCenter, Vector3.zero);
            for (int s = 0; s < shapes.Count; s++)
            {
                OverlapShape shape = shapes[s];
                Vector3 e = shape.LocalHalfExtents;
                for (int corner = 0; corner < 8; corner++)
                {
                    var local = new Vector3(
                        (corner & 1) == 0 ? -e.x : e.x,
                        (corner & 2) == 0 ? -e.y : e.y,
                        (corner & 4) == 0 ? -e.z : e.z);
                    bounds.Encapsulate(shape.LocalCenter + shape.LocalRotation * local);
                }
            }

            float baseY = bounds.min.y;
            Vector3 center = bounds.center;
            float insetX = bounds.extents.x * GroundProbeInset;
            float insetZ = bounds.extents.z * GroundProbeInset;

            float highest = float.MinValue;
            float lowest = float.MaxValue;
            for (int i = 0; i < 5; i++)
            {
                var probeLocal = new Vector3(
                    i == 0 ? center.x : (i & 1) == 0 ? center.x - insetX : center.x + insetX,
                    baseY,
                    i == 0 ? center.z : (i & 2) == 0 ? center.z - insetZ : center.z + insetZ);

                if (!ProbeGround(position + rotation * probeLocal, out float supportY))
                    return false; // a footprint corner hangs over nothing

                if (supportY > highest)
                    highest = supportY;
                if (supportY < lowest)
                    lowest = supportY;
            }

            return highest - lowest <= maxGroundUnevenness;
        }

        /// <summary>One downward support probe: the topmost terrain/piece surface below the point, if any is in range.</summary>
        private bool ProbeGround(Vector3 point, out float supportY)
        {
            supportY = float.MinValue;
            Vector3 origin = point + Vector3.up * GroundProbeStartAbove;
            int count = Physics.RaycastNonAlloc(
                origin, Vector3.down, rayBuffer, GroundProbeStartAbove + groundProbeDepth,
                ~0, QueryTriggerInteraction.Ignore);

            bool found = false;
            for (int i = 0; i < count; i++)
            {
                RaycastHit hit = rayBuffer[i];
                if (hit.collider == null)
                    continue;

                // Support = the voxel world or an existing structure. Loose
                // props, items, creatures and the player do NOT hold a house up.
                if (hit.collider.GetComponentInParent<ChunkView>() == null
                    && hit.collider.GetComponentInParent<BuildingPiece>() == null)
                    continue;

                if (hit.point.y > supportY)
                {
                    supportY = hit.point.y;
                    found = true;
                }
            }

            return found;
        }

        // ------------------------------------------------------------------
        // Confirm
        // ------------------------------------------------------------------

        private void TryPlace()
        {
            if (activePiece == null || !hasPose || !poseValid || !costValid)
                return;

            // Contract check BEFORE payment: a broken prefab must never eat
            // ingredients for nothing.
            if (activePiece.Prefab.GetComponent<BuildingPiece>() == null)
            {
                Debug.LogError(
                    $"Prefab of building piece '{activePiece.Id}' has no BuildingPiece on its root — " +
                    "open it in the Building Piece Editor and use the stamp button.", activePiece);
                return;
            }

            // Pay-at-confirm: revalidates and consumes atomically — only a
            // placement that is actually happening costs anything, and each
            // path pays exactly once (item OR recipe, never both).
            if (sourceItem != null)
            {
                if (inventory == null || inventory.RemoveItem(sourceItem, 1) != 1)
                {
                    costValid = false; // ghost turns red with the reason next frame
                    costReason = $"No {sourceItem.DisplayName} left.";
                    return;
                }
            }
            else if (costRecipe != null && craftingSystem != null
                     && !craftingSystem.TryConsumeIngredientsFor(costRecipe, out costReason))
            {
                costValid = false; // ghost turns red with the reason next frame
                return;
            }

            PlacedPieceRegistry registry = PlacedPieceRegistry.Instance;
            Transform parent = registry != null ? registry.transform : null;

            GameObject instance = Instantiate(activePiece.Prefab, ghostPosition, ghostRotation, parent);
            instance.GetComponent<BuildingPiece>().Initialize(activePiece);
        }
    }
}
