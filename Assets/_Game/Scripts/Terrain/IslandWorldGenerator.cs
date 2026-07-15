using System;
using System.Collections.Generic;
using IslandGame.Data.World;
using UnityEngine;

namespace IslandGame.Terrain
{
    /// <summary>
    /// Procedural island/ocean generation (Phase 7). Fills chunks column by
    /// column from two independent noise layers:
    ///
    ///   1. ISLAND MASK — low-frequency 2-octave fbm answers "is there land
    ///      here at all". Values above Land Threshold become land; the band
    ///      just above the threshold (Coast Falloff) smoothsteps a 0→1 land
    ///      factor, which is what produces real coastlines: columns blend from
    ///      ocean floor up to full island height across the band instead of
    ///      cliffing at a cutoff. Raising the threshold = more ocean; lowering
    ///      the frequency = larger, rarer islands.
    ///   2. HEIGHT — higher-frequency 4-octave fbm shapes hills ON the
    ///      islands: column height = lerp(ocean floor, sea level + height
    ///      noise × Max Island Height, land factor).
    ///
    /// Surface layering: columns whose top sits within Beach Band of sea level
    /// (or underwater) get sand; higher land gets grass over dirt over stone.
    /// Water fills every column from its floor up to sea level.
    ///
    /// NOISE CHOICE: Mathf.PerlinNoise — zero dependencies, plenty of quality
    /// for octave-stacked heightfields, and deterministic for given inputs
    /// (per Unity version/platform — fine for testing and save/load; note it
    /// has no seed parameter, so seeding works by offsetting the sample domain
    /// per octave with offsets drawn from System.Random(seed), which IS fully
    /// deterministic). If a later phase needs 3D noise (caves/overhangs),
    /// swap to Unity.Mathematics noise behind this same class.
    ///
    /// TREES (small-voxel trees phase — biome LAYERING only, the noise/island
    /// shaping above is untouched): the world is divided into 8×8-block anchor
    /// cells; each cell rolls a seeded hash for occupancy (Tree Density),
    /// jittered position, template (weighted from the TreeTemplateDatabase)
    /// and yaw. An anchor plants only on grassy columns (above the beach
    /// band). Because ComputeColumnHeight is a pure function of (x,z), a chunk
    /// evaluates anchors up to the largest template's extent BEYOND its own
    /// border and rasterizes just the local slice — trees straddle chunk seams
    /// with no ordering dependence. Trees generate when a chunk's data first
    /// generates, which always happens before the player can reach (data
    /// streams a ring beyond render distance, build reach is meters), so
    /// trees provably predate any player structure in their area — no
    /// PlacedPieceRegistry check is needed at generation time.
    ///
    /// ORGANIC SURFACE (organic-terrain phase): when enabled, each column's
    /// fill is shaped by OrganicSurfaceShaper — solid blocks up to the band
    /// the fine heightfield crosses, then that band is promoted and carved
    /// through the destruction system's existing sub-voxel pipeline. The
    /// coarse noise above is untouched (same seeds keep their islands);
    /// SampleHeight becomes shaping-aware so trees and structures sit on the
    /// surface the player actually sees. Interior blocks and the deep ocean
    /// floor never promote — see the shaper's performance notes.
    /// </summary>
    [Serializable]
    public sealed class IslandWorldGenerator : IChunkGenerator
    {
        [Header("Seed")]
        [Tooltip("Same seed = same world, reliably.")]
        [SerializeField] private int seed = 1337;

        [Header("Sea & Ocean Floor")]
        [Tooltip("Water surface sits at this world Y (water fills blocks below it).")]
        [Range(8, 48)]
        [SerializeField] private int seaLevel = 24;

        [Tooltip("Ocean floor depth below sea level (before floor noise).")]
        [Range(2, 20)]
        [SerializeField] private int oceanDepth = 10;

        [Tooltip("Gentle height variation of the ocean floor, blocks.")]
        [Range(0f, 6f)]
        [SerializeField] private float oceanFloorNoise = 3f;

        [Header("Island Mask")]
        [Tooltip("Lower = larger and rarer islands. This is 'how often does land happen'.")]
        [SerializeField] private float islandMaskFrequency = 0.005f;

        [Tooltip("Mask values above this are land. Higher = more ocean, less land — keep well above 0.5 for the 'mostly ocean' brief.")]
        [Range(0.4f, 0.9f)]
        [SerializeField] private float landThreshold = 0.62f;

        [Tooltip("Mask band above the threshold over which coastlines slope from ocean floor to full island — wider = flatter, longer beaches.")]
        [Range(0.02f, 0.3f)]
        [SerializeField] private float coastFalloff = 0.1f;

        [Header("Island Height")]
        [Tooltip("Frequency of the hill-shaping noise on land.")]
        [SerializeField] private float heightFrequency = 0.02f;

        [Range(1, 6)]
        [SerializeField] private int heightOctaves = 4;

        [Tooltip("Maximum island height above sea level, blocks.")]
        [Range(2f, 30f)]
        [SerializeField] private float maxIslandHeight = 14f;

        [Header("Spawn Island")]
        [Tooltip("Blend a guaranteed island in around the world origin, so spawn lands on a beach for every seed.")]
        [SerializeField] private bool forceIslandAtOrigin = true;

        [Min(8f)]
        [SerializeField] private float spawnIslandRadius = 56f;

        [Header("Surface Layers")]
        [Tooltip("Columns topping out within this many blocks of sea level (and all underwater floors) are sand.")]
        [Range(0, 4)]
        [SerializeField] private int beachBand = 1;

        [Range(1, 6)]
        [SerializeField] private int sandDepth = 3;

        [Range(1, 6)]
        [SerializeField] private int dirtDepth = 3;

        [Header("Trees")]
        [Tooltip("Chance that one 8×8-block anchor cell grows a tree. 0.35 ≈ light forest on grassy land; 0 disables trees.")]
        [Range(0f, 1f)]
        [SerializeField] private float treeDensity = 0.35f;

        [Header("Organic Surface (Sub-Voxel Shaping)")]
        [Tooltip("Generation-time surface shaping through the destruction system's promote-and-carve pipeline: sloped beaches, rolling hills and cliff faces instead of block stair-steps. Interior terrain and the deep ocean floor stay plain blocks.")]
        [SerializeField] private OrganicSurfaceSettings organicSurface = new OrganicSurfaceSettings();

        [Header("Blocks (string IDs)")]
        [SerializeField] private string stoneBlockId = "stone";
        [SerializeField] private string dirtBlockId = "dirt";
        [SerializeField] private string grassBlockId = "grass";
        [SerializeField] private string sandBlockId = "sand";
        [SerializeField] private string waterBlockId = "water";

        private ushort stoneId;
        private ushort dirtId;
        private ushort grassId;
        private ushort sandId;
        private ushort waterId;
        private Vector2[] maskOffsets;
        private Vector2[] heightOffsets;
        private Vector2 floorOffset;

        private BlockPalette palette;
        private readonly List<TreeTemplateDefinition> treeTemplates = new List<TreeTemplateDefinition>();
        private int treeScanMargin;

        // Organic-terrain phase: the shaper drives the sub-voxel pipeline at
        // generation time. Null when disabled — every path below then falls
        // back to the exact pre-organic blocky behavior.
        private OrganicSurfaceShaper surfaceShaper;
        private int surfaceSubVoxelResolution = 8;

        public void Initialize(BlockPalette palette)
        {
            if (!palette.TryGetIdByStringId(stoneBlockId, out stoneId))
                Debug.LogError($"IslandWorldGenerator: required block '{stoneBlockId}' missing from the BlockDatabase — terrain will be empty.");

            // Missing surface blocks degrade to stone instead of holes; missing
            // water degrades to air (dry ocean) — both logged, neither fatal.
            if (!palette.TryGetIdByStringId(dirtBlockId, out dirtId))
                ReportFallback(dirtBlockId, ref dirtId, stoneId);
            if (!palette.TryGetIdByStringId(grassBlockId, out grassId))
                ReportFallback(grassBlockId, ref grassId, stoneId);
            if (!palette.TryGetIdByStringId(sandBlockId, out sandId))
                ReportFallback(sandBlockId, ref sandId, stoneId);
            if (!palette.TryGetIdByStringId(waterBlockId, out waterId))
                Debug.LogWarning($"IslandWorldGenerator: block '{waterBlockId}' missing — the ocean will be air. Run Island Game/Data/Create World Gen Content.");

            // Seeding: PerlinNoise has no seed input, so each octave samples a
            // different region of the infinite noise plane. Offsets stay small
            // enough (±10k) that float precision doesn't degrade the noise.
            var random = new System.Random(seed);
            maskOffsets = MakeOffsets(random, 2);
            heightOffsets = MakeOffsets(random, heightOctaves);
            floorOffset = MakeOffset(random);

            this.palette = palette;

            surfaceShaper = organicSurface != null && organicSurface.enabled
                ? new OrganicSurfaceShaper(
                    ComputeContinuousHeight, organicSurface, seed, surfaceSubVoxelResolution, seaLevel)
                : null;

            InitializeTrees(palette);
        }

        /// <summary>
        /// VoxelWorld hands over its configured sub-voxel resolution BEFORE
        /// Initialize, so generation-shaped cells are the same kind of grid as
        /// damage-promoted ones (one resolution, one meshing path).
        /// </summary>
        public void ConfigureSurfaceDetail(int subVoxelResolution)
        {
            surfaceSubVoxelResolution = subVoxelResolution;
        }

        private void InitializeTrees(BlockPalette blockPalette)
        {
            treeTemplates.Clear();
            treeScanMargin = 0;

            if (treeDensity <= 0f)
                return;

            TreeTemplateDatabase database = TreeTemplateDatabase.Instance;
            if (database == null || database.Count == 0)
            {
                Debug.LogWarning("IslandWorldGenerator: no tree templates found — islands will be treeless. Run Island Game/Data/Create Tree Content.");
                return;
            }

            foreach (TreeTemplateDefinition template in database.All)
            {
                if (template == null || template.SpawnWeight <= 0f || template.Strokes.Count == 0)
                    continue;

                if (blockPalette.GetId(template.TrunkBlock) == BlockPalette.AirId)
                {
                    Debug.LogWarning($"IslandWorldGenerator: tree template '{template.Id}' has an unresolvable trunk block — skipped.");
                    continue;
                }

                treeTemplates.Add(template);
                treeScanMargin = Mathf.Max(treeScanMargin, Mathf.CeilToInt(template.MaxHorizontalExtent) + 1);
            }
        }

        // ------------------------------------------------------------------
        // World-data access for the structures phase (READ-ONLY — the noise
        // and island shaping above are untouched; structures only sample the
        // same height/biome data trees already use).
        // ------------------------------------------------------------------

        /// <summary>The world seed (saved so untouched chunks regenerate identically on load).</summary>
        public int Seed => seed;

        /// <summary>
        /// Load-time seed override. Must be called BEFORE Initialize runs
        /// (the save system applies it in the scene-loaded callback, ahead of
        /// VoxelWorld.Start) — the noise offsets derive from it there.
        /// </summary>
        public void SetSeed(int newSeed)
        {
            seed = newSeed;
        }

        /// <summary>Water surface height, for beach/water site rules.</summary>
        public int SeaLevelY => seaLevel;

        /// <summary>Columns topping out within this band of sea level are sand (the tree/structure 'not grass' rule).</summary>
        public int BeachBandSize => beachBand;

        /// <summary>
        /// Terrain height (top solid block + 1) at a column — the same pure
        /// function chunk filling uses, INCLUDING organic surface shaping, so
        /// trees/structures sit on the surface the player actually sees.
        /// Valid after Initialize.
        /// </summary>
        public int SampleHeight(float worldX, float worldZ)
        {
            if (surfaceShaper != null && surfaceShaper.TrySampleSurfaceHeight(worldX, worldZ, out int shaped))
                return shaped;

            return ComputeColumnHeight(worldX, worldZ);
        }

        /// <summary>The generator's deterministic seeded hash, shared so structure placement is a pure function of the world seed too.</summary>
        public float SampleHash01(int x, int z, int salt)
        {
            return Hash01(x, z, salt);
        }

        public void Generate(Chunk chunk)
        {
            if (stoneId == BlockPalette.AirId)
                return;

            int originX = chunk.Coord.x * Chunk.SizeX;
            int originZ = chunk.Coord.y * Chunk.SizeZ;

            surfaceShaper?.BeginChunk(originX, originZ);

            for (int z = 0; z < Chunk.SizeZ; z++)
            {
                for (int x = 0; x < Chunk.SizeX; x++)
                {
                    if (surfaceShaper != null)
                    {
                        surfaceShaper.ShapeColumn(x, z, out OrganicSurfaceShaper.ColumnShape shape);
                        if (shape.Shaped)
                        {
                            FillShapedColumn(chunk, x, z, in shape);
                            surfaceShaper.ApplyColumn(chunk, x, z, in shape);
                            continue;
                        }
                    }

                    int height = ComputeColumnHeight(originX + x, originZ + z);
                    FillColumn(chunk, x, z, height);
                }
            }

            PlantTrees(chunk, originX, originZ);
        }

        // ------------------------------------------------------------------
        // Trees (see class summary — deterministic anchor cells, local writes)
        // ------------------------------------------------------------------

        private const int TreeAnchorCellSize = 8;
        private const int TreeAnchorShift = 3;

        private void PlantTrees(Chunk chunk, int originX, int originZ)
        {
            if (treeTemplates.Count == 0)
                return;

            // Every anchor cell whose tree could reach into this chunk.
            int minAnchorX = (originX - treeScanMargin) >> TreeAnchorShift;
            int maxAnchorX = (originX + Chunk.SizeX - 1 + treeScanMargin) >> TreeAnchorShift;
            int minAnchorZ = (originZ - treeScanMargin) >> TreeAnchorShift;
            int maxAnchorZ = (originZ + Chunk.SizeZ - 1 + treeScanMargin) >> TreeAnchorShift;

            for (int anchorZ = minAnchorZ; anchorZ <= maxAnchorZ; anchorZ++)
            {
                for (int anchorX = minAnchorX; anchorX <= maxAnchorX; anchorX++)
                {
                    if (Hash01(anchorX, anchorZ, 101) >= treeDensity)
                        continue;

                    // Jittered position inside the anchor cell.
                    int worldX = (anchorX << TreeAnchorShift)
                                 + (int)(Hash01(anchorX, anchorZ, 102) * TreeAnchorCellSize);
                    int worldZ = (anchorZ << TreeAnchorShift)
                                 + (int)(Hash01(anchorX, anchorZ, 103) * TreeAnchorCellSize);

                    // Suitable surface only: grassy land above the beach band
                    // (FillColumn's rule — sand/underwater columns never get
                    // grass, so this is exactly "grass, not sand/stone").
                    // SampleHeight is shaping-aware, so the base cell sits on
                    // the surface the shaper actually produced.
                    int surface = SampleHeight(worldX, worldZ);
                    if (surface <= seaLevel + beachBand)
                        continue;

                    // Variant pick is biome-aware since the foliage phase:
                    // each template declares an altitude band (willows hug
                    // the coast, dead trees claim the high ground) and the
                    // weighted roll runs over the eligible set only. Altitude
                    // is a pure function of (x,z), so determinism holds.
                    TreeTemplateDefinition template = PickTemplate(anchorX, anchorZ, surface - seaLevel);
                    if (template == null)
                        continue; // no variant grows at this altitude

                    if (surface + template.MaxHeight >= Chunk.SizeY - 1)
                        continue; // too close to the world ceiling

                    int yawSteps = (int)(Hash01(anchorX, anchorZ, 105) * 4f);
                    TreeRasterizer.Rasterize(
                        chunk, palette, template, new Vector3Int(worldX, surface, worldZ), yawSteps);
                    AnchorTreeBase(chunk, originX, originZ, worldX, surface - 1, worldZ);
                }
            }
        }

        /// <summary>
        /// Organic shaping leaves the block under a trunk partially carved, and
        /// TreeRasterizer never overwrites terrain — so refill that one grid to
        /// solid or trunks hover above the fine surface on their downhill side.
        /// Reads as a small root mound. Only the chunk that owns the column
        /// writes it (order-independent, deterministic — the same ownership
        /// rule rasterization itself follows).
        /// </summary>
        private static void AnchorTreeBase(Chunk chunk, int originX, int originZ, int worldX, int y, int worldZ)
        {
            if (y < 0 || y >= Chunk.SizeY)
                return;

            int localX = worldX - originX;
            int localZ = worldZ - originZ;
            if (localX < 0 || localX >= Chunk.SizeX || localZ < 0 || localZ >= Chunk.SizeZ)
                return;

            if (!chunk.TryGetSubVoxels(localX, y, localZ, out SubVoxelGrid grid))
                return; // plain full block — already solid ground

            int res = grid.Resolution;
            for (int sy = 0; sy < res; sy++)
            {
                for (int sz = 0; sz < res; sz++)
                {
                    for (int sx = 0; sx < res; sx++)
                        grid.Set(sx, sy, sz, true);
                }
            }
        }

        /// <summary>
        /// Weighted pick among the templates whose altitude band accepts this
        /// anchor's surface (foliage phase — templates without a band keep
        /// the old everywhere behavior). The eligible weight total is summed
        /// per anchor: the template list is a handful of entries, so two
        /// passes cost less than any caching scheme would obscure. Null when
        /// nothing grows at this altitude.
        /// </summary>
        private TreeTemplateDefinition PickTemplate(int anchorX, int anchorZ, int altitudeAboveSea)
        {
            float eligibleWeightTotal = 0f;
            for (int i = 0; i < treeTemplates.Count; i++)
            {
                if (treeTemplates[i].IsEligibleAtAltitude(altitudeAboveSea))
                    eligibleWeightTotal += treeTemplates[i].SpawnWeight;
            }

            if (eligibleWeightTotal <= 0f)
                return null;

            float roll = Hash01(anchorX, anchorZ, 104) * eligibleWeightTotal;
            TreeTemplateDefinition last = null;
            for (int i = 0; i < treeTemplates.Count; i++)
            {
                TreeTemplateDefinition template = treeTemplates[i];
                if (!template.IsEligibleAtAltitude(altitudeAboveSea))
                    continue;

                last = template;
                roll -= template.SpawnWeight;
                if (roll <= 0f)
                    return template;
            }

            return last;
        }

        /// <summary>Deterministic seeded hash → [0,1). Same (seed, x, z, salt) always yields the same value on every machine.</summary>
        private float Hash01(int x, int z, int salt)
        {
            unchecked
            {
                uint h = (uint)seed;
                h = h * 374761393u + (uint)x * 668265263u;
                h ^= h >> 13;
                h *= 1274126177u;
                h += (uint)z * 2246822519u + (uint)salt * 3266489917u;
                h ^= h >> 16;
                return (h & 0xFFFFFF) / 16777216f;
            }
        }

        // ------------------------------------------------------------------
        // Column shaping
        // ------------------------------------------------------------------

        private int ComputeColumnHeight(float worldX, float worldZ)
        {
            return Mathf.RoundToInt(ComputeContinuousHeight(worldX, worldZ));
        }

        /// <summary>
        /// The un-rounded column height — the organic surface shaper's coarse
        /// field (it samples this at block corners and interpolates between).
        /// Rounding this IS the pre-organic blocky height, so both fill paths
        /// derive from one function. Clamp bounds are integers, so
        /// round-then-clamp and clamp-then-round agree exactly.
        /// </summary>
        private float ComputeContinuousHeight(float worldX, float worldZ)
        {
            float mask = Fbm(maskOffsets, worldX, worldZ, islandMaskFrequency);
            float landFactor = Mathf.SmoothStep(0f, 1f,
                Mathf.InverseLerp(landThreshold, landThreshold + coastFalloff, mask));

            if (forceIslandAtOrigin)
            {
                float distance = Mathf.Sqrt(worldX * worldX + worldZ * worldZ);
                float bump = Mathf.SmoothStep(1f, 0f, Mathf.Clamp01(distance / spawnIslandRadius));
                landFactor = Mathf.Max(landFactor, bump);
            }

            float heightNoise = Fbm(heightOffsets, worldX, worldZ, heightFrequency);
            float landHeight = seaLevel + 1f + heightNoise * maxIslandHeight;

            float floorNoise = Mathf.Clamp01(Mathf.PerlinNoise(
                worldX * islandMaskFrequency * 4f + floorOffset.x,
                worldZ * islandMaskFrequency * 4f + floorOffset.y));
            float oceanFloor = seaLevel - oceanDepth + floorNoise * this.oceanFloorNoise;

            return Mathf.Clamp(Mathf.Lerp(oceanFloor, landHeight, landFactor), 2f, Chunk.SizeY - 2f);
        }

        private void FillColumn(Chunk chunk, int x, int z, int height)
        {
            bool sandy = height <= seaLevel + beachBand; // beaches AND every underwater floor

            for (int y = 0; y < height; y++)
            {
                int depthFromTop = height - 1 - y;
                ushort id;
                if (sandy)
                    id = depthFromTop < sandDepth ? sandId : stoneId;
                else if (depthFromTop == 0)
                    id = grassId;
                else if (depthFromTop <= dirtDepth)
                    id = dirtId;
                else
                    id = stoneId;

                chunk.Set(x, y, z, id);
            }

            if (waterId != BlockPalette.AirId)
            {
                for (int y = height; y < seaLevel; y++)
                    chunk.Set(x, y, z, waterId);
            }
        }

        /// <summary>
        /// Block fill for an organically shaped column: solid up to the shaped
        /// band's top (ApplyColumn immediately carves the band blocks down to
        /// the fine surface). Biome layering is measured from the band so the
        /// VISUALLY topmost filled sub-cells carry the biome surface material:
        /// every partially-filled band block is grass (or sand on beaches and
        /// shallow floors), with dirt/stone depth counted below the band —
        /// matching FillColumn's layer thicknesses on flat ground.
        /// </summary>
        private void FillShapedColumn(Chunk chunk, int x, int z, in OrganicSurfaceShaper.ColumnShape shape)
        {
            int topSolid = shape.TopSolidY;
            int height = topSolid + 1;
            bool sandy = height <= seaLevel + beachBand; // beaches AND shaped shallow floors
            int bandMin = Mathf.Min(shape.BandMinY, topSolid);

            for (int y = 0; y < height; y++)
            {
                ushort id;
                if (sandy)
                    id = topSolid - y < sandDepth ? sandId : stoneId;
                else if (y >= bandMin)
                    id = grassId;
                else
                    id = bandMin - y <= dirtDepth ? dirtId : stoneId;

                chunk.Set(x, y, z, id);
            }

            if (waterId != BlockPalette.AirId)
            {
                for (int y = height; y < seaLevel; y++)
                    chunk.Set(x, y, z, waterId);
            }
        }

        // ------------------------------------------------------------------
        // Noise plumbing
        // ------------------------------------------------------------------

        /// <summary>Octave-stacked Perlin, normalized to [0,1]. Internal so OrganicSurfaceShaper reuses the exact same noise plumbing for its fine relief term.</summary>
        internal static float Fbm(Vector2[] octaveOffsets, float x, float z, float frequency)
        {
            float total = 0f;
            float amplitude = 1f;
            float amplitudeSum = 0f;

            for (int i = 0; i < octaveOffsets.Length; i++)
            {
                total += amplitude * Mathf.PerlinNoise(
                    x * frequency + octaveOffsets[i].x,
                    z * frequency + octaveOffsets[i].y);
                amplitudeSum += amplitude;
                amplitude *= 0.5f;
                frequency *= 2f;
            }

            return Mathf.Clamp01(total / amplitudeSum);
        }

        private static Vector2[] MakeOffsets(System.Random random, int count)
        {
            var offsets = new Vector2[count];
            for (int i = 0; i < count; i++)
                offsets[i] = MakeOffset(random);
            return offsets;
        }

        private static Vector2 MakeOffset(System.Random random)
        {
            return new Vector2(
                (float)(random.NextDouble() * 20000.0 - 10000.0),
                (float)(random.NextDouble() * 20000.0 - 10000.0));
        }

        private static void ReportFallback(string missingId, ref ushort target, ushort fallback)
        {
            Debug.LogWarning($"IslandWorldGenerator: block '{missingId}' missing — using stone instead. Run Island Game/Data/Create World Gen Content.");
            target = fallback;
        }
    }
}
