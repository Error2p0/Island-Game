using IslandGame.Building;
using IslandGame.Data.Stats;
using IslandGame.Sky;
using IslandGame.Stats;
using UnityEngine;

namespace IslandGame.Player
{
    /// <summary>
    /// The cross-stat survival rules, ALL expressed as stat modifiers so every
    /// effect stacks through the one modifier system and is visible in one
    /// place (StatInstance.Modifiers) when debugging:
    ///
    ///   WELL FED  — hunger AND thirst above the threshold grants health regen
    ///     (health's base regen is 0; food is the only passive heal today).
    ///   STARVING/DEHYDRATED — hunger OR thirst empty cuts stamina regen by
    ///     the authored fraction (health regen needs Well Fed, so it is
    ///     already gone by this point).
    ///   NIGHT     — a negative warmth-regen modifier flips warmth's daytime
    ///     recovery into a drain (TimeOfDayController is polled via IsNight
    ///     once per slow tick instead of subscribing to phase events: a debug
    ///     time-scrub fires no events by design, and polling one bool 4×/s is
    ///     cheaper than event bookkeeping and can never desync).
    ///   CAMPFIRE  — a lit campfire in range adds warmth regen that beats the
    ///     night drain; campfires register themselves in
    ///     CampfireBehavior.ActiveCampfires, so this is a scan over a handful
    ///     of fires, not a physics query.
    ///   COLD      — warmth under the cold threshold multiplies health regen
    ///     to zero (freezing people don't heal).
    ///   FREEZING  — warmth at zero applies direct health damage per second
    ///     (deliberately damage, not a regen modifier, so the COLD ×0 regen
    ///     multiplier cannot cancel it).
    ///
    /// State transitions swap modifiers on/off; nothing is re-applied per
    /// frame. The slow tick (default 0.25 s) is plenty for survival pacing.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(StatContainer))]
    public sealed class PlayerSurvival : MonoBehaviour
    {
        [Header("Well Fed (hunger + thirst high → health regen)")]
        [Tooltip("Hunger AND thirst must be at or above this fraction of full to grant health regen.")]
        [Range(0.5f, 1f)]
        [SerializeField] private float wellFedThreshold01 = 0.85f;

        [Tooltip("Health regenerated per second while Well Fed.")]
        [Min(0f)]
        [SerializeField] private float wellFedHealthRegenPerSecond = 1f;

        [Header("Starving / Dehydrated (either empty → weakened)")]
        [Tooltip("Fraction of stamina regen REMOVED while hunger or thirst is empty (0.75 = regen drops to 25%).")]
        [Range(0f, 1f)]
        [SerializeField] private float emptyStaminaRegenPenalty = 0.75f;

        [Header("Warmth — Night")]
        [Tooltip("Warmth-regen reduction applied at night. Must exceed the warmth stat's authored daytime regen for nights to actually get cold.")]
        [Min(0f)]
        [SerializeField] private float nightWarmthRegenReduction = 0.85f;

        [Header("Warmth — Campfire")]
        [Tooltip("Warmth regen added while within range of a lit campfire.")]
        [Min(0f)]
        [SerializeField] private float campfireWarmthRegenPerSecond = 2.5f;

        [Tooltip("Distance to a lit campfire that counts as 'warming up', meters.")]
        [Min(0.5f)]
        [SerializeField] private float campfireWarmthRadius = 5f;

        [Header("Cold / Freezing")]
        [Tooltip("Below this fraction of full warmth, health regen is multiplied to zero.")]
        [Range(0f, 0.5f)]
        [SerializeField] private float coldThreshold01 = 0.25f;

        [Tooltip("Health lost per second while warmth is fully depleted.")]
        [Min(0f)]
        [SerializeField] private float freezingHealthDamagePerSecond = 0.75f;

        [Header("Ticking")]
        [Tooltip("Seconds between condition re-evaluations. Freezing damage still applies every frame for smoothness.")]
        [Range(0.05f, 1f)]
        [SerializeField] private float conditionCheckInterval = 0.25f;

        private StatContainer statContainer;
        private TimeOfDayController timeOfDay;
        private float nextCheckTime;

        // One StatModifier instance per rule, created once and swapped in/out
        // on state transitions — never re-allocated, never re-applied per frame.
        private StatModifier wellFedHealthRegen;
        private StatModifier emptyStaminaPenalty;
        private StatModifier nightWarmthDrain;
        private StatModifier campfireWarmth;
        private StatModifier coldHealthRegenBlock;

        private bool isWellFed;
        private bool isStarvingOrDehydrated;
        private bool isNight;
        private bool isNearLitCampfire;
        private bool isCold;

        /// <summary>True while a lit campfire is warming the player (HUD/audio hooks later).</summary>
        public bool IsNearLitCampfire => isNearLitCampfire;

        /// <summary>True while warmth is fully depleted and health is draining.</summary>
        public bool IsFreezing { get; private set; }

        private void Awake()
        {
            statContainer = GetComponent<StatContainer>();

            wellFedHealthRegen = new StatModifier(
                this, StatModifierTarget.RegenRate, StatModifierType.Flat, wellFedHealthRegenPerSecond);
            emptyStaminaPenalty = new StatModifier(
                this, StatModifierTarget.RegenRate, StatModifierType.PercentMultiplier, -emptyStaminaRegenPenalty);
            nightWarmthDrain = new StatModifier(
                this, StatModifierTarget.RegenRate, StatModifierType.Flat, -nightWarmthRegenReduction);
            campfireWarmth = new StatModifier(
                this, StatModifierTarget.RegenRate, StatModifierType.Flat, campfireWarmthRegenPerSecond);
            coldHealthRegenBlock = new StatModifier(
                this, StatModifierTarget.RegenRate, StatModifierType.PercentMultiplier, -1f);
        }

        private void Start()
        {
            timeOfDay = FindFirstObjectByType<TimeOfDayController>();
            if (timeOfDay == null)
                Debug.LogWarning(
                    "[PlayerSurvival] No TimeOfDayController in the scene — warmth will never drain at night. " +
                    "Run Island Game/World/Create Day Night Cycle.", this);
        }

        private void OnDisable()
        {
            // Clean teardown: every rule this component applied disappears with it.
            statContainer.RemoveAllFromSource(this);
            isWellFed = isStarvingOrDehydrated = isNight = isNearLitCampfire = isCold = false;
            IsFreezing = false;
        }

        private void Update()
        {
            if (Time.time >= nextCheckTime)
            {
                nextCheckTime = Time.time + conditionCheckInterval;
                EvaluateConditions();
            }

            // Freezing damage is per-frame for a smooth drain; the flag itself
            // only flips on the slow tick, which is fine at survival pacing.
            if (IsFreezing)
                statContainer.Modify(StatIds.Health, -freezingHealthDamagePerSecond * Time.deltaTime);
        }

        private void EvaluateConditions()
        {
            float hunger01 = statContainer.GetNormalized(StatIds.Hunger, 1f);
            float thirst01 = statContainer.GetNormalized(StatIds.Thirst, 1f);
            float warmth01 = statContainer.GetNormalized(StatIds.Warmth, 1f);

            SetRuleActive(ref isWellFed,
                hunger01 >= wellFedThreshold01 && thirst01 >= wellFedThreshold01,
                StatIds.Health, wellFedHealthRegen);

            SetRuleActive(ref isStarvingOrDehydrated,
                hunger01 <= 0f || thirst01 <= 0f,
                StatIds.Stamina, emptyStaminaPenalty);

            SetRuleActive(ref isNight,
                timeOfDay != null && timeOfDay.IsNight,
                StatIds.Warmth, nightWarmthDrain);

            SetRuleActive(ref isNearLitCampfire,
                AnyLitCampfireInRange(),
                StatIds.Warmth, campfireWarmth);

            SetRuleActive(ref isCold,
                warmth01 < coldThreshold01,
                StatIds.Health, coldHealthRegenBlock);

            IsFreezing = warmth01 <= 0f;
        }

        /// <summary>Applies/removes one rule's modifier exactly on the state edge.</summary>
        private void SetRuleActive(ref bool active, bool shouldBeActive, string statId, StatModifier modifier)
        {
            if (active == shouldBeActive)
                return;

            active = shouldBeActive;
            if (shouldBeActive)
                statContainer.AddModifier(statId, modifier);
            else
                statContainer.RemoveModifier(statId, modifier);
        }

        private bool AnyLitCampfireInRange()
        {
            var campfires = CampfireBehavior.ActiveCampfires;
            float radiusSqr = campfireWarmthRadius * campfireWarmthRadius;
            Vector3 position = transform.position;

            for (int i = 0; i < campfires.Count; i++)
            {
                CampfireBehavior campfire = campfires[i];
                if (campfire != null && campfire.IsLit
                    && (campfire.transform.position - position).sqrMagnitude <= radiusSqr)
                    return true;
            }

            return false;
        }
    }
}
