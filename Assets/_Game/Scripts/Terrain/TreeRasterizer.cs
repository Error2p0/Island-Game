using System.Collections.Generic;
using IslandGame.Data.World;
using UnityEngine;

namespace IslandGame.Terrain
{
    /// <summary>
    /// Turns a TreeTemplateDefinition's stroke program into voxels during
    /// chunk generation. Called once per (tree, chunk) pair: a tree near a
    /// border is rasterized again by each neighboring chunk, and every call
    /// writes ONLY the local cells — the standard order-independent answer to
    /// cross-chunk structures, and cheap because strokes are evaluated only
    /// for cells whose bounds actually touch a stroke.
    ///
    /// Per cell inside the template bounds (existing terrain is never
    /// overwritten — first content wins, deterministically):
    ///   • every sub-cell center (at the TEMPLATE's resolution) is tested
    ///     against the strokes — Trunk/Branch beats Leaves when both cover a
    ///     point, and a cell containing ANY wood becomes the trunk block
    ///     (one material per cell; the chopping line stays consistent);
    ///   • all sub-cells filled → a PLAIN BASE BLOCK (2 bytes, fast mesher
    ///     path — canopy interiors and thick trunk cells cost nothing extra);
    ///   • partially filled → the cell is promoted at the template's (low)
    ///     resolution and the outside sub-cells cleared.
    /// From here on the tree IS terrain: mining, radius carving, drops and
    /// support collapse all come from the block definitions.
    /// </summary>
    public static class TreeRasterizer
    {
        private const byte Empty = 0;
        private const byte Wood = 1;
        private const byte Leaf = 2;

        // Scratch buffers — generation is single-threaded (VoxelWorld.Update).
        private static readonly byte[] materialScratch = new byte[8 * 8 * 8];
        private static readonly List<TreeStroke> candidateStrokes = new List<TreeStroke>(16);

        /// <summary>
        /// Rasterizes the template anchored at baseWorldPos (the first air
        /// cell above the surface; the trunk grows from its bottom center),
        /// rotated by yawSteps × 90°, into this chunk's cells only.
        /// </summary>
        public static void Rasterize(
            Chunk chunk, BlockPalette palette, TreeTemplateDefinition template,
            Vector3Int baseWorldPos, int yawSteps)
        {
            ushort trunkId = palette.GetId(template.TrunkBlock);
            if (trunkId == BlockPalette.AirId)
                return; // generator warned at Initialize

            ushort leavesId = palette.GetId(template.LeavesBlock); // may be air → leaf cells skipped

            int originX = chunk.Coord.x * Chunk.SizeX;
            int originZ = chunk.Coord.y * Chunk.SizeZ;

            int extent = Mathf.CeilToInt(template.MaxHorizontalExtent) + 1;
            int height = Mathf.CeilToInt(template.MaxHeight) + 1;

            int minX = Mathf.Max(baseWorldPos.x - extent, originX);
            int maxX = Mathf.Min(baseWorldPos.x + extent, originX + Chunk.SizeX - 1);
            int minZ = Mathf.Max(baseWorldPos.z - extent, originZ);
            int maxZ = Mathf.Min(baseWorldPos.z + extent, originZ + Chunk.SizeZ - 1);
            int minY = Mathf.Max(baseWorldPos.y, 0);
            int maxY = Mathf.Min(baseWorldPos.y + height, Chunk.SizeY - 1);
            if (minX > maxX || minZ > maxZ || minY > maxY)
                return;

            // Trunk base centered in the anchor cell's footprint.
            var anchor = new Vector3(baseWorldPos.x + 0.5f, baseWorldPos.y, baseWorldPos.z + 0.5f);

            IReadOnlyList<TreeStroke> strokes = template.Strokes;
            int res = template.Resolution;
            float subSize = 1f / res;
            int totalSubCells = res * res * res;

            for (int cy = minY; cy <= maxY; cy++)
            {
                for (int cz = minZ; cz <= maxZ; cz++)
                {
                    for (int cx = minX; cx <= maxX; cx++)
                    {
                        int localX = cx - originX;
                        int localZ = cz - originZ;
                        if (chunk.Get(localX, cy, localZ) != BlockPalette.AirId)
                            continue; // terrain (or an earlier tree) wins

                        // Cell center in tree-local space; 90°-step rotation
                        // maps AABBs to AABBs, so the half-extent stays 0.5.
                        Vector3 cellCenterLocal = InverseRotateY(
                            new Vector3(cx + 0.5f, cy + 0.5f, cz + 0.5f) - anchor, yawSteps);

                        if (!CollectCandidateStrokes(strokes, cellCenterLocal))
                            continue;

                        int woodCount = 0;
                        int leafCount = 0;
                        int scratchIndex = 0;
                        for (int sy = 0; sy < res; sy++)
                        {
                            for (int sz = 0; sz < res; sz++)
                            {
                                for (int sx = 0; sx < res; sx++)
                                {
                                    Vector3 point = InverseRotateY(new Vector3(
                                        cx + (sx + 0.5f) * subSize,
                                        cy + (sy + 0.5f) * subSize,
                                        cz + (sz + 0.5f) * subSize) - anchor, yawSteps);

                                    byte material = SampleStrokes(point);
                                    materialScratch[scratchIndex++] = material;
                                    if (material == Wood)
                                        woodCount++;
                                    else if (material == Leaf)
                                        leafCount++;
                                }
                            }
                        }

                        int filled = woodCount + leafCount;
                        if (filled == 0)
                            continue;

                        // One material per cell: any wood ⇒ trunk block, so the
                        // chopping line through branch joints stays wood.
                        ushort cellId = woodCount > 0 ? trunkId : leavesId;
                        if (cellId == BlockPalette.AirId)
                            continue;

                        chunk.Set(localX, cy, localZ, cellId);

                        if (filled < totalSubCells)
                        {
                            SubVoxelGrid grid = chunk.PromoteToSubVoxels(localX, cy, localZ, res);
                            scratchIndex = 0;
                            for (int sy = 0; sy < res; sy++)
                            {
                                for (int sz = 0; sz < res; sz++)
                                {
                                    for (int sx = 0; sx < res; sx++)
                                    {
                                        if (materialScratch[scratchIndex++] == Empty)
                                            grid.Set(sx, sy, sz, false);
                                    }
                                }
                            }
                        }
                    }
                }
            }
        }

        // ------------------------------------------------------------------
        // Stroke evaluation
        // ------------------------------------------------------------------

        /// <summary>Fills candidateStrokes with strokes whose expanded bounds touch the cell around cellCenterLocal. False when none do.</summary>
        private static bool CollectCandidateStrokes(IReadOnlyList<TreeStroke> strokes, Vector3 cellCenterLocal)
        {
            candidateStrokes.Clear();

            for (int i = 0; i < strokes.Count; i++)
            {
                TreeStroke stroke = strokes[i];
                if (stroke == null)
                    continue;

                // Cell half-extent 0.5 (plus a hair) against the stroke's AABB.
                float reach = stroke.Radius + 0.51f;
                Vector3 min = Vector3.Min(stroke.Start, stroke.End);
                Vector3 max = Vector3.Max(stroke.Start, stroke.End);

                if (cellCenterLocal.x >= min.x - reach && cellCenterLocal.x <= max.x + reach
                    && cellCenterLocal.y >= min.y - reach && cellCenterLocal.y <= max.y + reach
                    && cellCenterLocal.z >= min.z - reach && cellCenterLocal.z <= max.z + reach)
                    candidateStrokes.Add(stroke);
            }

            return candidateStrokes.Count > 0;
        }

        private static byte SampleStrokes(Vector3 point)
        {
            byte material = Empty;

            for (int i = 0; i < candidateStrokes.Count; i++)
            {
                TreeStroke stroke = candidateStrokes[i];
                if (DistanceSqToSegment(point, stroke.Start, stroke.End) > stroke.Radius * stroke.Radius)
                    continue;

                if (stroke.Part == TreePart.Leaves)
                {
                    material = Leaf; // keep scanning — wood wins if a branch also covers it
                }
                else
                {
                    return Wood;
                }
            }

            return material;
        }

        private static float DistanceSqToSegment(Vector3 point, Vector3 a, Vector3 b)
        {
            Vector3 ab = b - a;
            float lengthSq = ab.sqrMagnitude;

            float t = lengthSq > 1e-8f ? Mathf.Clamp01(Vector3.Dot(point - a, ab) / lengthSq) : 0f;
            Vector3 closest = a + ab * t;
            return (point - closest).sqrMagnitude;
        }

        /// <summary>Rotates a tree-local point by −(yawSteps × 90°) about +Y — exact integer-friendly swaps, no trig.</summary>
        private static Vector3 InverseRotateY(Vector3 point, int yawSteps)
        {
            for (int i = 0; i < (yawSteps & 3); i++)
                point = new Vector3(point.z, point.y, -point.x);

            return point;
        }
    }
}
