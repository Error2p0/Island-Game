using System;
using IslandGame.Combat;
using IslandGame.Data.Creatures;
using IslandGame.Data.Stats;
using IslandGame.Inventory;
using IslandGame.Stats;
using UnityEngine;

namespace IslandGame.Creatures
{
    /// <summary>
    /// The runtime identity of one spawned creature: owns nothing the systems
    /// don't already provide — stats live in the shared StatContainer
    /// (configured from the CreatureDefinition's species base values), damage
    /// arrives through the existing IDamageable seam and lands on the health
    /// stat, and death is the container's generic OnStatDepleted edge.
    ///
    /// DEATH (combat phase): OnDeath fires exactly once at 0 health, then the
    /// loot table rolls into WorldItems (the exact drop mechanism mining
    /// uses — never a second one), the animator hands the body over to a
    /// simple tip-over death pose (a primitive rig's honest stand-in for a
    /// ragdoll), and after DeathDespawnSeconds the instance despawns back to
    /// its spawner's pool (or destroys when hand-placed).
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(StatContainer))]
    public sealed class Creature : MonoBehaviour, IDamageable
    {
        [Tooltip("Species of a hand-placed creature (spawners pass theirs via Init instead). Init(definition) overrides this.")]
        [SerializeField] private CreatureDefinition definition;

        private const float DeathTipSeconds = 0.5f;

        private StatContainer statContainer;
        private Animator animator;
        private float despawnTime;
        private float deathTime;
        private Quaternion deathBaseRotation;
        private bool initialized;

        /// <summary>This creature's species data.</summary>
        public CreatureDefinition Definition => definition;

        /// <summary>The creature's stats (health, move_speed, detection_radius, ...).</summary>
        public StatContainer Stats
        {
            get
            {
                if (statContainer == null)
                    statContainer = GetComponent<StatContainer>();
                return statContainer;
            }
        }

        /// <summary>Anchor of the wander territory: the spawn point.</summary>
        public Vector3 HomePosition { get; private set; }

        /// <summary>True from health depletion until the body despawns.</summary>
        public bool IsDead { get; private set; }

        /// <summary>The spawner that owns/pools this instance; null for hand-placed creatures.</summary>
        public CreatureSpawner OwnerSpawner { get; set; }

        /// <summary>Fires once at 0 health, before the despawn timer starts. The combat phase's loot/corpse handling subscribes here.</summary>
        public event Action<Creature> OnDeath;

        /// <summary>Fires on every damaging hit while alive — the AI's aggro/flee reactions subscribe here.</summary>
        public event Action<Creature, DamageInfo> OnDamaged;

        private void Awake()
        {
            statContainer = GetComponent<StatContainer>();
            animator = GetComponentInChildren<Animator>();
        }

        private void OnEnable()
        {
            Stats.OnStatDepleted += OnStatDepleted;
        }

        private void OnDisable()
        {
            Stats.OnStatDepleted -= OnStatDepleted;
        }

        private void Start()
        {
            // Hand-placed path: no spawner called Init, so self-initialize
            // from the serialized definition at the current position.
            if (!initialized && definition != null)
                Init(definition, transform.position);
        }

        /// <summary>
        /// (Re)initializes this instance for a species and home point — the
        /// spawner's entry point, valid on fresh instances AND pooled reuse
        /// (ConfigureStats fully resets stats/modifiers; IsDead clears).
        /// </summary>
        public void Init(CreatureDefinition creatureDefinition, Vector3 home)
        {
            definition = creatureDefinition;
            HomePosition = home;
            IsDead = false;
            initialized = true;
            name = $"Creature_{definition.Id}";

            // Pooled reuse after a death: the animator was handed off to the
            // death pose — give it the body back.
            if (animator != null)
                animator.enabled = true;

            var statEntries = definition.Stats;
            var definitions = new StatDefinition[statEntries.Count];
            var baseValues = new float[statEntries.Count];
            for (int i = 0; i < statEntries.Count; i++)
            {
                definitions[i] = statEntries[i].stat;
                baseValues[i] = statEntries[i].baseValue; // 0 = definition default (ConfigureStats convention)
            }

            Stats.ConfigureStats(definitions, baseValues);

            if (!Stats.Has(StatIds.Health))
                Debug.LogWarning(
                    $"[Creature] '{definition.DisplayName}' has no health stat — it cannot die. " +
                    "Add the Health StatDefinition to its stat list.", this);
        }

        /// <summary>Weapon hits and the combat phase's future damage sources land here (existing IDamageable seam).</summary>
        public void ApplyDamage(in DamageInfo damage)
        {
            if (IsDead || !initialized || damage.Amount <= 0f)
                return;

            Stats.Modify(StatIds.Health, -damage.Amount);

            // Depletion → OnStatDepleted → death already ran synchronously
            // above when this hit was lethal; only survivors get the aggro event.
            if (!IsDead)
                OnDamaged?.Invoke(this, damage);
        }

        private void OnStatDepleted(string statId)
        {
            if (statId != StatIds.Health || IsDead || !initialized)
                return;

            IsDead = true;
            deathTime = Time.time;
            deathBaseRotation = transform.rotation;
            despawnTime = Time.time + (definition != null ? definition.DeathDespawnSeconds : 2f);
            Debug.Log($"[Creature] {definition.DisplayName} died.", this);
            OnDeath?.Invoke(this);

            // The animator would keep playing idle over the corpse — hand the
            // body to the scripted tip-over pose instead (primitive rigs have
            // no ragdoll to activate; this is the simple fallback).
            if (animator != null)
                animator.enabled = false;

            RollLoot();
        }

        /// <summary>
        /// Rolls the definition's loot table into WorldItems — the SAME drop
        /// mechanism mined blocks use (WorldItem.Spawn), never a parallel one.
        /// Each line rolls its chance once, then its count range; drops burst
        /// upward with a little spread so multi-line loot doesn't stack
        /// perfectly inside the corpse.
        /// </summary>
        private void RollLoot()
        {
            if (definition == null)
                return;

            var loot = definition.Loot;
            for (int i = 0; i < loot.Count; i++)
            {
                LootTableEntry entry = loot[i];
                if (entry == null || entry.item == null || UnityEngine.Random.value > entry.dropChance)
                    continue;

                int count = UnityEngine.Random.Range(entry.countMin, entry.countMax + 1);
                if (count <= 0)
                    continue;

                Vector2 spread = UnityEngine.Random.insideUnitCircle * 0.4f;
                WorldItem.Spawn(entry.item, count, 1f,
                    transform.position + new Vector3(spread.x, 0.6f, spread.y),
                    new Vector3(spread.x, 2f, spread.y));
            }
        }

        private void Update()
        {
            if (!IsDead)
                return;

            // Tip-over death pose: roll the whole body sideways over half a
            // second. Runs after the animator released the transforms.
            float tip = Mathf.Clamp01((Time.time - deathTime) / DeathTipSeconds);
            transform.rotation = deathBaseRotation * Quaternion.Euler(0f, 0f, 82f * Mathf.SmoothStep(0f, 1f, tip));

            if (Time.time >= despawnTime)
                Despawn();
        }

        /// <summary>Returns to the owning spawner's pool, or destroys a hand-placed creature.</summary>
        public void Despawn()
        {
            if (OwnerSpawner != null)
                OwnerSpawner.Release(this);
            else
                Destroy(gameObject);
        }
    }
}
