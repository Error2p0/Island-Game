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
        private float treeWeightTotal;
        private int treeScanMargin;

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
            InitializeTrees(palette);
        }

        private void InitializeTrees(BlockPalette blockPalette)
        {
            treeTemplates.Clear();
            treeWeightTotal = 0f;
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
                treeWeightTotal += template.SpawnWeight;
                treeScanMargin = Mathf.Max(treeScanMargin, Mathf.CeilToInt(template.MaxHorizontalExtent) + 1);
            }
        }

        // ------------------------------------------------------------------
        // World-data access for the structures phase (READ-ONLY — the noise
        // and island shaping above are untouched; structures only sample the
        // same height/biome data trees already use).
        // ------------------------------------------------------------------

        /// <summary>Water surface height, for beach/water site rules.</summary>
        public int SeaLevelY => seaLevel;

        /// <summary>Columns topping out within this band of sea level are sand (the tree/structure 'not grass' rule).</summary>
        public int BeachBandSize => beachBand;

        /// <summary>Terrain height at a column — the same pure function chunk filling uses. Valid after Initialize.</summary>
        public int SampleHeight(float worldX, float worldZ)
        {
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

            for (int z = 0; z < Chunk.SizeZ; z++)
            {
                for (int x = 0; x < Chunk.SizeX; x++)
                {
                    float worldX = originX + x;
                    float worldZ = originZ + z;

                    int height = ComputeColumnHeight(worldX, worldZ);
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
                    int surface = ComputeColumnHeight(worldX, worldZ);
                    if (surface <= seaLevel + beachBand)
                        continue;

                    TreeTemplateDefinition template = PickTemplate(anchorX, anchorZ);
                    if (surface + template.MaxHeight >= Chunk.SizeY - 1)
                        continue; // too close to the world ceiling

                    int yawSteps = (int)(Hash01(anchorX, anchorZ, 105) * 4f);
                    TreeRasterizer.Rasterize(
                        chunk, palette, template, new Vector3Int(worldX, surface, worldZ), yawSteps);
                }
            }
        }

        private TreeTemplateDefinition PickTemplate(int anchorX, int anchorZ)
        {
            float roll = Hash01(anchorX, anchorZ, 104) * treeWeightTotal;
            for (int i = 0; i < treeTemplates.Count; i++)
            {
                roll -= treeTemplates[i].SpawnWeight;
                if (roll <= 0f)
                    return treeTemplates[i];
            }

            return treeTemplates[treeTemplates.Count - 1];
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

            return Mathf.Clamp(Mathf.RoundToInt(Mathf.Lerp(oceanFloor, landHeight, landFactor)), 2, Chunk.SizeY - 2);
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

        // ------------------------------------------------------------------
        // Noise plumbing
        // ------------------------------------------------------------------

        private static float Fbm(Vector2[] octaveOffsets, float x, float z, float frequency)
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
