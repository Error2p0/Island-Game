using System.Collections.Generic;
using UnityEngine;

namespace IslandGame.Terrain
{
    /// <summary>
    /// Voxel data for one 16×64×16 world column — pure data, no Unity objects.
    ///
    /// CHUNK SIZE: 16×16 horizontal keeps per-edit remesh cost small and makes
    /// chunk coordinates cheap bit math (power of two). 64 world height in ONE
    /// vertical chunk fits this game — islands rise a few dozen blocks over
    /// sea level and builds stay modest — and it eliminates vertical chunk
    /// neighbors entirely: streaming is a 2D grid, seams exist on 4 sides only.
    ///
    /// STORAGE: flattened 1D ushort[] with index math instead of a 3D array —
    /// one contiguous 32 KB allocation, no per-row pointer chasing, cache-
    /// friendly iteration in the mesher's hot loop, and trivially blittable
    /// for the future save phase. Values are BlockPalette numeric IDs.
    ///
    /// SUB-VOXEL DETAIL (small-voxel phase): damaged cells are "promoted" —
    /// they KEEP their base palette ID (so neighbor culling, liquids and
    /// gameplay reads are untouched) and additionally get a SubVoxelGrid entry
    /// in a SPARSE dictionary keyed by the same flattened index the block
    /// array uses (already computed, no Vector3Int hashing, 4-byte keys).
    /// The dictionary is allocated lazily: a pristine chunk carries literally
    /// nothing extra, and PromotedCount == 0 lets the mesher skip every
    /// per-cell lookup. A dictionary (vs a parallel array) because promotions
    /// are a tiny fraction of 16 384 cells — O(1) lookup, ~150 B per damaged
    /// cell, nothing for the rest.
    /// </summary>
    public sealed class Chunk
    {
        public const int SizeX = 16;
        public const int SizeY = 64;
        public const int SizeZ = 16;

        /// <summary>log2(SizeX/SizeZ) — world→chunk coordinate shift (VoxelWorld relies on this).</summary>
        public const int Shift = 4;

        public const int Mask = 15;

        private readonly ushort[] blocks = new ushort[SizeX * SizeY * SizeZ];

        // Sparse sub-voxel detail — null until the first promotion (see class summary).
        private Dictionary<int, SubVoxelGrid> subVoxels;

        /// <summary>Chunk grid coordinate (x = world X / 16, y = world Z / 16).</summary>
        public Vector2Int Coord { get; }

        /// <summary>Non-air voxel count — lets the mesher and streamer skip empty ocean chunks outright.</summary>
        public int NonAirCount { get; private set; }

        /// <summary>True once the world generator has filled this chunk; Set calls after that mark it modified.</summary>
        public bool IsGenerated { get; private set; }

        /// <summary>True when edited after generation — the future save phase persists exactly these chunks (block data AND sub-voxel grids).</summary>
        public bool IsModified { get; private set; }

        /// <summary>Number of promoted (sub-voxel) cells. 0 for pristine chunks — the mesher's fast path.</summary>
        public int PromotedCount => subVoxels?.Count ?? 0;

        public Chunk(Vector2Int coord)
        {
            Coord = coord;
        }

        /// <summary>Called by VoxelWorld right after generation, so generator writes don't count as player edits.</summary>
        public void MarkGenerated()
        {
            IsGenerated = true;
        }

        public ushort Get(int x, int y, int z)
        {
            return blocks[Index(x, y, z)];
        }

        public void Set(int x, int y, int z, ushort id)
        {
            int index = Index(x, y, z);
            ushort previous = blocks[index];
            if (previous == id)
                return; // no-op writes also deliberately keep any sub-voxel detail

            if (previous == BlockPalette.AirId)
                NonAirCount++;
            else if (id == BlockPalette.AirId)
                NonAirCount--;

            blocks[index] = id;

            // Replacing a cell wholesale (mined to air, block placed) discards
            // its fine detail — the detail described the PREVIOUS content.
            subVoxels?.Remove(index);

            if (IsGenerated)
                IsModified = true;
        }

        // ------------------------------------------------------------------
        // Sub-voxel detail (small-voxel phase)
        // ------------------------------------------------------------------

        /// <summary>The promoted grid at a cell, when there is one.</summary>
        public bool TryGetSubVoxels(int x, int y, int z, out SubVoxelGrid grid)
        {
            if (subVoxels == null)
            {
                grid = null;
                return false;
            }

            return subVoxels.TryGetValue(Index(x, y, z), out grid);
        }

        /// <summary>
        /// Lazy-subdivision entry point: gives the (non-air) cell a fully
        /// filled grid — visually identical to the intact block — or returns
        /// the existing grid. Callers go through VoxelWorld, which validates
        /// the cell and supplies the configured resolution.
        /// </summary>
        public SubVoxelGrid PromoteToSubVoxels(int x, int y, int z, int resolution)
        {
            subVoxels ??= new Dictionary<int, SubVoxelGrid>();

            int index = Index(x, y, z);
            if (subVoxels.TryGetValue(index, out SubVoxelGrid existing))
                return existing;

            var grid = SubVoxelGrid.CreateFilled(resolution);
            subVoxels.Add(index, grid);

            if (IsGenerated)
                IsModified = true;

            return grid;
        }

        /// <summary>Grid contents were edited in place — keep the save-phase flag honest.</summary>
        public void MarkSubVoxelsModified()
        {
            if (IsGenerated)
                IsModified = true;
        }

        /// <summary>
        /// Radius placement's demotion: a grid refilled to 100% is identical
        /// to the intact block, so the entry drops and the cell costs 2 bytes
        /// again — the mirror of carving's demote-to-air. No-op unless the
        /// grid is actually full.
        /// </summary>
        public void DemoteFullSubVoxels(int x, int y, int z)
        {
            if (subVoxels == null)
                return;

            int index = Index(x, y, z);
            if (!subVoxels.TryGetValue(index, out SubVoxelGrid grid) || !grid.IsFull)
                return;

            subVoxels.Remove(index);
            if (IsGenerated)
                IsModified = true;
        }

        // ------------------------------------------------------------------
        // Save phase
        // ------------------------------------------------------------------

        private static readonly Dictionary<int, SubVoxelGrid> EmptyPromotions = new Dictionary<int, SubVoxelGrid>();

        /// <summary>Every promoted cell as (flattened local index, grid) — the save phase persists these alongside block deltas.</summary>
        public IReadOnlyDictionary<int, SubVoxelGrid> PromotedCells => subVoxels ?? EmptyPromotions;

        /// <summary>The flattened-index math, public for the save phase's delta encoding.</summary>
        public static int FlattenIndex(int x, int y, int z)
        {
            return Index(x, y, z);
        }

        /// <summary>Inverse of FlattenIndex, for applying loaded deltas.</summary>
        public static void UnflattenIndex(int index, out int x, out int y, out int z)
        {
            x = index & (SizeX - 1);
            z = (index / SizeX) & (SizeZ - 1);
            y = index / (SizeX * SizeZ);
        }

        // Y-major layout: one full 16×16 horizontal slice per Y keeps the
        // mesher's x/z inner loops on adjacent memory.
        private static int Index(int x, int y, int z)
        {
            return (y * SizeZ + z) * SizeX + x;
        }
    }
}
