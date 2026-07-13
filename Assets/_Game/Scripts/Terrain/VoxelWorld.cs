using System.Collections.Generic;
using IslandGame.Data.Blocks;
using IslandGame.Inventory;
using IslandGame.Player;
using UnityEngine;
using UnityEngine.Rendering;

namespace IslandGame.Terrain
{
    /// <summary>
    /// The chunk manager: owns the block palette, the runtime texture atlas
    /// and terrain materials, streams chunks around the player, and is the one
    /// entry point for block reads/writes (GetBlock/SetBlock).
    ///
    /// STREAMING uses two rings: chunk DATA generates out to renderDistance+1,
    /// meshes build only out to renderDistance and only once all four
    /// horizontal neighbors have data — so border-face culling always sees
    /// real neighbor blocks and chunk seams can't show gaps or phantom walls.
    /// Both stages are budgeted per frame to avoid hitches. Far chunks release
    /// their VIEW (GameObject + meshes) to a pool; their DATA stays resident
    /// (32 KB each), so block edits survive walking away within a session —
    /// disk persistence is a later phase.
    ///
    /// EDITS remesh synchronously (the edited chunk, plus the touching
    /// neighbor when the block sits on a border) — never the whole world.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class VoxelWorld : MonoBehaviour
    {
        [Header("Streaming")]
        [Tooltip("Player transform chunks stream around. Auto-resolved to the PlayerReferences object when empty.")]
        [SerializeField] private Transform player;

        [Tooltip("Meshed chunk radius around the player (data loads one ring further).")]
        [Range(2, 16)]
        [SerializeField] private int renderDistance = 6;

        [Tooltip("Max chunk data generations per frame.")]
        [Min(1)]
        [SerializeField] private int dataBudgetPerFrame = 6;

        [Tooltip("Max chunk mesh builds per frame (edits remesh instantly regardless).")]
        [Min(1)]
        [SerializeField] private int meshBudgetPerFrame = 2;

        [Header("Sub-Voxel Detail")]
        [Tooltip("Sub-cells per block axis for damaged (promoted) blocks. 8 → 512 sub-cells per block and 8×8 texture slices per face (2×2 texels on a 16 px block texture). Higher = finer carving but cubically more sub-cells; below the texture's texel density sub-faces stop gaining visual detail. Grids keep the resolution they were created with, so changing this mid-session is safe.")]
        [Range(2, 16)]
        [SerializeField] private int subVoxelResolution = 8;

        [Header("Generation")]
        [SerializeField] private IslandWorldGenerator islandGenerator = new IslandWorldGenerator();

        [Tooltip("Use the Phase 6 flat debug island instead of procedural generation (seam/meshing testing).")]
        [SerializeField] private bool useDebugFlatGenerator;

        [UnityEngine.Serialization.FormerlySerializedAs("generator")]
        [SerializeField] private DebugFlatIslandGenerator debugGenerator = new DebugFlatIslandGenerator();

        private IChunkGenerator activeGenerator;

        private readonly Dictionary<Vector2Int, Chunk> chunks = new Dictionary<Vector2Int, Chunk>();
        private readonly Dictionary<Vector2Int, ChunkView> views = new Dictionary<Vector2Int, ChunkView>();
        private readonly Stack<ChunkView> viewPool = new Stack<ChunkView>();
        private readonly List<Vector2Int> releaseBuffer = new List<Vector2Int>();
        private readonly ChunkMesher mesher = new ChunkMesher();

        private List<Vector2Int> sortedOffsets;
        private SupportCollapseSystem supportCollapse;
        private BlockPalette palette;
        private BlockTextureAtlas atlas;
        private Material opaqueMaterial;
        private Material cutoutMaterial;
        private bool initialized;

        /// <summary>Block-ID mapping for this session (generators, interaction, future save code).</summary>
        public BlockPalette Palette => palette;

        /// <summary>Sub-cells per block axis used for NEW promotions (existing grids keep theirs).</summary>
        public int SubVoxelResolution => subVoxelResolution;

        /// <summary>True once Start resolved the databases and generator — consumers (structure placement) wait on this.</summary>
        public bool IsReady => initialized;

        /// <summary>The procedural generator when active; null in debug-flat mode (structures don't place there).</summary>
        public IslandWorldGenerator ActiveIslandGenerator =>
            initialized && !useDebugFlatGenerator ? islandGenerator : null;

        // ------------------------------------------------------------------
        // Save/load (delta model — see SaveManager for the format rationale)
        // ------------------------------------------------------------------

        // Loaded deltas waiting for their chunk to stream in. Applied right
        // after baseline generation, so loading never blocks on the world.
        private readonly Dictionary<Vector2Int, ChunkDelta> pendingDeltas = new Dictionary<Vector2Int, ChunkDelta>();

        /// <summary>Every chunk with data this session (save iterates these for IsModified).</summary>
        public IEnumerable<Chunk> LoadedChunks => chunks.Values;

        /// <summary>
        /// Regenerates a chunk's pristine baseline for delta diffing at save
        /// time. A fresh Chunk, NOT registered anywhere — compare and discard.
        /// </summary>
        public Chunk GenerateBaselineChunk(Vector2Int coord)
        {
            var baseline = new Chunk(coord);
            activeGenerator.Generate(baseline);
            return baseline;
        }

        /// <summary>Load-time seed override — call in the scene-loaded callback, before Start initializes the generator.</summary>
        public void ApplyLoadedSeed(int seed)
        {
            if (initialized)
            {
                Debug.LogError("VoxelWorld: ApplyLoadedSeed called after initialization — the seed cannot change mid-session.", this);
                return;
            }

            islandGenerator.SetSeed(seed);
        }

        /// <summary>Queues one chunk's saved delta; it applies the moment that chunk's baseline generates (chunk-streamed loading).</summary>
        public void RegisterPendingChunkDelta(Vector2Int coord, ChunkDelta delta)
        {
            if (delta != null)
                pendingDeltas[coord] = delta;
        }

        /// <summary>
        /// Replays a saved delta onto a freshly generated chunk: block edits
        /// (string ids resolved against the live palette — unknown ids skip
        /// with one warning instead of corrupting the chunk), then sub-voxel
        /// promotions. Set/Promote run AFTER MarkGenerated, so the chunk is
        /// IsModified again and the next save re-persists it.
        /// </summary>
        private void ApplyPendingDelta(Chunk chunk, ChunkDelta delta)
        {
            // Resolve the per-delta string table once.
            var resolvedIds = new ushort[delta.BlockIdTable.Length];
            for (int i = 0; i < delta.BlockIdTable.Length; i++)
            {
                string blockId = delta.BlockIdTable[i];
                if (string.IsNullOrEmpty(blockId))
                {
                    resolvedIds[i] = BlockPalette.AirId;
                }
                else if (!palette.TryGetIdByStringId(blockId, out resolvedIds[i]))
                {
                    resolvedIds[i] = BlockPalette.AirId;
                    Debug.LogWarning(
                        $"VoxelWorld: saved block id '{blockId}' no longer exists — those cells load as air.", this);
                }
            }

            for (int i = 0; i < delta.CellIndices.Length; i++)
            {
                Chunk.UnflattenIndex(delta.CellIndices[i], out int x, out int y, out int z);
                chunk.Set(x, y, z, resolvedIds[delta.BlockIdTableIndices[i]]);
            }

            for (int i = 0; i < delta.Promotions.Count; i++)
            {
                PromotedCellDelta promotion = delta.Promotions[i];
                Chunk.UnflattenIndex(promotion.CellIndex, out int x, out int y, out int z);
                if (chunk.Get(x, y, z) == BlockPalette.AirId)
                    continue; // block edit above already emptied it (or bad data)

                SubVoxelGrid grid = chunk.PromoteToSubVoxels(x, y, z, promotion.Resolution);
                if (!grid.ImportBits(promotion.Bits))
                    Debug.LogWarning($"VoxelWorld: promoted-cell payload mismatch at {chunk.Coord} index {promotion.CellIndex} — cell stays intact.", this);
            }

            chunk.MarkSubVoxelsModified();
        }

        private void Start()
        {
            BlockDatabase database = BlockDatabase.Instance;
            if (database == null)
            {
                enabled = false; // Instance already logged the fix
                return;
            }

            if (player == null)
            {
                var references = FindFirstObjectByType<PlayerReferences>();
                if (references != null)
                    player = references.transform;
            }

            if (player == null)
            {
                Debug.LogError("VoxelWorld: no player found to stream around.", this);
                enabled = false;
                return;
            }

            palette = BlockPalette.Build(database.All);
            atlas = BlockTextureAtlas.Build(database.All);
            CreateMaterials();
            activeGenerator = useDebugFlatGenerator ? (IChunkGenerator)debugGenerator : islandGenerator;
            // Generation-time surface shaping must promote at the same
            // resolution damage does — one grid kind, one meshing path.
            islandGenerator.ConfigureSurfaceDetail(subVoxelResolution);
            activeGenerator.Initialize(palette);
            sortedOffsets = BuildSortedOffsets(renderDistance + 2);
            supportCollapse = new SupportCollapseSystem(this);
            initialized = true;
        }

        private void OnDestroy()
        {
            atlas?.Release();
            if (opaqueMaterial != null)
                Destroy(opaqueMaterial);
            if (cutoutMaterial != null)
                Destroy(cutoutMaterial);
        }

        private void Update()
        {
            if (!initialized)
                return;

            Vector2Int center = WorldToChunkCoord(player.position);
            GenerateChunkData(center);
            BuildChunkMeshes(center);
            ReleaseFarViews(center);
            supportCollapse.Tick();
        }

        // ------------------------------------------------------------------
        // Block API (requirement 4)
        // ------------------------------------------------------------------

        /// <summary>Palette ID at a world position. Air for out-of-bounds or unloaded chunks.</summary>
        public ushort GetBlockId(int worldX, int worldY, int worldZ)
        {
            if (worldY < 0 || worldY >= Chunk.SizeY)
                return BlockPalette.AirId;

            var coord = new Vector2Int(worldX >> Chunk.Shift, worldZ >> Chunk.Shift);
            return chunks.TryGetValue(coord, out Chunk chunk)
                ? chunk.Get(worldX & Chunk.Mask, worldY, worldZ & Chunk.Mask)
                : BlockPalette.AirId;
        }

        public ushort GetBlockId(Vector3Int worldPos)
        {
            return GetBlockId(worldPos.x, worldPos.y, worldPos.z);
        }

        /// <summary>Full definition at a world position; null for air/unloaded.</summary>
        public BlockDefinition GetBlock(Vector3Int worldPos)
        {
            return palette.GetDefinition(GetBlockId(worldPos));
        }

        /// <summary>
        /// Writes one block and synchronously remeshes the affected chunk —
        /// plus the touching neighbor when the block lies on a chunk border,
        /// so the neighbor's now-hidden/now-exposed faces update in the same
        /// frame. False when the position is out of bounds or not loaded.
        /// </summary>
        public bool SetBlock(Vector3Int worldPos, ushort blockId)
        {
            if (worldPos.y < 0 || worldPos.y >= Chunk.SizeY)
                return false;

            var coord = new Vector2Int(worldPos.x >> Chunk.Shift, worldPos.z >> Chunk.Shift);
            if (!chunks.TryGetValue(coord, out Chunk chunk))
                return false;

            int localX = worldPos.x & Chunk.Mask;
            int localZ = worldPos.z & Chunk.Mask;
            if (chunk.Get(localX, worldPos.y, localZ) == blockId)
                return true;

            chunk.Set(localX, worldPos.y, localZ, blockId);
            RemeshBlockAndBorders(coord, localX, localZ);

            // Removal may strand a NeedsSupport region (tree crowns).
            if (blockId == BlockPalette.AirId)
                supportCollapse?.NotifyBlockRemoved(worldPos);

            return true;
        }

        public bool SetBlock(Vector3Int worldPos, BlockDefinition block)
        {
            return SetBlock(worldPos, block == null ? BlockPalette.AirId : palette.GetId(block));
        }

        /// <summary>Edited chunk plus the touching neighbor when the cell sits on a border — faces across the seam must update the same frame.</summary>
        private void RemeshBlockAndBorders(Vector2Int coord, int localX, int localZ)
        {
            RebuildIfMeshed(coord);
            if (localX == 0)
                RebuildIfMeshed(coord + Vector2Int.left);
            else if (localX == Chunk.SizeX - 1)
                RebuildIfMeshed(coord + Vector2Int.right);
            if (localZ == 0)
                RebuildIfMeshed(coord + Vector2Int.down);
            else if (localZ == Chunk.SizeZ - 1)
                RebuildIfMeshed(coord + Vector2Int.up);
        }

        // ------------------------------------------------------------------
        // Sub-voxel detail (small-voxel phase)
        // ------------------------------------------------------------------

        /// <summary>Promoted grid at a world cell; null when not promoted, air, or unloaded.</summary>
        public SubVoxelGrid GetSubVoxelGrid(int worldX, int worldY, int worldZ)
        {
            if (worldY < 0 || worldY >= Chunk.SizeY)
                return null;

            var coord = new Vector2Int(worldX >> Chunk.Shift, worldZ >> Chunk.Shift);
            if (!chunks.TryGetValue(coord, out Chunk chunk) || chunk.PromotedCount == 0)
                return null;

            chunk.TryGetSubVoxels(worldX & Chunk.Mask, worldY, worldZ & Chunk.Mask, out SubVoxelGrid grid);
            return grid;
        }

        public SubVoxelGrid GetSubVoxelGrid(Vector3Int worldPos)
        {
            return GetSubVoxelGrid(worldPos.x, worldPos.y, worldPos.z);
        }

        /// <summary>
        /// Lazy-subdivision entry point (damage systems call this before
        /// carving): converts the non-air cell into a fully-filled
        /// SubVoxelGrid at the configured resolution — visually a no-op, so
        /// no remesh happens here — or returns the existing grid. Null when
        /// the cell is air, out of bounds, or unloaded.
        /// </summary>
        public SubVoxelGrid PromoteBlockToSubVoxels(Vector3Int worldPos)
        {
            if (worldPos.y < 0 || worldPos.y >= Chunk.SizeY)
                return null;

            var coord = new Vector2Int(worldPos.x >> Chunk.Shift, worldPos.z >> Chunk.Shift);
            if (!chunks.TryGetValue(coord, out Chunk chunk))
                return null;

            int localX = worldPos.x & Chunk.Mask;
            int localZ = worldPos.z & Chunk.Mask;
            if (chunk.Get(localX, worldPos.y, localZ) == BlockPalette.AirId)
                return null; // nothing to subdivide

            return chunk.PromoteToSubVoxels(localX, worldPos.y, localZ, subVoxelResolution);
        }

        /// <summary>
        /// Call after mutating a promoted cell's grid (single cells or a whole
        /// carve batch): demotes the CELL TO AIR when every sub-cell is empty
        /// (removing the sparse entry — the block is gone), otherwise
        /// remeshes the chunk and any touching border neighbor. The reverse
        /// (re-compacting an all-full grid back to a plain block) is a
        /// possible future optimization, deliberately not built — refilled
        /// grids are rare and staying promoted is merely a few quads.
        /// </summary>
        public bool NotifySubVoxelsChanged(Vector3Int worldPos)
        {
            if (worldPos.y < 0 || worldPos.y >= Chunk.SizeY)
                return false;

            var coord = new Vector2Int(worldPos.x >> Chunk.Shift, worldPos.z >> Chunk.Shift);
            if (!chunks.TryGetValue(coord, out Chunk chunk))
                return false;

            int localX = worldPos.x & Chunk.Mask;
            int localZ = worldPos.z & Chunk.Mask;
            if (!chunk.TryGetSubVoxels(localX, worldPos.y, localZ, out SubVoxelGrid grid))
                return false;

            if (grid.IsEmpty)
                return SetBlock(worldPos, BlockPalette.AirId); // demotion: Set drops the entry, borders remesh

            chunk.MarkSubVoxelsModified();
            RemeshBlockAndBorders(coord, localX, localZ);
            return true;
        }

        /// <summary>Single sub-cell convenience over grid.Set + NotifySubVoxelsChanged. The cell must already be promoted.</summary>
        public bool SetSubVoxel(Vector3Int worldPos, Vector3Int subCell, bool filled)
        {
            SubVoxelGrid grid = GetSubVoxelGrid(worldPos);
            if (grid == null)
                return false;

            if (!grid.Set(subCell.x, subCell.y, subCell.z, filled))
                return true; // no change, nothing to remesh

            return NotifySubVoxelsChanged(worldPos);
        }

        /// <summary>One block cell fully emptied by a carve — the mining code grants its drops.</summary>
        public readonly struct CarvedBlock
        {
            public readonly Vector3Int Cell;
            public readonly BlockDefinition Definition;

            public CarvedBlock(Vector3Int cell, BlockDefinition definition)
            {
                Cell = cell;
                Definition = definition;
            }
        }

        private readonly HashSet<Vector2Int> carveDirtyChunks = new HashSet<Vector2Int>();
        private readonly List<Vector3Int> carveRemovedCells = new List<Vector3Int>(8);

        /// <summary>
        /// Radius damage (the mining phase's entry point): promotes every
        /// block cell the sphere overlaps (lazy subdivision) and clears every
        /// sub-cell whose center lies inside the sphere. canCarve filters
        /// cells by definition — tool permission lives with the CALLER's
        /// rules, this method only edits geometry. Cells whose grids empty
        /// completely demote to air and are reported via fullyEmptied (with
        /// their pre-demotion definition, for drops).
        ///
        /// Efficiency: all touched chunks (plus border neighbors of touched
        /// cells) collect into a set and remesh EXACTLY ONCE at the end —
        /// same principle as single-block edits, batched.
        /// </summary>
        public int CarveSphere(
            Vector3 worldCenter, float radius,
            System.Predicate<BlockDefinition> canCarve, List<CarvedBlock> fullyEmptied)
        {
            if (radius <= 0f)
                return 0;

            carveDirtyChunks.Clear();
            carveRemovedCells.Clear();
            float radiusSqr = radius * radius;
            int clearedTotal = 0;

            int minX = Mathf.FloorToInt(worldCenter.x - radius);
            int maxX = Mathf.FloorToInt(worldCenter.x + radius);
            int minY = Mathf.Max(0, Mathf.FloorToInt(worldCenter.y - radius));
            int maxY = Mathf.Min(Chunk.SizeY - 1, Mathf.FloorToInt(worldCenter.y + radius));
            int minZ = Mathf.FloorToInt(worldCenter.z - radius);
            int maxZ = Mathf.FloorToInt(worldCenter.z + radius);

            for (int cy = minY; cy <= maxY; cy++)
            {
                for (int cz = minZ; cz <= maxZ; cz++)
                {
                    for (int cx = minX; cx <= maxX; cx++)
                    {
                        var coord = new Vector2Int(cx >> Chunk.Shift, cz >> Chunk.Shift);
                        if (!chunks.TryGetValue(coord, out Chunk chunk))
                            continue;

                        int localX = cx & Chunk.Mask;
                        int localZ = cz & Chunk.Mask;
                        ushort id = chunk.Get(localX, cy, localZ);
                        if (id == BlockPalette.AirId)
                            continue;

                        BlockDefinition definition = palette.GetDefinition(id);
                        if (canCarve != null && !canCarve(definition))
                            continue;

                        // Sphere vs cell AABB quick reject.
                        float dx = worldCenter.x - Mathf.Clamp(worldCenter.x, cx, cx + 1f);
                        float dy = worldCenter.y - Mathf.Clamp(worldCenter.y, cy, cy + 1f);
                        float dz = worldCenter.z - Mathf.Clamp(worldCenter.z, cz, cz + 1f);
                        if (dx * dx + dy * dy + dz * dz > radiusSqr)
                            continue;

                        clearedTotal += CarveCell(
                            chunk, coord, localX, cy, localZ,
                            new Vector3Int(cx, cy, cz), definition, worldCenter, radiusSqr, fullyEmptied);
                    }
                }
            }

            foreach (Vector2Int coord in carveDirtyChunks)
                RebuildIfMeshed(coord);

            // Demoted cells may have stranded NeedsSupport regions above.
            for (int i = 0; i < carveRemovedCells.Count; i++)
                supportCollapse?.NotifyBlockRemoved(carveRemovedCells[i]);

            return clearedTotal;
        }

        private int CarveCell(
            Chunk chunk, Vector2Int coord, int localX, int cy, int localZ,
            Vector3Int cell, BlockDefinition definition,
            Vector3 center, float radiusSqr, List<CarvedBlock> fullyEmptied)
        {
            // Resolution for the containment test: the existing grid's, or the
            // configured one for a fresh promotion.
            int res = chunk.TryGetSubVoxels(localX, cy, localZ, out SubVoxelGrid grid)
                ? grid.Resolution
                : subVoxelResolution;
            float subSize = 1f / res;

            // Pass 1 — does the sphere actually contain any sub-cell CENTER?
            // The AABB overlap alone would promote grazed-but-untouched cells
            // and litter them with pointless full grids.
            bool anyInside = false;
            for (int sy = 0; sy < res && !anyInside; sy++)
            {
                for (int sz = 0; sz < res && !anyInside; sz++)
                {
                    for (int sx = 0; sx < res && !anyInside; sx++)
                        anyInside = SubCellInSphere(cell, sx, sy, sz, subSize, center, radiusSqr);
                }
            }

            if (!anyInside)
                return 0;

            grid ??= chunk.PromoteToSubVoxels(localX, cy, localZ, subVoxelResolution);

            // Pass 2 — clear.
            int cleared = 0;
            for (int sy = 0; sy < res; sy++)
            {
                for (int sz = 0; sz < res; sz++)
                {
                    for (int sx = 0; sx < res; sx++)
                    {
                        if (SubCellInSphere(cell, sx, sy, sz, subSize, center, radiusSqr)
                            && grid.Set(sx, sy, sz, false))
                            cleared++;
                    }
                }
            }

            if (cleared == 0)
                return 0;

            chunk.MarkSubVoxelsModified();

            if (grid.IsEmpty)
            {
                // Fully mined: report for drops, demote to air (drops the
                // sparse entry). Remeshing is the batch's job, not Set's.
                fullyEmptied?.Add(new CarvedBlock(cell, definition));
                chunk.Set(localX, cy, localZ, BlockPalette.AirId);
                carveRemovedCells.Add(cell);
            }

            carveDirtyChunks.Add(coord);
            if (localX == 0)
                carveDirtyChunks.Add(coord + Vector2Int.left);
            else if (localX == Chunk.SizeX - 1)
                carveDirtyChunks.Add(coord + Vector2Int.right);
            if (localZ == 0)
                carveDirtyChunks.Add(coord + Vector2Int.down);
            else if (localZ == Chunk.SizeZ - 1)
                carveDirtyChunks.Add(coord + Vector2Int.up);

            return cleared;
        }

        private static bool SubCellInSphere(
            Vector3Int cell, int sx, int sy, int sz, float subSize, Vector3 center, float radiusSqr)
        {
            float px = cell.x + (sx + 0.5f) * subSize;
            float py = cell.y + (sy + 0.5f) * subSize;
            float pz = cell.z + (sz + 0.5f) * subSize;
            float dx = px - center.x;
            float dy = py - center.y;
            float dz = pz - center.z;
            return dx * dx + dy * dy + dz * dz < radiusSqr;
        }

        /// <summary>
        /// Batched removal used by SupportCollapseSystem's "convert to debris"
        /// rule: every listed cell goes to air, spawns its block's DropItem as
        /// a WorldItem plus chip particles (bursts capped so a whole crown
        /// doesn't spike the particle system), and each touched chunk remeshes
        /// exactly once. Deliberately does NOT re-notify support checks — a
        /// collapsed region is by definition the complete connected flagged
        /// component, so nothing flagged can be left hanging off it.
        /// </summary>
        public void RemoveCellsAsDebris(List<Vector3Int> cells)
        {
            const int MaxDebrisBursts = 16;

            carveDirtyChunks.Clear();
            MiningDebrisEffect debris = MiningDebrisEffect.GetOrCreate();
            int bursts = 0;

            for (int i = 0; i < cells.Count; i++)
            {
                Vector3Int cell = cells[i];
                if (cell.y < 0 || cell.y >= Chunk.SizeY)
                    continue;

                var coord = new Vector2Int(cell.x >> Chunk.Shift, cell.z >> Chunk.Shift);
                if (!chunks.TryGetValue(coord, out Chunk chunk))
                    continue;

                int localX = cell.x & Chunk.Mask;
                int localZ = cell.z & Chunk.Mask;
                ushort id = chunk.Get(localX, cell.y, localZ);
                if (id == BlockPalette.AirId)
                    continue;

                BlockDefinition definition = palette.GetDefinition(id);
                chunk.Set(localX, cell.y, localZ, BlockPalette.AirId);

                carveDirtyChunks.Add(coord);
                if (localX == 0)
                    carveDirtyChunks.Add(coord + Vector2Int.left);
                else if (localX == Chunk.SizeX - 1)
                    carveDirtyChunks.Add(coord + Vector2Int.right);
                if (localZ == 0)
                    carveDirtyChunks.Add(coord + Vector2Int.down);
                else if (localZ == Chunk.SizeZ - 1)
                    carveDirtyChunks.Add(coord + Vector2Int.up);

                Vector3 center = cell + new Vector3(0.5f, 0.5f, 0.5f);

                if (definition != null && definition.DropItem != null)
                {
                    int count = Random.Range(definition.DropCountMin, definition.DropCountMax + 1);
                    if (count > 0)
                        WorldItem.Spawn(definition.DropItem, count, 1f, center, Vector3.up * 1.5f);
                }

                if (debris != null && bursts < MaxDebrisBursts)
                {
                    debris.EmitBurst(center, definition, 6);
                    bursts++;
                }
            }

            foreach (Vector2Int coord in carveDirtyChunks)
                RebuildIfMeshed(coord);
        }
        /// <summary>
        /// True when this chunk or any horizontal neighbor holds promoted
        /// cells — the mesher checks once per rebuild and keeps the pristine
        /// hot path completely lookup-free when false (the common case).
        /// </summary>
        public bool HasPromotionsNear(Vector2Int coord)
        {
            return PromotedAt(coord)
                   || PromotedAt(coord + Vector2Int.left) || PromotedAt(coord + Vector2Int.right)
                   || PromotedAt(coord + Vector2Int.up) || PromotedAt(coord + Vector2Int.down);
        }

        private bool PromotedAt(Vector2Int coord)
        {
            return chunks.TryGetValue(coord, out Chunk chunk) && chunk.PromotedCount > 0;
        }

        public static Vector2Int WorldToChunkCoord(Vector3 worldPosition)
        {
            // Arithmetic shift handles negative coordinates correctly for the
            // power-of-two chunk size (floor division, not truncation).
            return new Vector2Int(
                Mathf.FloorToInt(worldPosition.x) >> Chunk.Shift,
                Mathf.FloorToInt(worldPosition.z) >> Chunk.Shift);
        }

        // ------------------------------------------------------------------
        // Streaming
        // ------------------------------------------------------------------

        private void GenerateChunkData(Vector2Int center)
        {
            int dataDistance = renderDistance + 1;
            int budget = dataBudgetPerFrame;

            foreach (Vector2Int offset in sortedOffsets)
            {
                if (Chebyshev(offset) > dataDistance)
                    continue;

                Vector2Int coord = center + offset;
                if (chunks.ContainsKey(coord))
                    continue;

                // Only genuinely NEW chunks generate; anything already in the
                // dictionary (including everything the player edited) is left
                // untouched — IsGenerated/IsModified flag exactly that for the
                // save phase.
                var chunk = new Chunk(coord);
                activeGenerator.Generate(chunk);
                chunk.MarkGenerated();
                chunks.Add(coord, chunk);

                // Loaded-game deltas replay here, per chunk as it streams in.
                if (pendingDeltas.Count > 0 && pendingDeltas.TryGetValue(coord, out ChunkDelta delta))
                {
                    ApplyPendingDelta(chunk, delta);
                    pendingDeltas.Remove(coord);
                }

                if (--budget <= 0)
                    return;
            }
        }

        private void BuildChunkMeshes(Vector2Int center)
        {
            int budget = meshBudgetPerFrame;

            foreach (Vector2Int offset in sortedOffsets)
            {
                if (Chebyshev(offset) > renderDistance)
                    continue;

                Vector2Int coord = center + offset;
                if (views.ContainsKey(coord) || !chunks.ContainsKey(coord) || !NeighborsHaveData(coord))
                    continue;

                RebuildChunkMesh(coord);
                if (--budget <= 0)
                    return;
            }
        }

        private void ReleaseFarViews(Vector2Int center)
        {
            releaseBuffer.Clear();
            foreach (KeyValuePair<Vector2Int, ChunkView> entry in views)
            {
                if (Chebyshev(entry.Key - center) > renderDistance + 1)
                    releaseBuffer.Add(entry.Key);
            }

            foreach (Vector2Int coord in releaseBuffer)
            {
                ChunkView view = views[coord];
                views.Remove(coord);
                view.Release();
                viewPool.Push(view);
                // Chunk DATA stays in `chunks` on purpose: edits persist for
                // the session and re-entering the area only re-meshes.
            }
        }

        private void RebuildChunkMesh(Vector2Int coord)
        {
            Chunk chunk = chunks[coord];

            if (!views.TryGetValue(coord, out ChunkView view))
            {
                view = viewPool.Count > 0 ? viewPool.Pop() : ChunkView.Create(transform, opaqueMaterial, cutoutMaterial);
                views.Add(coord, view);
                view.Bind(coord);
            }

            mesher.Build(chunk, this, palette, atlas, view.RenderMesh, view.CollisionMesh);
            view.RefreshAfterBuild(mesher.HasCollision);
        }

        private void RebuildIfMeshed(Vector2Int coord)
        {
            if (views.ContainsKey(coord) && chunks.ContainsKey(coord))
                RebuildChunkMesh(coord);
        }

        private bool NeighborsHaveData(Vector2Int coord)
        {
            return chunks.ContainsKey(coord + Vector2Int.left)
                   && chunks.ContainsKey(coord + Vector2Int.right)
                   && chunks.ContainsKey(coord + Vector2Int.up)
                   && chunks.ContainsKey(coord + Vector2Int.down);
        }

        private static int Chebyshev(Vector2Int offset)
        {
            return Mathf.Max(Mathf.Abs(offset.x), Mathf.Abs(offset.y));
        }

        private static List<Vector2Int> BuildSortedOffsets(int radius)
        {
            var offsets = new List<Vector2Int>((radius * 2 + 1) * (radius * 2 + 1));
            for (int x = -radius; x <= radius; x++)
            {
                for (int z = -radius; z <= radius; z++)
                    offsets.Add(new Vector2Int(x, z));
            }

            // Nearest-first: the ground under the player exists before the horizon.
            offsets.Sort((a, b) => (a.x * a.x + a.y * a.y).CompareTo(b.x * b.x + b.y * b.y));
            return offsets;
        }

        // ------------------------------------------------------------------
        // Materials
        // ------------------------------------------------------------------

        private void CreateMaterials()
        {
            Shader lit = GraphicsSettings.currentRenderPipeline != null
                ? GraphicsSettings.currentRenderPipeline.defaultShader
                : Shader.Find("Standard");

            opaqueMaterial = new Material(lit) { name = "TerrainOpaque", mainTexture = atlas.Texture };
            SetMatte(opaqueMaterial);

            // Since Phase 7 the transparent submesh carries WATER, so it alpha-
            // BLENDS (URP Lit transparent surface) instead of alpha-clipping:
            // the ocean reads as translucent from above. If the runtime keyword
            // recipe ever breaks on a URP upgrade, blocks fall back to opaque
            // rendering — ugly but functional, and loudly obvious.
            cutoutMaterial = new Material(lit) { name = "TerrainTransparent", mainTexture = atlas.Texture };
            SetMatte(cutoutMaterial);
            if (cutoutMaterial.HasProperty("_Surface"))
            {
                cutoutMaterial.SetFloat("_Surface", 1f); // 1 = Transparent
                cutoutMaterial.SetOverrideTag("RenderType", "Transparent");
                cutoutMaterial.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                cutoutMaterial.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
                cutoutMaterial.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
                cutoutMaterial.SetFloat("_ZWrite", 0f);
                cutoutMaterial.renderQueue = (int)RenderQueue.Transparent;
            }
        }

        private static void SetMatte(Material material)
        {
            if (material.HasProperty("_Smoothness"))
                material.SetFloat("_Smoothness", 0f);
            if (material.HasProperty("_Glossiness"))
                material.SetFloat("_Glossiness", 0f);
        }
    }
}
