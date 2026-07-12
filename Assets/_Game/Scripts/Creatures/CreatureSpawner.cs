using System.Collections.Generic;
using IslandGame.Data.Creatures;
using IslandGame.Data.Stats;
using IslandGame.Player;
using IslandGame.Sky;
using IslandGame.Stats;
using UnityEngine;

namespace IslandGame.Creatures
{
    /// <summary>
    /// Maintains a population of one creature species around a point: spawns
    /// over time up to Max Population on walkable ground within the radius,
    /// despawns creatures the player has left far behind, and POOLS instances
    /// (deactivate + reuse, never destroy/instantiate churn) — a spawner's
    /// GameObject count is bounded by its population cap forever.
    ///
    /// Placement: standalone scene object now (manual or the example-spawners
    /// menu); the structures phase (ruins guards) and world-gen phase call
    /// the same public surface — SetDefinition + the spawn cycle.
    ///
    /// PERFORMANCE: logic runs on a 1 s tick, not per frame. Spawn attempts
    /// sample ground through VoxelNavigation (a few block reads each, up to 8
    /// tries) — no physics. Creatures despawn beyond Despawn Distance, which
    /// stays inside the voxel data ring so movers never walk off loaded data.
    ///
    /// DAY/NIGHT (combat phase): subscribes to TimeOfDayController's
    /// OnNightStart/OnDayStart events — never polls the clock. At night the
    /// population cap grows by Night Population Bonus, the spawn interval
    /// scales by Night Spawn Interval Multiplier, and Night Detection Radius
    /// Bonus lands on every living spawnling as a Phase 1 stat MODIFIER
    /// (source = this spawner, so dawn removes exactly these with one
    /// RemoveAllFromSource). Spawn-Only-At-Night spawners go dormant by day
    /// and despawn their brood at first light. Caveat: debug time JUMPS
    /// (SetTimeOfDay) fire no events by design — the next natural threshold
    /// crossing resyncs; F11 fast-forward fires events normally.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CreatureSpawner : MonoBehaviour
    {
        [Tooltip("Species this spawner maintains.")]
        [SerializeField] private CreatureDefinition definition;

        [Tooltip("Maximum simultaneously alive creatures from this spawner.")]
        [Range(1, 20)]
        [SerializeField] private int maxPopulation = 3;

        [Tooltip("Creatures spawn at a random walkable point within this radius, meters. Also the home wander anchor area.")]
        [Min(2f)]
        [SerializeField] private float spawnRadius = 10f;

        [Tooltip("Seconds between spawns while below Max Population (also the respawn delay after deaths/despawns).")]
        [Min(1f)]
        [SerializeField] private float spawnIntervalSeconds = 10f;

        [Tooltip("Creatures farther than this from the PLAYER return to the pool (must stay inside the voxel data ring: renderDistance+1 chunks).")]
        [Min(20f)]
        [SerializeField] private float despawnDistance = 90f;

        [Tooltip("The player must be within this range of the SPAWNER for it to spawn at all — distant spawners stay dormant.")]
        [Min(10f)]
        [SerializeField] private float activationDistance = 80f;

        [Header("Day/Night (driven by TimeOfDayController events)")]
        [Tooltip("Only spawns between OnNightStart and OnDayStart; the brood despawns at first light.")]
        [SerializeField] private bool spawnOnlyAtNight;

        [Tooltip("Extra population allowed at night (hostile pressure after dark).")]
        [Min(0)]
        [SerializeField] private int nightPopulationBonus;

        [Tooltip("Spawn interval multiplier at night (0.5 = twice as fast).")]
        [Range(0.1f, 2f)]
        [SerializeField] private float nightSpawnIntervalMultiplier = 1f;

        [Tooltip("Detection-radius bonus applied to this spawner's living creatures at night, as a fraction (0.5 = +50%). Applied through the stat-modifier system; removed at dawn.")]
        [Range(0f, 2f)]
        [SerializeField] private float nightDetectionRadiusBonus;

        private const float TickInterval = 1f;
        private const int PlacementAttempts = 8;

        private readonly List<Creature> alive = new List<Creature>();
        private readonly Stack<Creature> pool = new Stack<Creature>();

        private PlayerReferences player;
        private TimeOfDayController timeOfDay;
        private float nextTickTime;
        private float nextSpawnTime;
        private bool isNight;

        /// <summary>Currently alive creatures from this spawner (registration order).</summary>
        public IReadOnlyList<Creature> Alive => alive;

        public CreatureDefinition Definition => definition;

        /// <summary>Structures/world-gen phases configure placed spawners through this before first tick.</summary>
        public void SetDefinition(CreatureDefinition creatureDefinition)
        {
            definition = creatureDefinition;
        }

        private void Start()
        {
            timeOfDay = FindFirstObjectByType<TimeOfDayController>();
            if (timeOfDay != null)
            {
                timeOfDay.OnNightStart += OnNightStart;
                timeOfDay.OnDayStart += OnDayStart;
                isNight = timeOfDay.IsNight; // one initial read; events drive every change after
            }
            else if (spawnOnlyAtNight)
            {
                Debug.LogWarning(
                    "[CreatureSpawner] Spawn-Only-At-Night set but no TimeOfDayController in the scene — " +
                    "this spawner will never spawn. Run Island Game/World/Create Day Night Cycle.", this);
            }
        }

        private void OnDestroy()
        {
            if (timeOfDay != null)
            {
                timeOfDay.OnNightStart -= OnNightStart;
                timeOfDay.OnDayStart -= OnDayStart;
            }
        }

        private void OnNightStart()
        {
            isNight = true;

            // Night senses: one Value modifier per living creature, sourced to
            // this spawner so dawn can strip exactly these and nothing else.
            if (nightDetectionRadiusBonus > 0f)
            {
                for (int i = 0; i < alive.Count; i++)
                    ApplyNightModifier(alive[i]);
            }
        }

        private void OnDayStart()
        {
            isNight = false;

            for (int i = 0; i < alive.Count; i++)
            {
                if (alive[i] != null)
                    alive[i].Stats.RemoveAllFromSource(this);
            }

            if (spawnOnlyAtNight)
            {
                // First light clears the night brood (reverse order — Despawn
                // routes through Release, which mutates the alive list).
                for (int i = alive.Count - 1; i >= 0; i--)
                {
                    if (alive[i] != null)
                        alive[i].Despawn();
                }
            }
        }

        private void ApplyNightModifier(Creature creature)
        {
            if (creature == null || nightDetectionRadiusBonus <= 0f)
                return;

            creature.Stats.AddModifier(StatIds.DetectionRadius, new StatModifier(
                this, StatModifierTarget.Value, StatModifierType.PercentMultiplier, nightDetectionRadiusBonus));
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
            if (definition == null || definition.Prefab == null)
                return;

            if (player == null)
            {
                player = FindFirstObjectByType<PlayerReferences>();
                if (player == null)
                    return;
            }

            DespawnFarCreatures();

            float playerDistance = Vector3.Distance(player.transform.position, transform.position);
            if (playerDistance > activationDistance)
                return; // dormant until the player comes near

            if (spawnOnlyAtNight && !isNight)
                return;

            int effectiveMax = maxPopulation + (isNight ? nightPopulationBonus : 0);
            if (alive.Count < effectiveMax && Time.time >= nextSpawnTime)
            {
                if (TrySpawnOne())
                    nextSpawnTime = Time.time + spawnIntervalSeconds * (isNight ? nightSpawnIntervalMultiplier : 1f);
                // Failed placement (chunks not loaded yet, all-water radius):
                // retry next tick without burning the interval.
            }
        }

        private bool TrySpawnOne()
        {
            for (int attempt = 0; attempt < PlacementAttempts; attempt++)
            {
                Vector2 offset = Random.insideUnitCircle * spawnRadius;
                var candidate = new Vector3(transform.position.x + offset.x, transform.position.y, transform.position.z + offset.y);

                // Scan a tall column: the spawner marker floats at an authored
                // height and the island surface is wherever it is.
                if (!VoxelNavigation.TryGetGroundHeight(candidate + Vector3.up * 8f, 2, 60, out float groundY, out bool onWater)
                    || onWater)
                    continue;

                Vector3 spawnPosition = new Vector3(candidate.x, groundY, candidate.z);
                Creature creature = ObtainInstance();
                creature.transform.SetPositionAndRotation(
                    spawnPosition, Quaternion.Euler(0f, Random.value * 360f, 0f));
                creature.gameObject.SetActive(true);
                creature.OwnerSpawner = this;
                creature.Init(definition, spawnPosition);
                alive.Add(creature);

                if (isNight)
                    ApplyNightModifier(creature); // born into the dark, sharper senses

                return true;
            }

            return false;
        }

        private Creature ObtainInstance()
        {
            while (pool.Count > 0)
            {
                Creature pooled = pool.Pop();
                if (pooled != null)
                    return pooled;
            }

            GameObject instance = Instantiate(definition.Prefab, transform);
            var creature = instance.GetComponent<Creature>();
            if (creature == null)
            {
                Debug.LogError(
                    $"[CreatureSpawner] Prefab '{definition.Prefab.name}' has no Creature component — " +
                    "rebuild it with the creature content creator.", this);
                creature = instance.AddComponent<Creature>();
            }

            return creature;
        }

        /// <summary>Returns an instance to the pool (death despawn or out-of-range despawn). Called by Creature.Despawn.</summary>
        public void Release(Creature creature)
        {
            if (creature == null)
                return;

            alive.Remove(creature);
            creature.gameObject.SetActive(false);
            pool.Push(creature);

            // Deaths/despawns respect the spawn cadence — no instant refill.
            nextSpawnTime = Mathf.Max(nextSpawnTime, Time.time + spawnIntervalSeconds);
        }

        private void DespawnFarCreatures()
        {
            Vector3 playerPosition = player.transform.position;
            for (int i = alive.Count - 1; i >= 0; i--)
            {
                Creature creature = alive[i];
                if (creature == null)
                {
                    alive.RemoveAt(i);
                    continue;
                }

                if ((creature.transform.position - playerPosition).sqrMagnitude > despawnDistance * despawnDistance)
                    creature.Despawn(); // routes back through Release
            }
        }

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(0.9f, 0.35f, 0.2f, 0.6f);
            Gizmos.DrawWireSphere(transform.position, spawnRadius);
            Gizmos.color = new Color(0.4f, 0.4f, 0.9f, 0.35f);
            Gizmos.DrawWireSphere(transform.position, activationDistance);
        }
    }
}
