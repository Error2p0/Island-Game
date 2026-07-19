using System.Collections.Generic;
using IslandGame.Building;
using IslandGame.Combat;
using IslandGame.Data.Creatures;
using IslandGame.Data.Stats;
using IslandGame.Inventory;
using IslandGame.Player;
using UnityEngine;

namespace IslandGame.Creatures
{
    /// <summary>
    /// The base creature state machine (Idle / Wander / Alert / Flee / Chase),
    /// driving CreatureMover and reading tuning from the CreatureDefinition
    /// and live values from the StatContainer (move_speed, detection_radius —
    /// so future buffs/debuffs genuinely change behavior).
    ///
    /// AGGRESSION TABLE (EffectiveAggression — the authored value, except an
    /// attacked Neutral counts as Hostile for AggroDurationSeconds):
    ///   Passive — detection → Alert (freeze and stare); player closing within
    ///     FleeTriggerDistance OR any damage → Flee until FleeSafeDistance.
    ///   Neutral — ignores detection entirely; damage → aggro + Chase, and it
    ///     REMEMBERS: re-detection during the aggro window re-chases; the
    ///     grudge clears only when the chase is successfully escaped, or on
    ///     death.
    ///   Hostile — detection → Alert → Chase → Attack at ApproachDistance:
    ///     windup → timed hit window (damage via the player's IDamageable,
    ///     the same interface player weapons use — symmetric damage flow) →
    ///     recovery → a short sidestep reposition → Chase again. Gives up
    ///     after the player stays beyond ~1.6× detection radius for
    ///     LoseInterestSeconds, walks home.
    ///
    /// PACK BEHAVIOR: damaging a creature alerts same-species creatures
    /// within PackAlertRadius through a static live-instance registry (the
    /// campfire-registry pattern — a list scan, no physics): hostiles and
    /// neutrals join the aggro, passives scatter. Deliberately simple — one
    /// broadcast on damage, no squad coordination.
    ///
    /// PERFORMANCE: detection (distance + optional line-of-sight ray) runs on
    /// a 0.2 s tick with a random per-creature phase so a herd never checks
    /// on the same frame; state logic itself is trivial per-frame math. The
    /// player is resolved once through a static cache shared by all creatures.
    ///
    /// TAMED BRANCH (taming phase — a mode ON TOP of the machine, not a new
    /// one): CreatureTaming flips Creature.IsTamed and calls EnterTamedMode;
    /// from then on the wild reactions are gated off (no detection reactions,
    /// no flee, no grudges, no pack alerts sent OR received) and the machine
    /// runs TamedFollow / TamedStay. Assist reuses the EXISTING Chase/Attack
    /// states verbatim through a combat-target seam (TryGetCombatTarget):
    /// wild creatures target the player as always; a tamed assist targets a
    /// hostile creature engaged with the player — one attack resolution, two
    /// targets. Also in this phase: a wild tameable creature is LURED by its
    /// favorite food (proximity flee suppressed while the player brandishes
    /// it — walking up to feed a deer must be possible; damage still flees).
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Creature))]
    [RequireComponent(typeof(CreatureMover))]
    public sealed class CreatureAI : MonoBehaviour
    {
        private const float DetectionInterval = 0.2f;
        private const float ChaseGiveUpRadiusMultiplier = 1.6f;
        private const float WanderSpeedFraction = 0.45f;

        // Taming phase tuning (behavior constants; per-species numbers live
        // on the definition).
        private const float TamedFollowRunDistance = 8f;
        private const float TamedTeleportDistance = 45f;
        private const float TamedStayTolerance = 2.5f;
        private const float AssistEngageRadius = 14f;
        private const float AssistLeashRadius = 20f;
        private const float AssistScanInterval = 0.5f;
        private const float FoodLureRadius = 12f;

        private static readonly int Speed01Hash = Animator.StringToHash("Speed01");
        private static readonly int AttackHash = Animator.StringToHash("Attack");
        private static PlayerReferences cachedPlayer;

        // Live-instance registry for pack alerts (campfire-registry pattern).
        private static readonly List<CreatureAI> activeAIs = new List<CreatureAI>();

        private Creature creature;
        private CreatureMover mover;
        private Animator animator;

        private CreatureAIState state = CreatureAIState.Idle;
        private float stateTimer;
        private float idleWaitSeconds;
        private float nextDetectionTime;
        private float lostTargetTimer;
        private float nextFleeRepickTime;
        private bool playerDetected;

        // Combat phase state.
        private float aggroUntil;
        private bool attackHitResolved;
        private float repositionUntil;
        private Vector3 repositionTarget;
        private int strafeSign = 1;

        // Taming phase state.
        private CompanionMode companionMode = CompanionMode.Follow;
        private Vector3 stayPosition;
        private Creature assistTarget;
        private float nextAssistScanTime;
        private static HotbarSelector cachedSelector;

        private readonly RaycastHit[] sightBuffer = new RaycastHit[8];
        private readonly RaycastHit[] strikeBuffer = new RaycastHit[8];

        /// <summary>Current state, exposed for debugging/gizmos and the combat phase's attack layer.</summary>
        public CreatureAIState State => state;

        /// <summary>The tamed command mode (meaningful only while the creature is tamed). Persisted by the save system.</summary>
        public CompanionMode CompanionMode => companionMode;

        /// <summary>
        /// Taming API: switches into (or between) the tamed modes. Called by
        /// CreatureTaming on tame, command cycle and save restore. Clears
        /// every wild leftover (grudge, targets) so the transition is total.
        /// </summary>
        public void EnterTamedMode(CompanionMode mode)
        {
            companionMode = mode;
            stayPosition = transform.position;
            aggroUntil = 0f;
            assistTarget = null;
            lostTargetTimer = 0f;
            repositionUntil = 0f;
            mover.ClearTarget();
            state = mode == CompanionMode.Stay ? CreatureAIState.TamedStay : CreatureAIState.TamedFollow;
            stateTimer = 0f;
        }

        private static PlayerReferences Player
        {
            get
            {
                if (cachedPlayer == null)
                    cachedPlayer = FindFirstObjectByType<PlayerReferences>();
                return cachedPlayer;
            }
        }

        private CreatureDefinition Definition => creature.Definition;

        private float MoveSpeed => creature.Stats.GetValue(StatIds.MoveSpeed, 3.5f);
        private float DetectionRadius => creature.Stats.GetValue(StatIds.DetectionRadius, 12f);
        private float AttackDamage => creature.Stats.GetValue(StatIds.AttackDamage, 5f);

        /// <summary>The authored aggression, except an attacked Neutral counts as Hostile while its grudge lasts.</summary>
        private CreatureAggression EffectiveAggression =>
            Definition.Aggression == CreatureAggression.Neutral && Time.time < aggroUntil
                ? CreatureAggression.Hostile
                : Definition.Aggression;

        private void Awake()
        {
            creature = GetComponent<Creature>();
            mover = GetComponent<CreatureMover>();
            animator = GetComponentInChildren<Animator>();

            // Random phase so a group of creatures staggers its detection ticks.
            nextDetectionTime = Time.time + Random.value * DetectionInterval;
        }

        private void OnEnable()
        {
            creature.OnDamaged += OnDamaged;
            activeAIs.Add(this);
            aggroUntil = 0f; // pooled reuse: a fresh body holds no grudge
            EnterIdle();
        }

        private void OnDisable()
        {
            creature.OnDamaged -= OnDamaged;
            activeAIs.Remove(this);
            mover.ClearTarget();
        }

        private void Update()
        {
            if (creature.IsDead || Definition == null)
            {
                mover.ClearTarget();
                UpdateAnimator();
                return;
            }

            // Tamed companions don't watch the player as a threat — skip the
            // detection tick entirely (they also can't be in wild states, but
            // a pooled OnEnable lands in Idle before RestoreTamed runs, so
            // route stray wild states back to the tamed branch defensively).
            if (creature.IsTamed)
            {
                playerDetected = false;
                if (state != CreatureAIState.TamedFollow && state != CreatureAIState.TamedStay
                    && state != CreatureAIState.Chase && state != CreatureAIState.Attack)
                    EnterTamedMode(companionMode);
            }
            else if (Time.time >= nextDetectionTime)
            {
                nextDetectionTime = Time.time + DetectionInterval;
                playerDetected = DetectPlayer();
            }

            stateTimer += Time.deltaTime;

            switch (state)
            {
                case CreatureAIState.Idle: TickIdle(); break;
                case CreatureAIState.Wander: TickWander(); break;
                case CreatureAIState.Alert: TickAlert(); break;
                case CreatureAIState.Flee: TickFlee(); break;
                case CreatureAIState.Chase: TickChase(); break;
                case CreatureAIState.Attack: TickAttack(); break;
                case CreatureAIState.TamedFollow: TickTamedFollow(); break;
                case CreatureAIState.TamedStay: TickTamedStay(); break;
            }

            UpdateAnimator();
        }

        // ------------------------------------------------------------------
        // States
        // ------------------------------------------------------------------

        private void EnterIdle()
        {
            state = CreatureAIState.Idle;
            stateTimer = 0f;
            idleWaitSeconds = Random.Range(2f, 5f);
            mover.ClearTarget();
        }

        private void TickIdle()
        {
            if (ReactToDetection())
                return;

            if (stateTimer >= idleWaitSeconds)
                EnterWander();
        }

        private void EnterWander()
        {
            state = CreatureAIState.Wander;
            stateTimer = 0f;

            // Random point in the home territory; the mover's walkability
            // probes handle any unlucky pick (water/cliff) via stuck→repick.
            Vector2 offset = Random.insideUnitCircle * Definition.WanderRadius;
            Vector3 destination = creature.HomePosition + new Vector3(offset.x, 0f, offset.y);
            mover.SetTarget(destination, MoveSpeed * WanderSpeedFraction);
        }

        private void TickWander()
        {
            if (ReactToDetection())
                return;

            if (mover.HasArrived || mover.IsStuck || stateTimer > 20f)
                EnterIdle();
        }

        private void EnterAlert()
        {
            state = CreatureAIState.Alert;
            stateTimer = 0f;
            mover.ClearTarget();
        }

        private void TickAlert()
        {
            PlayerReferences player = Player;
            if (player == null)
            {
                EnterIdle();
                return;
            }

            mover.FaceTowards(player.transform.position, Time.deltaTime);

            if (!playerDetected)
            {
                EnterIdle();
                return;
            }

            if (stateTimer < Definition.AlertSeconds)
                return;

            switch (EffectiveAggression)
            {
                case CreatureAggression.Hostile:
                    EnterChase();
                    break;

                case CreatureAggression.Passive:
                    // Wary freeze: keep staring; bolt when the player crowds
                    // in — unless lured (taming phase): a brandished favorite
                    // food reads as safe, which is what makes walking up to
                    // feed a skittish species possible. Damage still flees.
                    float distance = Vector3.Distance(transform.position, player.transform.position);
                    if (distance <= Definition.FleeTriggerDistance && !IsLuredByFood(player))
                        EnterFlee();
                    break;

                default:
                    EnterIdle(); // Neutral never Alerts from detection; safety fallback
                    break;
            }
        }

        private void EnterFlee()
        {
            state = CreatureAIState.Flee;
            stateTimer = 0f;
            nextFleeRepickTime = 0f;
        }

        private void TickFlee()
        {
            PlayerReferences player = Player;
            if (player == null)
            {
                EnterIdle();
                return;
            }

            Vector3 fromPlayer = transform.position - player.transform.position;
            fromPlayer.y = 0f;
            float distance = fromPlayer.magnitude;

            if (distance >= Definition.FleeSafeDistance)
            {
                EnterIdle();
                return;
            }

            // Repick the escape point on a short cadence (the player moves);
            // when the straight-away line is blocked, angle the escape so the
            // creature skirts obstacles instead of pinning itself against them.
            if (Time.time >= nextFleeRepickTime || mover.IsStuck)
            {
                nextFleeRepickTime = Time.time + 0.5f;
                Vector3 away = distance > 0.01f ? fromPlayer / distance : transform.forward;
                if (mover.IsStuck)
                    away = Quaternion.Euler(0f, Random.value < 0.5f ? 70f : -70f, 0f) * away;

                mover.SetTarget(transform.position + away * 8f, MoveSpeed);
            }
        }

        private void EnterChase()
        {
            state = CreatureAIState.Chase;
            stateTimer = 0f;
            lostTargetTimer = 0f;
        }

        private void TickChase()
        {
            // The combat-target seam (taming phase): the player for wild
            // creatures, the assist target for a tamed companion — every
            // line below is target-agnostic.
            if (!TryGetCombatTarget(out Vector3 targetPosition, out _))
            {
                ExitCombat();
                return;
            }

            float distance = Vector3.Distance(transform.position, targetPosition);

            if (creature.IsTamed)
            {
                // Companion leash: never chase a target that has left the
                // player's fight — back to station instead of across the map.
                PlayerReferences player = Player;
                if (player == null
                    || (targetPosition - player.transform.position).sqrMagnitude > AssistLeashRadius * AssistLeashRadius)
                {
                    ExitCombat();
                    return;
                }
            }
            else
            {
                // Give up after the player stays out of extended range long enough.
                if (distance > DetectionRadius * ChaseGiveUpRadiusMultiplier)
                {
                    lostTargetTimer += Time.deltaTime;
                    if (lostTargetTimer >= Definition.LoseInterestSeconds)
                    {
                        // Successful escape: a Neutral's grudge ends here (the
                        // "flees successfully" clause); walk home rather than
                        // idling wherever the chase died.
                        aggroUntil = 0f;
                        state = CreatureAIState.Wander;
                        stateTimer = 0f;
                        mover.SetTarget(creature.HomePosition, MoveSpeed * WanderSpeedFraction);
                        return;
                    }
                }
                else
                {
                    lostTargetTimer = 0f;
                }
            }

            // Post-attack reposition: a short sidestep before closing again,
            // so combat reads as attack → circle → attack, not a static grind.
            if (Time.time < repositionUntil)
            {
                mover.SetTarget(repositionTarget, MoveSpeed * 0.8f);
                if (mover.HasArrived || mover.IsStuck)
                    repositionUntil = 0f;
                return;
            }

            if (distance <= Definition.ApproachDistance)
            {
                EnterAttack();
            }
            else
            {
                mover.SetTarget(targetPosition, MoveSpeed);
            }
        }

        private void EnterAttack()
        {
            state = CreatureAIState.Attack;
            stateTimer = 0f;
            attackHitResolved = false;
            mover.ClearTarget();

            if (animator != null)
                animator.SetTrigger(AttackHash);
        }

        private void TickAttack()
        {
            // Same combat-target seam as Chase: ONE attack resolution serves
            // wild-vs-player and companion-vs-hostile alike (taming phase).
            if (!TryGetCombatTarget(out Vector3 targetPosition, out IDamageable damageable))
            {
                ExitCombat();
                return;
            }

            mover.FaceTowards(targetPosition, Time.deltaTime);

            // The timed hit window: damage lands at the windup point of the
            // attack animation, and only if the target is STILL in range —
            // backpedaling out of a windup dodges the hit.
            if (!attackHitResolved && stateTimer >= Definition.AttackWindupSeconds)
            {
                attackHitResolved = true;

                float hitRange = Definition.ApproachDistance + 0.75f;
                if (damageable != null
                    && (targetPosition - transform.position).sqrMagnitude <= hitRange * hitRange)
                {
                    Vector3 hitPoint = targetPosition + Vector3.up * (creature.IsTamed ? 0.6f : 1.1f);
                    Vector3 direction = (targetPosition - transform.position).normalized;

                    // A building piece standing in the strike line absorbs the
                    // hit: creatures damage structures instead of biting their
                    // target through a wall.
                    if (TryGetBlockingPiece(hitPoint, damageable, out BuildingPiece blocker, out Vector3 blockPoint))
                    {
                        damageable = blocker;
                        hitPoint = blockPoint;
                    }

                    var info = new DamageInfo(
                        AttackDamage, Definition.AttackDamageType, hitPoint, direction, gameObject);
                    damageable.ApplyDamage(in info);
                }
            }

            // Recovery over: sidestep to a flanking point, then Chase closes
            // back in for the next swing.
            if (stateTimer >= Definition.AttackCooldownSeconds)
            {
                strafeSign = -strafeSign;
                Vector3 fromTarget = transform.position - targetPosition;
                fromTarget.y = 0f;
                Vector3 away = fromTarget.sqrMagnitude > 0.01f ? fromTarget.normalized : -transform.forward;
                Vector3 side = Vector3.Cross(Vector3.up, away) * strafeSign;

                repositionTarget = targetPosition + (away + side * 1.2f).normalized * (Definition.ApproachDistance + 1.2f);
                repositionUntil = Time.time + 0.7f;
                EnterChase();
            }
        }

        /// <summary>
        /// Nearest building piece crossing the strike line to the target, if
        /// any — checked only at the moment a hit lands. The creature's own
        /// colliders and the target's never count as blockers; any other
        /// nearest obstacle that is NOT a piece leaves the hit unredirected
        /// (exactly the pre-existing behavior, so terrain lips can't eat hits).
        /// </summary>
        private bool TryGetBlockingPiece(Vector3 hitPoint, IDamageable target, out BuildingPiece piece, out Vector3 point)
        {
            piece = null;
            point = default;

            Vector3 origin = transform.position + Vector3.up * 0.5f;
            Vector3 toHit = hitPoint - origin;
            float distance = toHit.magnitude;
            if (distance < 0.001f)
                return false;

            Transform targetTransform = target is Component targetComponent ? targetComponent.transform : null;

            int count = Physics.RaycastNonAlloc(
                origin, toHit / distance, strikeBuffer, distance, ~0, QueryTriggerInteraction.Ignore);

            int best = -1;
            for (int i = 0; i < count; i++)
            {
                Transform hitTransform = strikeBuffer[i].collider.transform;
                if (hitTransform.IsChildOf(transform))
                    continue;
                if (targetTransform != null && hitTransform.IsChildOf(targetTransform))
                    continue;
                if (best < 0 || strikeBuffer[i].distance < strikeBuffer[best].distance)
                    best = i;
            }

            if (best < 0)
                return false;

            piece = strikeBuffer[best].collider.GetComponentInParent<BuildingPiece>();
            if (piece == null)
                return false;

            point = strikeBuffer[best].point;
            return true;
        }

        /// <summary>
        /// The combat-target seam (taming phase): wild = the player (exactly
        /// the pre-taming behavior), tamed = the current assist target. False
        /// means combat has nothing to fight — callers ExitCombat.
        /// </summary>
        private bool TryGetCombatTarget(out Vector3 position, out IDamageable damageable)
        {
            if (creature.IsTamed)
            {
                if (assistTarget != null && !assistTarget.IsDead)
                {
                    position = assistTarget.transform.position;
                    damageable = assistTarget;
                    return true;
                }

                position = default;
                damageable = null;
                return false;
            }

            PlayerReferences player = Player;
            if (player == null)
            {
                position = default;
                damageable = null;
                return false;
            }

            position = player.transform.position;
            damageable = player.GetComponent<IDamageable>();
            return true;
        }

        /// <summary>Combat over/unwinnable: a companion returns to its commanded station, a wild creature to Idle.</summary>
        private void ExitCombat()
        {
            assistTarget = null;
            if (creature.IsTamed)
                EnterTamedMode(companionMode);
            else
                EnterIdle();
        }

        // ------------------------------------------------------------------
        // Tamed branch (taming phase)
        // ------------------------------------------------------------------

        private void TickTamedFollow()
        {
            PlayerReferences player = Player;
            if (player == null)
            {
                mover.ClearTarget();
                return;
            }

            Vector3 playerPosition = player.transform.position;
            float distance = Vector3.Distance(transform.position, playerPosition);

            // Catch-up teleport: a hopelessly dropped companion (cliffs,
            // water, mining tunnels) pops back to the player's side rather
            // than being lost — the reference games all do this.
            if (distance > TamedTeleportDistance)
            {
                TeleportBesidePlayer(playerPosition);
                return;
            }

            // Assist: engage hostiles that are fighting the player. Chase/
            // Attack then run with the assist target through the combat seam.
            if (companionMode == CompanionMode.Assist && Time.time >= nextAssistScanTime)
            {
                nextAssistScanTime = Time.time + AssistScanInterval;
                Creature threat = FindAssistTarget(playerPosition);
                if (threat != null)
                {
                    assistTarget = threat;
                    EnterChase();
                    return;
                }
            }

            if (distance > Definition.FollowDistance)
            {
                mover.SetTarget(playerPosition,
                    distance > TamedFollowRunDistance ? MoveSpeed : MoveSpeed * 0.6f);
            }
            else
            {
                mover.ClearTarget();
                mover.FaceTowards(playerPosition, Time.deltaTime);
            }
        }

        private void TickTamedStay()
        {
            // Hold the commanded spot; walk back when shoved off it. No
            // assist engagement from Stay — stay means stay.
            float drift = Vector3.Distance(transform.position, stayPosition);
            if (drift > TamedStayTolerance)
                mover.SetTarget(stayPosition, MoveSpeed * 0.5f);
            else if (mover.HasArrived || drift < 0.8f)
                mover.ClearTarget();
        }

        /// <summary>Nearest living wild creature currently in Chase/Attack (i.e. fighting the player) within engage range of the player.</summary>
        private Creature FindAssistTarget(Vector3 playerPosition)
        {
            float bestSqr = AssistEngageRadius * AssistEngageRadius;
            Creature best = null;

            for (int i = 0; i < activeAIs.Count; i++)
            {
                CreatureAI other = activeAIs[i];
                if (other == this || other.creature.IsTamed || other.creature.IsDead)
                    continue;

                if (other.state != CreatureAIState.Chase && other.state != CreatureAIState.Attack)
                    continue;

                float sqr = (other.transform.position - playerPosition).sqrMagnitude;
                if (sqr <= bestSqr)
                {
                    bestSqr = sqr;
                    best = other.creature;
                }
            }

            return best;
        }

        private void TeleportBesidePlayer(Vector3 playerPosition)
        {
            Vector2 offset = Random.insideUnitCircle.normalized * 2.5f;
            Vector3 candidate = playerPosition + new Vector3(offset.x, 0f, offset.y);

            // Ground through the same voxel sampling the spawner uses; water
            // or missing data just skips — the walk continues and the next
            // frame retries with a fresh offset.
            if (VoxelNavigation.TryGetGroundHeight(candidate + Vector3.up * 8f, 2, 60, out float groundY, out bool onWater)
                && !onWater)
            {
                mover.ClearTarget();
                transform.position = new Vector3(candidate.x, groundY, candidate.z);
            }
        }

        // ------------------------------------------------------------------
        // Detection & reactions
        // ------------------------------------------------------------------

        /// <summary>Routes a fresh detection per EFFECTIVE aggression (an aggroed Neutral re-chases). Returns true when the state changed.</summary>
        private bool ReactToDetection()
        {
            if (creature.IsTamed || !playerDetected)
                return false;

            switch (EffectiveAggression)
            {
                case CreatureAggression.Hostile:
                case CreatureAggression.Passive:
                    EnterAlert();
                    return true;

                default:
                    return false; // un-aggroed Neutral ignores the player
            }
        }

        private void OnDamaged(Creature damagedCreature, DamageInfo damage)
        {
            // Tamed companions never flee, never grudge the player and never
            // pack-alert. In Assist mode they retaliate against a wild
            // creature that hurt them (self-defense through the same combat
            // seam); Follow/Stay stoically hold their orders.
            if (creature.IsTamed)
            {
                if (companionMode == CompanionMode.Assist && damage.Source != null)
                {
                    var attacker = damage.Source.GetComponent<Creature>();
                    if (attacker != null && !attacker.IsTamed && !attacker.IsDead)
                    {
                        assistTarget = attacker;
                        EnterChase();
                    }
                }

                return;
            }

            ReactToThreat();
            BroadcastPackAlert();
        }

        /// <summary>The being-attacked reaction, shared by direct damage and pack alerts.</summary>
        private void ReactToThreat()
        {
            switch (Definition.Aggression)
            {
                case CreatureAggression.Passive:
                    if (state != CreatureAIState.Flee)
                        EnterFlee();
                    break;

                case CreatureAggression.Neutral:
                case CreatureAggression.Hostile:
                    // The grudge: refreshed per hit, outlives losing sight of
                    // the player, cleared only by successful escape or death.
                    aggroUntil = Time.time + Definition.AggroDurationSeconds;
                    if (state != CreatureAIState.Chase && state != CreatureAIState.Attack)
                        EnterChase();
                    break;
            }
        }

        /// <summary>
        /// Pack behavior: alert same-species creatures in range. A registry
        /// scan (dozens of entries), deliberately not a physics query and
        /// deliberately simple — one broadcast, no chained re-broadcasts
        /// (alerted members don't alert others, so herds can't cascade
        /// world-wide).
        /// </summary>
        private void BroadcastPackAlert()
        {
            float radius = Definition.PackAlertRadius;
            if (radius <= 0f)
                return;

            float radiusSqr = radius * radius;
            for (int i = 0; i < activeAIs.Count; i++)
            {
                CreatureAI other = activeAIs[i];
                if (other == this || other.creature.IsDead || other.Definition != Definition)
                    continue;

                if (other.creature.IsTamed)
                    continue; // a tamed pack-mate ignores its wild kin's alarm

                if ((other.transform.position - transform.position).sqrMagnitude <= radiusSqr)
                    other.ReactToThreat();
            }
        }

        /// <summary>Taming: true while the player brandishes this species' favorite food within lure range (suppresses the proximity flee).</summary>
        private bool IsLuredByFood(PlayerReferences player)
        {
            if (!Definition.Tameable)
                return false;

            if ((player.transform.position - transform.position).sqrMagnitude > FoodLureRadius * FoodLureRadius)
                return false;

            if (cachedSelector == null)
                cachedSelector = player.GetComponent<HotbarSelector>();

            return cachedSelector != null && Definition.IsFavoriteFood(cachedSelector.EquippedItem);
        }

        private bool DetectPlayer()
        {
            PlayerReferences player = Player;
            if (player == null)
                return false;

            Vector3 eye = transform.position + Vector3.up * Definition.EyeHeight;
            Vector3 playerChest = player.transform.position + Vector3.up * 1.2f;

            if ((playerChest - eye).sqrMagnitude > DetectionRadius * DetectionRadius)
                return false;

            if (!Definition.RequireLineOfSight)
                return true;

            // First non-self hit along the sight line decides: the player =
            // seen, anything else (terrain, buildings) = blocked.
            Vector3 toPlayer = playerChest - eye;
            float sightDistance = toPlayer.magnitude;
            int hitCount = Physics.RaycastNonAlloc(
                eye, toPlayer / sightDistance, sightBuffer, sightDistance, ~0, QueryTriggerInteraction.Ignore);

            int best = -1;
            for (int i = 0; i < hitCount; i++)
            {
                if (sightBuffer[i].collider.transform.IsChildOf(transform))
                    continue;
                if (best < 0 || sightBuffer[i].distance < sightBuffer[best].distance)
                    best = i;
            }

            return best < 0 || sightBuffer[best].collider.transform.IsChildOf(player.transform);
        }

        private void UpdateAnimator()
        {
            if (animator != null)
                animator.SetFloat(Speed01Hash, mover.CurrentSpeed / Mathf.Max(0.1f, MoveSpeed));
        }

        private void OnDrawGizmosSelected()
        {
            if (creature == null || creature.Definition == null)
                return;

            Gizmos.color = new Color(0.3f, 0.8f, 0.3f, 0.5f);
            Gizmos.DrawWireSphere(Application.isPlaying ? creature.HomePosition : transform.position, creature.Definition.WanderRadius);
            Gizmos.color = new Color(0.9f, 0.6f, 0.2f, 0.5f);
            Gizmos.DrawWireSphere(transform.position, DetectionRadius);
        }
    }
}
