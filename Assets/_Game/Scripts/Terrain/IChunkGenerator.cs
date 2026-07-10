namespace IslandGame.Terrain
{
    /// <summary>
    /// Fills freshly created chunks with block data. Phase 6 ships the flat
    /// debug generator; Phase 7's noise-based island generator implements this
    /// same contract and swaps in without touching VoxelWorld's streaming.
    /// </summary>
    public interface IChunkGenerator
    {
        /// <summary>Resolve configured block string-IDs to palette IDs once, before any Generate call.</summary>
        void Initialize(BlockPalette palette);

        /// <summary>Fill the chunk's voxels. Called exactly once per chunk, on the main thread.</summary>
        void Generate(Chunk chunk);
    }
}
