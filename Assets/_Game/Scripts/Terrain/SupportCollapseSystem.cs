using System.Collections.Generic;
using IslandGame.Data.Blocks;
using UnityEngine;

namespace IslandGame.Terrain
{
    /// <summary>
    /// The generic rule behind falling trees: blocks flagged NeedsSupport
    /// (tree trunks/leaves — pure content, no tree-specific mining code)
    /// survive only while their CONNECTED FLAGGED REGION touches at least one
    /// unflagged solid block. When mining/carving removes a cell, its flagged
    /// neighbors queue a check; an unsupported region is REMOVED AND CONVERTED
    /// TO DEBRIS — every cell spawns its block's DropItem as a WorldItem plus
    /// chip particles. This is the documented severing behavior: sever a
    /// trunk and the crown drops its wood/leaf yield where it stood. A
    /// physics fall of live voxel structures is deliberately NOT built (that
    /// is a fracture sim, out of scope by the project brief).
    ///
    /// Bounds: checks run budgeted per frame (a one-frame reaction delay
    /// reads as the tree "giving way"); the flood fill is capped — regions
    /// larger than the cap are treated as supported, so a pathological
    /// flagged mega-structure can never freeze a frame. Support is
    /// CELL-granular: a promoted cell counts as present while any sub-cell
    /// remains (chop the whole cell to sever, not just a sliver).
    /// </summary>
    public sealed class SupportCollapseSystem
    {
        private const int MaxRegionCells = 1024;
        private const int ChecksPerTick = 2;

        private static readonly Vector3Int[] Neighbors =
        {
            new Vector3Int(0, 1, 0), new Vector3Int(0, -1, 0),
            new Vector3Int(0, 0, 1), new Vector3Int(0, 0, -1),
            new Vector3Int(1, 0, 0), new Vector3Int(-1, 0, 0),
        };

        private readonly VoxelWorld world;
        private readonly Queue<Vector3Int> pendingChecks = new Queue<Vector3Int>();
        private readonly HashSet<Vector3Int> pendingSet = new HashSet<Vector3Int>();

        private readonly HashSet<Vector3Int> visited = new HashSet<Vector3Int>();
        private readonly Queue<Vector3Int> frontier = new Queue<Vector3Int>();
        private readonly List<Vector3Int> region = new List<Vector3Int>(256);

        public SupportCollapseSystem(VoxelWorld world)
        {
            this.world = world;
        }

        /// <summary>A cell went to air: any flagged neighbor might now be unsupported.</summary>
        public void NotifyBlockRemoved(Vector3Int cell)
        {
            for (int i = 0; i < Neighbors.Length; i++)
            {
                Vector3Int neighbor = cell + Neighbors[i];
                BlockDefinition definition = world.GetBlock(neighbor);
                if (definition != null && definition.HasBehavior(BlockBehaviorFlags.NeedsSupport)
                    && pendingSet.Add(neighbor))
                    pendingChecks.Enqueue(neighbor);
            }
        }

        /// <summary>Call once per frame (VoxelWorld.Update).</summary>
        public void Tick()
        {
            int budget = ChecksPerTick;
            while (budget-- > 0 && pendingChecks.Count > 0)
            {
                Vector3Int start = pendingChecks.Dequeue();
                pendingSet.Remove(start);

                BlockDefinition definition = world.GetBlock(start);
                if (definition == null || !definition.HasBehavior(BlockBehaviorFlags.NeedsSupport))
                    continue; // already gone (earlier collapse this frame)

                if (!RegionIsSupported(start))
                    world.RemoveCellsAsDebris(region);
            }
        }

        /// <summary>
        /// Flood-fills the connected flagged region from start (6-connectivity,
        /// capped). True when any region cell touches an unflagged solid block
        /// (terrain, planks, ...) or the region exceeds the cap. On false,
        /// `region` holds every cell to remove.
        /// </summary>
        private bool RegionIsSupported(Vector3Int start)
        {
            visited.Clear();
            frontier.Clear();
            region.Clear();

            visited.Add(start);
            frontier.Enqueue(start);
            region.Add(start);

            while (frontier.Count > 0)
            {
                Vector3Int cell = frontier.Dequeue();

                for (int i = 0; i < Neighbors.Length; i++)
                {
                    Vector3Int neighbor = cell + Neighbors[i];
                    BlockDefinition definition = world.GetBlock(neighbor);
                    if (definition == null)
                        continue; // air/water-void — no support, no expansion

                    if (definition.HasBehavior(BlockBehaviorFlags.NeedsSupport))
                    {
                        if (visited.Add(neighbor))
                        {
                            if (region.Count >= MaxRegionCells)
                                return true; // cap: huge regions count as supported

                            frontier.Enqueue(neighbor);
                            region.Add(neighbor);
                        }
                    }
                    else if (definition.IsSolid)
                    {
                        return true; // grounded on unflagged solid
                    }
                }
            }

            return false;
        }
    }
}
