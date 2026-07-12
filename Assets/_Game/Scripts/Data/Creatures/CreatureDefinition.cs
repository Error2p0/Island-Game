using System.Collections.Generic;
using IslandGame.Data.Items;
using UnityEngine;

namespace IslandGame.Data.Creatures
{
    /// <summary>
    /// Authored data for one creature species. Pure data, standard pattern:
    /// stable ID + ScriptableObject + CreatureDatabase registry, auto-synced
    /// on import. Runtime behavior lives in Creature/CreatureAI/CreatureMover,
    /// which read these fields; stats flow into the shared StatContainer via
    /// the CreatureStatEntry list (a creature is just another StatContainer
    /// owner). The loot table is authored now and rolled by the combat phase's
    /// death handling.
    /// </summary>
    [CreateAssetMenu(fileName = "NewCreature", menuName = "Island Game/Creature Definition")]
    public sealed class CreatureDefinition : ScriptableObject, IDefinition
    {
        [Header("Identity")]
        [Tooltip("Stable unique ID (lowercase_underscore). Serialized into spawner saves later — never change it once used. Auto-filled from the asset name when empty.")]
        [SerializeField] private string id;

        [SerializeField] private string displayName;

        [TextArea(2, 4)]
        [SerializeField] private string description;

        [Tooltip("Passive flees, Neutral reacts only when attacked, Hostile detects and closes distance.")]
        [SerializeField] private CreatureAggression aggression = CreatureAggression.Passive;

        [Header("Stats (shared StatDefinitions + species base values)")]
        [Tooltip("The stats this species carries (health, move_speed, detection_radius, attack_damage…). Base Value 0 = use the StatDefinition's own default.")]
        [SerializeField] private List<CreatureStatEntry> stats = new List<CreatureStatEntry>();

        [Header("Prefab")]
        [Tooltip("The rigged creature prefab (built by the creature content creator; must carry Creature/StatContainer/CreatureMover/CreatureAI).")]
        [SerializeField] private GameObject prefab;

        [Header("Loot (rolled by the combat phase's death handling)")]
        [SerializeField] private List<LootTableEntry> loot = new List<LootTableEntry>();

        [Header("Behavior Tuning")]
        [Tooltip("Wander destination radius around the creature's home (spawn) point, meters.")]
        [Min(1f)]
        [SerializeField] private float wanderRadius = 12f;

        [Tooltip("Passive only: the player closing within this distance triggers Flee even without an attack.")]
        [Min(0f)]
        [SerializeField] private float fleeTriggerDistance = 6f;

        [Tooltip("Fleeing ends once the player is at least this far away.")]
        [Min(1f)]
        [SerializeField] private float fleeSafeDistance = 25f;

        [Tooltip("Seconds spent in Alert (staring at the player) before the aggression reaction fires.")]
        [Min(0f)]
        [SerializeField] private float alertSeconds = 0.8f;

        [Tooltip("Chase gives up after the player has been outside ~1.6× detection radius for this long, seconds.")]
        [Min(0.5f)]
        [SerializeField] private float loseInterestSeconds = 4f;

        [Tooltip("Chase closes to this distance and holds (the combat phase attacks from here), meters.")]
        [Min(0.5f)]
        [SerializeField] private float approachDistance = 1.6f;

        [Tooltip("Detection additionally requires an unobstructed line from the creature's eye to the player (terrain blocks sight).")]
        [SerializeField] private bool requireLineOfSight = true;

        [Header("Combat (combat phase; damage amount is the attack_damage stat)")]
        [Tooltip("Seconds into the attack animation when the hit lands — the timed hit window.")]
        [Min(0.05f)]
        [SerializeField] private float attackWindupSeconds = 0.35f;

        [Tooltip("Total seconds one attack occupies (windup + recovery) before the creature repositions.")]
        [Min(0.3f)]
        [SerializeField] private float attackCooldownSeconds = 1.4f;

        [Tooltip("Damage classification of this creature's attacks (armor/resistances later).")]
        [SerializeField] private DamageType attackDamageType = DamageType.Blunt;

        [Tooltip("An attacked Neutral creature stays hostile toward the player this long, seconds (refreshes per hit; cleared when the chase is successfully escaped).")]
        [Min(1f)]
        [SerializeField] private float aggroDurationSeconds = 30f;

        [Tooltip("Being attacked alerts same-species creatures within this radius (pack behavior): hostiles/neutrals join the chase, passives scatter. 0 = solitary.")]
        [Min(0f)]
        [SerializeField] private float packAlertRadius;

        [Tooltip("Eye height above the creature root for line-of-sight checks, meters.")]
        [Min(0f)]
        [SerializeField] private float eyeHeight = 0.6f;

        [Tooltip("Seconds the body lingers after death before despawning (the combat phase inserts loot/corpse handling here).")]
        [Min(0f)]
        [SerializeField] private float deathDespawnSeconds = 2.5f;

        public string Id => id;
        public string DisplayName => displayName;
        public string Description => description;
        public CreatureAggression Aggression => aggression;
        public IReadOnlyList<CreatureStatEntry> Stats => stats;
        public GameObject Prefab => prefab;
        public IReadOnlyList<LootTableEntry> Loot => loot;

        public float WanderRadius => wanderRadius;
        public float FleeTriggerDistance => fleeTriggerDistance;
        public float FleeSafeDistance => fleeSafeDistance;
        public float AlertSeconds => alertSeconds;
        public float LoseInterestSeconds => loseInterestSeconds;
        public float ApproachDistance => approachDistance;
        public bool RequireLineOfSight => requireLineOfSight;
        public float EyeHeight => eyeHeight;
        public float DeathDespawnSeconds => deathDespawnSeconds;

        public float AttackWindupSeconds => attackWindupSeconds;
        public float AttackCooldownSeconds => attackCooldownSeconds;
        public DamageType AttackDamageType => attackDamageType;
        public float AggroDurationSeconds => aggroDurationSeconds;
        public float PackAlertRadius => packAlertRadius;

#if UNITY_EDITOR
        private void OnValidate()
        {
            // Convenience only: a fresh asset inherits its name as ID/display name.
            // An existing ID is never regenerated — stability beats tidiness.
            if (string.IsNullOrWhiteSpace(id) && !string.IsNullOrEmpty(name))
                id = name.Trim().ToLowerInvariant().Replace(' ', '_');
            else if (id != null)
                id = id.Trim();

            if (string.IsNullOrWhiteSpace(displayName) && !string.IsNullOrEmpty(name))
                displayName = name;
        }
#endif
    }
}
