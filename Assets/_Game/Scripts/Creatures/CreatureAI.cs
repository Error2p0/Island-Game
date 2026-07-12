using System.Collections.Generic;
using IslandGame.Combat;
using IslandGame.Data.Creatures;
using IslandGame.Data.Stats;
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
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Creature))]
    [RequireComponent(typeof(CreatureMover))]
    public sealed class CreatureAI : MonoBehaviour
    {
        private const float DetectionInterval = 0.2f;
        private const float ChaseGiveUpRadiusMultiplier = 1.6f;
        private const float WanderSpeedFraction = 0.45f;

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

        private readonly RaycastHit[] sightBuffer = new RaycastHit[8];

        /// <summary>Current state, exposed for debugging/gizmos and the combat phase's attack layer.</summary>
        public CreatureAIState State => state;

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

            if (Time.time >= nextDetectionTime)
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
                    // Wary freeze: keep staring; bolt when the player crowds in.
                    float distance = Vector3.Distance(transform.position, player.transform.position);
                    if (distance <= Definition.FleeTriggerDistance)
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
            PlayerReferences player = Player;
            if (player == null)
            {
                EnterIdle();
                return;
            }

            Vector3 playerPosition = player.transform.position;
            float distance = Vector3.Distance(transform.position, playerPosition);

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
                mover.SetTarget(playerPosition, MoveSpeed);
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
            PlayerReferences player = Player;
            if (player == null)
            {
                EnterIdle();
                return;
            }

            Vector3 playerPosition = player.transform.position;
            mover.FaceTowards(playerPosition, Time.deltaTime);

            // The timed hit window: damage lands at the windup point of the
            // attack animation, and only if the player is STILL in range —
            // backpedaling out of a windup dodges the hit.
            if (!attackHitResolved && stateTimer >= Definition.AttackWindupSeconds)
            {
                attackHitResolved = true;

                float hitRange = Definition.ApproachDistance + 0.75f;
                if ((playerPosition - transform.position).sqrMagnitude <= hitRange * hitRange)
                {
                    var damageable = player.GetComponent<IDamageable>();
                    if (damageable != null)
                    {
                        Vector3 hitPoint = playerPosition + Vector3.up * 1.1f;
                        Vector3 direction = (playerPosition - transform.position).normalized;
                        var info = new DamageInfo(
                            AttackDamage, Definition.AttackDamageType, hitPoint, direction, gameObject);
                        damageable.ApplyDamage(in info);
                    }
                }
            }

            // Recovery over: sidestep to a flanking point, then Chase closes
            // back in for the next swing.
            if (stateTimer >= Definition.AttackCooldownSeconds)
            {
                strafeSign = -strafeSign;
                Vector3 fromPlayer = transform.position - playerPosition;
                fromPlayer.y = 0f;
                Vector3 away = fromPlayer.sqrMagnitude > 0.01f ? fromPlayer.normalized : -transform.forward;
                Vector3 side = Vector3.Cross(Vector3.up, away) * strafeSign;

                repositionTarget = playerPosition + (away + side * 1.2f).normalized * (Definition.ApproachDistance + 1.2f);
                repositionUntil = Time.time + 0.7f;
                EnterChase();
            }
        }

        // ------------------------------------------------------------------
        // Detection & reactions
        // ------------------------------------------------------------------

        /// <summary>Routes a fresh detection per EFFECTIVE aggression (an aggroed Neutral re-chases). Returns true when the state changed.</summary>
        private bool ReactToDetection()
        {
            if (!playerDetected)
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

                if ((other.transform.position - transform.position).sqrMagnitude <= radiusSqr)
                    other.ReactToThreat();
            }
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
