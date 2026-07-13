using System;
using UnityEngine;

namespace IslandGame.Terrain
{
    /// <summary>
    /// Tuning for generation-time organic surface shaping. Serialized by
    /// IslandWorldGenerator so all shaping knobs live in one inspector foldout;
    /// the shaper captures them at Initialize (mid-session changes apply on the
    /// next world start, like every other generation setting).
    /// </summary>
    [Serializable]
    public sealed class OrganicSurfaceSettings
    {
        [Tooltip("Master switch. Off = exactly the pre-organic blocky terrain (and zero shaping cost).")]
        public bool enabled = true;

        [Tooltip("Frequency of the fine surface-relief noise, cycles per block. ~0.45 gives features a couple of blocks wide with genuine sub-block variation.")]
        [Range(0.05f, 2f)]
        public float detailFrequency = 0.45f;

        [Tooltip("Octaves of fine relief noise. Each octave costs one PerlinNoise call per SUB-COLUMN during generation — the single biggest generation cost knob. 2 looks organic; drop to 1 if chunk streaming hitches.")]
        [Range(1, 3)]
        public int detailOctaves = 2;

        [Tooltip("Amplitude of the fine relief in blocks (peak-to-center). Keep below ~0.6 so relief stays surface texture rather than fake hills fighting the real heightfield.")]
        [Range(0f, 1.5f)]
        public float detailAmplitude = 0.45f;

        [Tooltip("How many blocks BELOW sea level shaping continues, so shorelines slope smoothly into shallow water. The ocean floor deeper than this stays plain blocks — it's hidden under water and promoting it would waste memory and mesh budget.")]
        [Range(0, 8)]
        public int shoreShapingDepth = 3;

        [Tooltip("Vertical band around sea level (blocks) where fine relief fades down to Shore Detail Scale — beaches read as smooth wet sand instead of noisy rubble.")]
        [Range(0.5f, 4f)]
        public float shoreSmoothingBand = 2f;

        [Tooltip("Relief multiplier right at sea level (fades back to 1 over the Shore Smoothing Band).")]
        [Range(0f, 1f)]
        public float shoreDetailScale = 0.25f;
    }

    /// <summary>
    /// Generation-time organic surface shaping (organic-terrain phase): gives
    /// island surfaces natural slopes by driving the destruction system's
    /// EXISTING promote-and-carve pipeline with a fine-resolution heightfield
    /// sample instead of a damage sphere. There is deliberately no second
    /// voxel-shaping mechanism here — a shaped surface block is a promoted
    /// SubVoxelGrid identical in kind to a mined one, so meshing, collision,
    /// mining, save/load and support collapse all keep working unchanged.
    ///
    /// THE FINE HEIGHTFIELD: per sub-column, height = bilinear interpolation
    /// of the generator's continuous (un-rounded) coarse height sampled at the
    /// four surrounding block corners, PLUS a genuinely fine-resolution fbm
    /// relief term sampled at the sub-column's own world position. The bilinear
    /// part is exact-enough reuse of the coarse noise — at the island noise
    /// frequencies (≤0.16 cycles/block) the true field varies sub-linearly
    /// inside one block, so re-sampling the full fbm per sub-column would cost
    /// 7× more Perlin calls for no visible difference — while the relief term
    /// is REAL new sub-block signal, which is what stops the result being a
    /// smoothed reinterpretation of the same coarse samples.
    ///
    /// PERFORMANCE ENVELOPE (why this is safe at island scale):
    ///   • Only the crossing band promotes — the 1-2 blocks per column the
    ///     fine surface actually passes through (more only where cliffs are
    ///     steep, proportional to local relief). Blocks fully below stay plain
    ///     2-byte blocks; blocks fully above stay air.
    ///   • The ocean floor deeper than shoreShapingDepth never shapes at all.
    ///   • Coarse noise: one fbm per block CORNER (17×17 per chunk — the same
    ///     order of cost as the pre-organic per-column sampling). Fine noise:
    ///     detailOctaves Perlin calls per sub-column, land columns only.
    ///   • Everything is a pure function of (seed, world position), so chunks
    ///     shape independently, in any order, with seamless borders — and
    ///     regeneration is exactly reproducible for the save system's deltas.
    ///
    /// Single instance owned by IslandWorldGenerator; scratch buffers are
    /// reused per column (generation is single-threaded in VoxelWorld.Update).
    /// </summary>
    public sealed class OrganicSurfaceShaper
    {
        /// <summary>One column's shaping result — which blocks are solid, and which band ApplyColumn will promote and carve.</summary>
        public struct ColumnShape
        {
            /// <summary>False = column keeps the pre-organic blocky fill (deep ocean floor, or shaping disabled).</summary>
            public bool Shaped;

            /// <summary>Topmost block containing any filled sub-cell — the column's solid fill goes up to and including this.</summary>
            public int TopSolidY;

            /// <summary>Lowest partially-filled block. Greater than TopSolidY when the fine surface happens to cut cleanly between blocks (no promotion needed).</summary>
            public int BandMinY;
        }

        private const int CornersX = Chunk.SizeX + 1;
        private const int CornersZ = Chunk.SizeZ + 1;

        private readonly Func<float, float, float> coarseHeight;
        private readonly OrganicSurfaceSettings settings;
        private readonly int seaLevel;
        private readonly int resolution;
        private readonly float subSize;
        private readonly float halfSub;
        private readonly Vector2[] detailOffsets;

        // Per-chunk corner cache: coarse continuous height at the 17×17 block
        // corners. Corners sit on the world-integer lattice, so neighboring
        // chunks compute IDENTICAL values for shared corners — seamless.
        private readonly float[] cornerCache = new float[CornersX * CornersZ];
        private int chunkOriginX;
        private int chunkOriginZ;

        // ShapeColumn → ApplyColumn scratch for ONE column: the fine height of
        // each sub-column (index = sv * resolution + su) and its min/max.
        // ApplyColumn must run immediately after ShapeColumn for that column.
        private readonly float[] columnFine;
        private float columnMin;
        private float columnMax;

        /// <param name="coarseHeight">The generator's continuous (float, un-rounded) column height — the same function the blocky fill rounds.</param>
        /// <param name="subVoxelResolution">MUST match VoxelWorld's promotion resolution, so generation-shaped cells and damage-promoted cells are the same kind of grid.</param>
        public OrganicSurfaceShaper(
            Func<float, float, float> coarseHeight, OrganicSurfaceSettings settings,
            int seed, int subVoxelResolution, int seaLevel)
        {
            this.coarseHeight = coarseHeight;
            this.settings = settings;
            this.seaLevel = seaLevel;

            resolution = Mathf.Clamp(subVoxelResolution, 2, 16);
            subSize = 1f / resolution;
            halfSub = 0.5f * subSize;
            columnFine = new float[resolution * resolution];

            // Own seeded domain offsets, derived from the world seed but NOT
            // drawn from the generator's Random sequence — existing seeds keep
            // their exact coarse terrain, and same seed = same relief, always.
            var random = new System.Random(unchecked(seed * 486187739 + 1013904223));
            detailOffsets = new Vector2[Mathf.Clamp(settings.detailOctaves, 1, 3)];
            for (int i = 0; i < detailOffsets.Length; i++)
            {
                detailOffsets[i] = new Vector2(
                    (float)(random.NextDouble() * 20000.0 - 10000.0),
                    (float)(random.NextDouble() * 20000.0 - 10000.0));
            }
        }

        /// <summary>Caches the chunk's coarse corner heights — call once per Generate, before any ShapeColumn.</summary>
        public void BeginChunk(int originX, int originZ)
        {
            chunkOriginX = originX;
            chunkOriginZ = originZ;

            int index = 0;
            for (int iz = 0; iz < CornersZ; iz++)
            {
                for (int ix = 0; ix < CornersX; ix++)
                    cornerCache[index++] = coarseHeight(originX + ix, originZ + iz);
            }
        }

        /// <summary>
        /// Computes one column's fine heightfield and shaping band from the
        /// chunk corner cache. Leaves the fine samples in scratch for the
        /// ApplyColumn call that must directly follow.
        /// </summary>
        public void ShapeColumn(int localX, int localZ, out ColumnShape shape)
        {
            float c00 = cornerCache[localZ * CornersX + localX];
            float c10 = cornerCache[localZ * CornersX + localX + 1];
            float c01 = cornerCache[(localZ + 1) * CornersX + localX];
            float c11 = cornerCache[(localZ + 1) * CornersX + localX + 1];

            ComputeColumn(chunkOriginX + localX, chunkOriginZ + localZ, c00, c10, c01, c11, out shape);
        }

        /// <summary>
        /// Promote-and-carve for one shaped column — the destruction pipeline
        /// driven by generation data: each crossing-band block gets the same
        /// fully-filled promotion damage uses, then every sub-cell whose CENTER
        /// sits at or above its sub-column's fine height is cleared (the same
        /// center-containment rule CarveSphere applies to its damage sphere).
        /// Blocks the band predicate proves fully solid are skipped and stay
        /// plain 2-byte blocks. Must run right after ShapeColumn (shared scratch).
        /// </summary>
        public void ApplyColumn(Chunk chunk, int localX, int localZ, in ColumnShape shape)
        {
            if (!shape.Shaped)
                return;

            for (int y = Mathf.Max(1, shape.BandMinY); y <= shape.TopSolidY; y++)
            {
                if (columnMin >= y + 1f - halfSub)
                    continue; // every sub-cell center is below the surface — fully solid, no promotion

                SubVoxelGrid grid = chunk.PromoteToSubVoxels(localX, y, localZ, resolution);

                for (int sy = resolution - 1; sy >= 0; sy--)
                {
                    float centerY = y + (sy + 0.5f) * subSize;
                    if (centerY < columnMin)
                        break; // this layer and everything below is fully inside the ground

                    bool clearWholeLayer = centerY >= columnMax;
                    int index = 0;
                    for (int sv = 0; sv < resolution; sv++)
                    {
                        for (int su = 0; su < resolution; su++)
                        {
                            if (clearWholeLayer || centerY >= columnFine[index])
                                grid.Set(su, sy, sv, false);
                            index++;
                        }
                    }
                }

                // Float-edge safety: a grid that carved completely empty must
                // not linger — demote the cell to air like the damage path does.
                if (grid.IsEmpty)
                    chunk.Set(localX, y, localZ, BlockPalette.AirId);
            }
        }

        /// <summary>
        /// Standalone surface query for trees/structures: the height (top solid
        /// block + 1) this shaper actually produces at a world position — kept
        /// exactly consistent with Generate by running the same column math.
        /// False when the column is unshaped (deep ocean) — the caller falls
        /// back to the generator's blocky column height. Clobbers the column
        /// scratch, so never call between a ShapeColumn/ApplyColumn pair.
        /// </summary>
        public bool TrySampleSurfaceHeight(float worldX, float worldZ, out int height)
        {
            int blockX = Mathf.FloorToInt(worldX);
            int blockZ = Mathf.FloorToInt(worldZ);

            float c00 = coarseHeight(blockX, blockZ);
            float c10 = coarseHeight(blockX + 1, blockZ);
            float c01 = coarseHeight(blockX, blockZ + 1);
            float c11 = coarseHeight(blockX + 1, blockZ + 1);

            ComputeColumn(blockX, blockZ, c00, c10, c01, c11, out ColumnShape shape);
            height = shape.Shaped ? shape.TopSolidY + 1 : 0;
            return shape.Shaped;
        }

        private void ComputeColumn(
            int blockX, int blockZ, float c00, float c10, float c01, float c11, out ColumnShape shape)
        {
            shape = default;

            float coarseCenter = (c00 + c10 + c01 + c11) * 0.25f;
            if (coarseCenter < seaLevel - settings.shoreShapingDepth)
                return; // deep ocean floor: stays plain blocks, zero fine samples

            // Relief fades toward sea level so beaches read as smooth wet sand.
            float relief = settings.detailAmplitude * Mathf.Lerp(
                settings.shoreDetailScale, 1f,
                Mathf.Clamp01(Mathf.Abs(coarseCenter - seaLevel) / settings.shoreSmoothingBand));

            float min = float.MaxValue;
            float max = float.MinValue;
            int index = 0;
            for (int sv = 0; sv < resolution; sv++)
            {
                float v = (sv + 0.5f) * subSize;
                float fz = blockZ + v;
                for (int su = 0; su < resolution; su++)
                {
                    float u = (su + 0.5f) * subSize;
                    float fx = blockX + u;

                    float coarse = Mathf.Lerp(Mathf.Lerp(c00, c10, u), Mathf.Lerp(c01, c11, u), v);
                    float detail = (IslandWorldGenerator.Fbm(detailOffsets, fx, fz, settings.detailFrequency) - 0.5f) * 2f * relief;
                    float fine = Mathf.Clamp(coarse + detail, 2f, Chunk.SizeY - 2f);

                    columnFine[index++] = fine;
                    if (fine < min)
                        min = fine;
                    if (fine > max)
                        max = fine;
                }
            }

            columnMin = min;
            columnMax = max;

            // Block y holds a filled sub-cell iff its lowest center (y + halfSub)
            // is under the column's highest fine surface; it is FULLY solid iff
            // its highest center (y + 1 - halfSub) is under the lowest surface.
            shape.Shaped = true;
            shape.TopSolidY = Mathf.Clamp(Mathf.CeilToInt(max - halfSub) - 1, 1, Chunk.SizeY - 2);
            shape.BandMinY = Mathf.Max(1, Mathf.CeilToInt(min - 1f + halfSub));
        }
    }
}
