using System;
using UnityEngine;

namespace IslandGame.Terrain
{
    /// <summary>
    /// Phase 6 debug generator: a flat circular stone island in an empty ocean
    /// of air, purely to prove chunking, meshing, streaming and the block-edit
    /// API. Optional marker posts on chunk corners make seams and chunk
    /// boundaries visually obvious while testing. Replaced by the noise-based
    /// island generator in Phase 7.
    /// </summary>
    [Serializable]
    public sealed class DebugFlatIslandGenerator : IChunkGenerator
    {
        [Tooltip("Top surface of the island sits at this world Y (blocks fill 0..height-1).")]
        [Range(1, Chunk.SizeY - 2)]
        [SerializeField] private int groundHeight = 24;

        [Tooltip("Island radius in blocks around Island Center.")]
        [Min(4f)]
        [SerializeField] private float islandRadius = 48f;

        [SerializeField] private Vector2 islandCenter = Vector2.zero;

        [Tooltip("String ID of the block filling the island.")]
        [SerializeField] private string groundBlockId = "stone";

        [Tooltip("String ID for the chunk-corner marker posts.")]
        [SerializeField] private string markerBlockId = "wood_plank";

        [Tooltip("One marker block on every chunk corner — makes chunk borders (and any seam bugs) visible.")]
        [SerializeField] private bool chunkCornerMarkers = true;

        private ushort groundId;
        private ushort markerId;

        public void Initialize(BlockPalette palette)
        {
            if (!palette.TryGetIdByStringId(groundBlockId, out groundId))
                Debug.LogError($"DebugFlatIslandGenerator: ground block '{groundBlockId}' not in the BlockDatabase — the island will be empty.");
            if (!palette.TryGetIdByStringId(markerBlockId, out markerId))
                markerId = BlockPalette.AirId; // markers are optional decoration
        }

        public void Generate(Chunk chunk)
        {
            if (groundId == BlockPalette.AirId)
                return;

            int originX = chunk.Coord.x * Chunk.SizeX;
            int originZ = chunk.Coord.y * Chunk.SizeZ;
            float radiusSquared = islandRadius * islandRadius;

            for (int z = 0; z < Chunk.SizeZ; z++)
            {
                for (int x = 0; x < Chunk.SizeX; x++)
                {
                    float dx = originX + x + 0.5f - islandCenter.x;
                    float dz = originZ + z + 0.5f - islandCenter.y;
                    if (dx * dx + dz * dz > radiusSquared)
                        continue; // ocean: air for now, water comes with Phase 7

                    for (int y = 0; y < groundHeight; y++)
                        chunk.Set(x, y, z, groundId);

                    if (chunkCornerMarkers && markerId != BlockPalette.AirId && x == 0 && z == 0)
                        chunk.Set(x, groundHeight, z, markerId);
                }
            }
        }
    }
}
