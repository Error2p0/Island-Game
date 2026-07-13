using System.Collections.Generic;

namespace IslandGame.Terrain
{
    /// <summary>One promoted cell's saved shape: which cell, at what resolution, and its raw bitset.</summary>
    public sealed class PromotedCellDelta
    {
        public int CellIndex;
        public int Resolution;
        public byte[] Bits;
    }

    /// <summary>
    /// The runtime shape of one modified chunk's saved changes, exchanged
    /// between the save system and VoxelWorld: block edits as (flattened
    /// local index → block STRING id) — string ids because palette numeric
    /// ids are session-local and shift when content is added — plus every
    /// promoted cell's bitset. VoxelWorld resolves string ids against the
    /// live palette at APPLY time (load order independent), and applies the
    /// whole delta right after the chunk's baseline generates, so loading
    /// stays chunk-streamed exactly like generation.
    /// </summary>
    public sealed class ChunkDelta
    {
        /// <summary>Flattened local cell indices (Chunk.FlattenIndex order), parallel to BlockIdTableIndices.</summary>
        public int[] CellIndices;

        /// <summary>Per-edit index into BlockIdTable (a per-chunk string table keeps repeated ids cheap).</summary>
        public int[] BlockIdTableIndices;

        /// <summary>The block string ids this delta references ("" = air).</summary>
        public string[] BlockIdTable;

        public List<PromotedCellDelta> Promotions = new List<PromotedCellDelta>();
    }
}
