using IslandGame.Data.Blocks;
using IslandGame.Terrain;
using UnityEngine;

namespace IslandGame.Creatures
{
    /// <summary>
    /// The creature systems' walkability oracle over the voxel world — THE
    /// pathing decision of the creatures phase, chosen over NavMeshSurface
    /// runtime rebaking:
    ///
    ///   1. NavMesh sources would be the chunk COLLISION meshes, which exist
    ///      only inside the pooled render ring and vanish behind the player;
    ///      chunk DATA persists for everything ever loaded, so sampling it
    ///      navigates exactly where creatures actually simulate.
    ///   2. Radius mining completes a bite roughly every second; NavMesh has
    ///      no incremental walkable-topology update — even an "async local"
    ///      rebake re-collects sources and rebuilds whole tiles (tens of ms,
    ///      always lagging the carve). The voxel grid needs NO rebake: a
    ///      terrain edit is visible to the very next query.
    ///   3. The world is block-quantized with 1-block steps — the domain where
    ///      column sampling + step logic IS navigation; NavMesh's win
    ///      (arbitrary mesh topology) buys nothing here.
    ///   4. Cost: one walkability probe = a few dictionary+array block reads.
    ///      Dozens of creatures cost microseconds per frame, with zero bake
    ///      spikes ever.
    ///
    /// Accepted limitation: probe-and-slide steering (CreatureMover) does no
    /// long-range planning, so a creature can dither in a large concave
    /// pocket. The designated upgrade, IF wildlife ever needs it, is A* over
    /// this same voxel data feeding the same mover — no NavMesh retrofit.
    /// </summary>
    public static class VoxelNavigation
    {
        private static VoxelWorld world;

        /// <summary>The scene's voxel world (lazy-cached; refreshes if the object is rebuilt).</summary>
        public static VoxelWorld World
        {
            get
            {
                if (world == null)
                    world = Object.FindFirstObjectByType<VoxelWorld>();
                return world;
            }
        }

        /// <summary>A cell a creature body can occupy: air or any non-solid, non-liquid decoration.</summary>
        public static bool IsPassable(VoxelWorld voxels, int x, int y, int z)
        {
            if (y >= Chunk.SizeY)
                return true;
            if (y < 0)
                return false;

            BlockDefinition block = voxels.GetBlock(new Vector3Int(x, y, z));
            return block == null || (!block.IsSolid && !block.HasBehavior(BlockBehaviorFlags.Liquid));
        }

        /// <summary>
        /// Finds the walkable ground surface in the column at `position`:
        /// scans from scanUp blocks above down to scanDown below for the first
        /// non-passable cell. Solid ground needs 2 cells of body clearance
        /// above; water reports found-with-onWater=true so callers can treat
        /// it as blocked (land creatures) without a second scan. False when
        /// the column is all air, clearance fails (overhang cave ceiling), or
        /// the chunk has no data yet (unloaded — callers must not move there).
        /// </summary>
        public static bool TryGetGroundHeight(
            Vector3 position, int scanUp, int scanDown, out float groundY, out bool onWater)
        {
            groundY = 0f;
            onWater = false;

            VoxelWorld voxels = World;
            if (voxels == null)
                return false;

            int x = Mathf.FloorToInt(position.x);
            int z = Mathf.FloorToInt(position.z);
            int startY = Mathf.Clamp(Mathf.FloorToInt(position.y) + scanUp, 0, Chunk.SizeY - 1);
            int endY = Mathf.Max(0, Mathf.FloorToInt(position.y) - scanDown);

            for (int y = startY; y >= endY; y--)
            {
                BlockDefinition block = voxels.GetBlock(new Vector3Int(x, y, z));
                if (block == null)
                    continue; // air (or unloaded — indistinguishable, and both mean "keep scanning")

                if (block.HasBehavior(BlockBehaviorFlags.Liquid))
                {
                    groundY = y + 1f;
                    onWater = true;
                    return true;
                }

                if (!block.IsSolid)
                    continue; // decoration — fall through it

                if (!IsPassable(voxels, x, y + 1, z) || !IsPassable(voxels, x, y + 2, z))
                    return false; // ground exists but a body doesn't fit on it

                groundY = y + 1f;
                return true;
            }

            return false;
        }

        /// <summary>
        /// Can a creature standing at `from` take a step to `to`? True with
        /// the step's ground height when the destination has standable ground
        /// that is not water, no higher than stepHeight above the feet and no
        /// further than maxDropHeight below them.
        /// </summary>
        public static bool IsStepWalkable(
            Vector3 from, Vector3 to, float stepHeight, float maxDropHeight, out float stepGroundY)
        {
            // Scan from slightly above the feet so a 1-block rise is seen.
            var probe = new Vector3(to.x, from.y, to.z);
            if (!TryGetGroundHeight(probe, 2, Mathf.CeilToInt(maxDropHeight) + 1, out stepGroundY, out bool onWater))
                return false;

            if (onWater)
                return false;

            float rise = stepGroundY - from.y;
            return rise <= stepHeight && rise >= -maxDropHeight;
        }
    }
}
