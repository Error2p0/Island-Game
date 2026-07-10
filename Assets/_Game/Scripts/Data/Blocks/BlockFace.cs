namespace IslandGame.Data.Blocks
{
    /// <summary>
    /// Voxel face convention shared by block texturing (now) and the terrain
    /// mesher (Phase 6): Top = +Y, Bottom = -Y, North = +Z, South = -Z,
    /// East = +X, West = -X. Append only — the numeric values are a stable
    /// contract for face loops and lookups.
    /// </summary>
    public enum BlockFace
    {
        Top = 0,
        Bottom = 1,
        North = 2,
        South = 3,
        East = 4,
        West = 5,
    }

    /// <summary>Allocation-free iteration helpers for the six faces.</summary>
    public static class BlockFaces
    {
        public const int Count = 6;

        /// <summary>All six faces in enum order. Do not mutate.</summary>
        public static readonly BlockFace[] All =
        {
            BlockFace.Top,
            BlockFace.Bottom,
            BlockFace.North,
            BlockFace.South,
            BlockFace.East,
            BlockFace.West,
        };
    }
}
