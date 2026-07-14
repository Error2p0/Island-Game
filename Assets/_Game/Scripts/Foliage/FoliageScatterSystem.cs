using System.Collections.Generic;
using IslandGame.Data.Foliage;
using IslandGame.Player;
using IslandGame.Terrain;
using UnityEngine;

namespace IslandGame.Foliage
{
    /// <summary>
    /// Scatters FoliageDefinitions across the generated islands — the same
    /// CATEGORY of system as tree scattering (deterministic seeded anchor
    /// cells validated against the generator's height data; the noise/island
    /// shaping itself is untouched) and the same GameObject pattern as
    /// structure placement, tuned for foliage's scale:
    ///
    ///   CELLS — the world is divided into Cell Size anchor cells; each rolls
    ///   seeded occupancy (Foliage Density), a jittered position, a variety
    ///   (weighted among definitions whose surface rule passes at that
    ///   column), yaw and scale — all through the generator's own hash, so a
    ///   seed always grows the same bush in the same place.
    ///
    ///   SURFACE RULES — one generator height sample per candidate cell:
    ///   Grass varieties need grassy land (above the beach band, the tree
    ///   rule); Shore varieties need the shoreline band around sea level —
    ///   reeds may stand ankle-deep. Underwater columns grow nothing.
    ///
    ///   STREAMING — unlike structures (placed once, kept forever — looting
    ///   matters), foliage is numerous and stateless-cheap: instances spawn
    ///   inside Spawn Radius and despawn beyond Despawn Radius into
    ///   per-variety pools. Harvest state survives streaming: a depleted
    ///   plant's regrow time is remembered per cell and re-applied on
    ///   respawn (already-elapsed timers come back fully grown). Cell
    ///   verdicts (barren / planned spot) are cached so re-entering an area
    ///   re-rolls nothing and re-samples no heights.
    ///
    /// SAVE: session-only on purpose — the same tradeoff the save phase made
    /// for wild creatures. The scatter itself is a pure function of the world
    /// seed, so plants regenerate identically on load; only in-flight regrow
    /// timers reset (bushes load fully grown). A future phase can persist the
    /// depleted-cell map through the additive SaveGame policy if that ever
    /// matters.
    ///
    /// PERFORMANCE: a 0.5 s tick walks the cells inside Spawn Radius; unseen
    /// cells cost one hash roll (most miss) plus, for hits, one height sample
    /// and a weighted pick — then the verdict is cached for the session.
    /// Steady-state ticks over explored ground are dictionary lookups only.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class FoliageScatterSystem : MonoBehaviour
    {
        [Tooltip("Anchor cell size, meters — one plant max per cell, so this is also the minimum foliage spacing.")]
        [Range(3, 16)]
        [SerializeField] private int cellSize = 5;

        [Tooltip("Seeded chance an anchor cell grows a plant (surface rules may still reject it). 0.3 with 5 m cells reads as healthy undergrowth without crowding the trees.")]
        [Range(0f, 1f)]
        [SerializeField] private float foliageDensity = 0.3f;

        [Tooltip("Plants spawn when the player is within this range, meters.")]
        [Min(20f)]
        [SerializeField] private float spawnRadius = 55f;

        [Tooltip("Plants despawn (into the pool, keeping harvest state) beyond this range. Keep comfortably above Spawn Radius so the ring edge never flickers.")]
        [Min(25f)]
        [SerializeField] private float despawnRadius = 68f;

        [Tooltip("Plants sink this far into the ground so organic-surface slopes never leave them floating.")]
        [Range(0f, 0.6f)]
        [SerializeField] private float groundSink = 0.25f;

        [Tooltip("Wired by the builder; auto-resolved when empty.")]
        [SerializeField] private VoxelWorld world;

        private const float TickInterval = 0.5f;

        // Hash salts 500+ — trees use 101-104, structures 200+/300+/400+.
        private const int SaltOccupancy = 500;
        private const int SaltJitterX = 501;
        private const int SaltJitterZ = 502;
        private const int SaltVariety = 503;
        private const int SaltYaw = 504;
        private const int SaltScale = 505;

        private struct PlannedPlant
        {
            public FoliageDefinition definition;
            public Vector3 position;
            public float yawDegrees;
            public float scale;
        }

        private sealed class ActivePlant
        {
            public GameObject instance;
            public HarvestableFoliage harvestable;
            public FoliageDefinition definition;
            public Vector3 position;
        }

        private readonly HashSet<Vector2Int> barrenCells = new HashSet<Vector2Int>();
        private readonly Dictionary<Vector2Int, PlannedPlant> plannedCells = new Dictionary<Vector2Int, PlannedPlant>();
        private readonly Dictionary<Vector2Int, ActivePlant> activeCells = new Dictionary<Vector2Int, ActivePlant>();
        private readonly Dictionary<Vector2Int, double> depletedRegrowTimes = new Dictionary<Vector2Int, double>();
        private readonly Dictionary<string, Stack<GameObject>> pools = new Dictionary<string, Stack<GameObject>>();
        private readonly List<Vector2Int> despawnBuffer = new List<Vector2Int>(64);

        private readonly List<FoliageDefinition> grassVarieties = new List<FoliageDefinition>();
        private readonly List<FoliageDefinition> shoreVarieties = new List<FoliageDefinition>();
        private float grassWeightTotal;
        private float shoreWeightTotal;
        private bool varietiesInitialized;

        private PlayerReferences player;
        private float nextTickTime;

        private void Start()
        {
            if (world == null)
                world = FindFirstObjectByType<VoxelWorld>();
        }

        private void OnValidate()
        {
            if (despawnRadius < spawnRadius + cellSize)
                despawnRadius = spawnRadius + cellSize;
        }

        private void Update()
        {
            if (Time.time < nextTickTime)
                return;

            nextTickTime = Time.time + TickInterval;
            Tick();
        }

        private void Tick()
        {
            if (world == null || !world.IsReady)
                return;

            IslandWorldGenerator generator = world.ActiveIslandGenerator;
            if (generator == null)
                return; // debug-flat mode: no islands, no foliage

            if (!varietiesInitialized && !InitializeVarieties())
                return;

            if (player == null)
            {
                player = FindFirstObjectByType<PlayerReferences>();
                if (player == null)
                    return;
            }

            Vector3 playerPosition = player.transform.position;
            SpawnAround(playerPosition, generator);
            DespawnBeyond(playerPosition);
        }

        // ------------------------------------------------------------------
        // Variety registration (once per session, like the generator's trees)
        // ------------------------------------------------------------------

        private bool InitializeVarieties()
        {
            FoliageDatabase database = FoliageDatabase.Instance;
            if (database == null || database.Count == 0)
            {
                Debug.LogWarning(
                    "FoliageScatterSystem: no foliage definitions found — islands will be bare. " +
                    "Run Island Game/Data/Create Foliage Content.");
                varietiesInitialized = true; // don't warn every tick
                return false;
            }

            foreach (FoliageDefinition definition in database.All)
            {
                if (definition == null || definition.SpawnWeight <= 0f)
                    continue;

                if (definition.Prefab == null)
                {
                    Debug.LogWarning($"FoliageScatterSystem: foliage '{definition.Id}' has no prefab — skipped.");
                    continue;
                }

                if (definition.Surface == FoliageSurface.Grass)
                {
                    grassVarieties.Add(definition);
                    grassWeightTotal += definition.SpawnWeight;
                }
                else
                {
                    shoreVarieties.Add(definition);
                    shoreWeightTotal += definition.SpawnWeight;
                }
            }

            varietiesInitialized = true;
            return grassVarieties.Count > 0 || shoreVarieties.Count > 0;
        }

        // ------------------------------------------------------------------
        // Spawning (pure functions of world seed + cell coordinates)
        // ------------------------------------------------------------------

        private void SpawnAround(Vector3 playerPosition, IslandWorldGenerator generator)
        {
            int minCellX = Mathf.FloorToInt((playerPosition.x - spawnRadius) / cellSize);
            int maxCellX = Mathf.FloorToInt((playerPosition.x + spawnRadius) / cellSize);
            int minCellZ = Mathf.FloorToInt((playerPosition.z - spawnRadius) / cellSize);
            int maxCellZ = Mathf.FloorToInt((playerPosition.z + spawnRadius) / cellSize);

            for (int cellZ = minCellZ; cellZ <= maxCellZ; cellZ++)
            {
                for (int cellX = minCellX; cellX <= maxCellX; cellX++)
                {
                    var cell = new Vector2Int(cellX, cellZ);
                    if (activeCells.ContainsKey(cell) || barrenCells.Contains(cell))
                        continue;

                    if (!plannedCells.TryGetValue(cell, out PlannedPlant plan))
                    {
                        if (!TryPlanCell(cell, generator, out plan))
                        {
                            barrenCells.Add(cell);
                            continue;
                        }

                        plannedCells.Add(cell, plan);
                    }

                    Spawn(cell, in plan);
                }
            }
        }

        private bool TryPlanCell(Vector2Int cell, IslandWorldGenerator generator, out PlannedPlant plan)
        {
            plan = default;

            if (generator.SampleHash01(cell.x, cell.y, SaltOccupancy) >= foliageDensity)
                return false;

            float worldX = (cell.x + generator.SampleHash01(cell.x, cell.y, SaltJitterX)) * cellSize;
            float worldZ = (cell.y + generator.SampleHash01(cell.x, cell.y, SaltJitterZ)) * cellSize;

            int height = generator.SampleHeight(worldX, worldZ);
            int seaLevel = generator.SeaLevelY;
            int grassMin = seaLevel + generator.BeachBandSize + 1;

            List<FoliageDefinition> varieties;
            float weightTotal;
            if (height >= grassMin)
            {
                varieties = grassVarieties;
                weightTotal = grassWeightTotal;
            }
            else if (height >= seaLevel)
            {
                varieties = shoreVarieties;
                weightTotal = shoreWeightTotal;
            }
            else
            {
                return false; // open water
            }

            if (varieties.Count == 0)
                return false;

            float roll = generator.SampleHash01(cell.x, cell.y, SaltVariety) * weightTotal;
            FoliageDefinition chosen = varieties[varieties.Count - 1];
            for (int i = 0; i < varieties.Count; i++)
            {
                roll -= varieties[i].SpawnWeight;
                if (roll <= 0f)
                {
                    chosen = varieties[i];
                    break;
                }
            }

            plan = new PlannedPlant
            {
                definition = chosen,
                position = new Vector3(worldX, height - groundSink, worldZ),
                yawDegrees = generator.SampleHash01(cell.x, cell.y, SaltYaw) * 360f,
                scale = 0.85f + generator.SampleHash01(cell.x, cell.y, SaltScale) * 0.35f,
            };
            return true;
        }

        private void Spawn(Vector2Int cell, in PlannedPlant plan)
        {
            GameObject instance = null;
            if (pools.TryGetValue(plan.definition.Id, out Stack<GameObject> pool))
            {
                while (pool.Count > 0 && instance == null)
                    instance = pool.Pop(); // destroyed-by-someone entries fall through
            }

            if (instance == null)
                instance = Instantiate(plan.definition.Prefab, transform);

            Transform plant = instance.transform;
            plant.SetPositionAndRotation(plan.position, Quaternion.Euler(0f, plan.yawDegrees, 0f));
            plant.localScale = Vector3.one * plan.scale;
            instance.SetActive(true);

            var harvestable = instance.GetComponent<HarvestableFoliage>();
            if (harvestable != null)
            {
                harvestable.Initialize(plan.definition);
                if (depletedRegrowTimes.TryGetValue(cell, out double regrowAt))
                {
                    harvestable.RestoreDepleted(regrowAt);
                    if (!harvestable.IsDepleted)
                        depletedRegrowTimes.Remove(cell); // regrew while despawned
                }
            }

            activeCells.Add(cell, new ActivePlant
            {
                instance = instance,
                harvestable = harvestable,
                definition = plan.definition,
                position = plan.position,
            });
        }

        // ------------------------------------------------------------------
        // Despawning (state captured, instance pooled)
        // ------------------------------------------------------------------

        private void DespawnBeyond(Vector3 playerPosition)
        {
            float sqrRadius = despawnRadius * despawnRadius;

            despawnBuffer.Clear();
            foreach (KeyValuePair<Vector2Int, ActivePlant> entry in activeCells)
            {
                Vector3 offset = entry.Value.position - playerPosition;
                offset.y = 0f;
                if (offset.sqrMagnitude > sqrRadius)
                    despawnBuffer.Add(entry.Key);
            }

            for (int i = 0; i < despawnBuffer.Count; i++)
            {
                Vector2Int cell = despawnBuffer[i];
                ActivePlant active = activeCells[cell];
                activeCells.Remove(cell);

                if (active.harvestable != null && active.harvestable.IsDepleted)
                    depletedRegrowTimes[cell] = active.harvestable.RegrowAtWorldDays;
                else
                    depletedRegrowTimes.Remove(cell);

                if (active.instance == null)
                    continue; // externally destroyed — nothing to pool

                active.instance.SetActive(false);
                if (!pools.TryGetValue(active.definition.Id, out Stack<GameObject> pool))
                {
                    pool = new Stack<GameObject>();
                    pools.Add(active.definition.Id, pool);
                }

                pool.Push(active.instance);
            }
        }
    }
}
