using UnityEngine;

namespace IslandGame.Creatures
{
    /// <summary>
    /// Voxel-aware kinematic locomotion for creatures: ground-following via
    /// VoxelNavigation column sampling (smooth 1-block step up/down, gravity
    /// when the ground falls away), and probe-and-slide obstacle avoidance —
    /// the desired heading is probed for walkability and, when blocked, fanned
    /// left/right until a passable bearing is found. See VoxelNavigation for
    /// why this samples voxel data instead of a runtime-baked NavMesh.
    ///
    /// Driven by CreatureAI: SetTarget every state tick, ClearTarget to stand.
    /// The transform moves kinematically (Rigidbody is kinematic; the capsule
    /// collider exists so weapon sphere-casts can hit the creature).
    ///
    /// PERFORMANCE: per frame this costs one ground sample plus, only while
    /// moving, up to 5 step probes of a few block reads each — no physics
    /// casts, no allocation. Stuck detection is a moving progress average so
    /// the AI can repick destinations instead of grinding a wall forever.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CreatureMover : MonoBehaviour
    {
        [Tooltip("Highest ledge the creature walks up without jumping, meters (1.05 = one block plus tolerance).")]
        [SerializeField] private float stepHeight = 1.05f;

        [Tooltip("Biggest drop the creature willingly walks off, meters.")]
        [SerializeField] private float maxDropHeight = 3f;

        [Tooltip("Vertical smoothing speed for step up/down, m/s (visual — walkability already validated the step).")]
        [SerializeField] private float stepSmoothSpeed = 8f;

        [SerializeField] private float gravity = -22f;

        [Tooltip("Turn rate toward the current heading, degrees/second.")]
        [SerializeField] private float turnSpeed = 480f;

        [Tooltip("Distance to the target that counts as arrived, meters.")]
        [SerializeField] private float arriveDistance = 0.6f;

        /// <summary>Fan of headings probed when the direct bearing is blocked, degrees.</summary>
        private static readonly float[] ProbeAngles = { 0f, 40f, -40f, 80f, -80f };

        // Any fall this far below the last verified standing spot means the
        // creature has clipped into a water/void column (a legitimate walk-off
        // never exceeds maxDropHeight) — the fall-rescue snaps it back.
        private const float FallRescueMargin = 4f;

        private Vector3 target;
        private float targetSpeed;
        private bool hasTarget;
        private float verticalVelocity;
        private float stuckTimer;
        private Vector3 lastGroundedPosition;
        private bool hasGroundedPosition;

        /// <summary>Actual horizontal speed this frame, m/s — drives the animator's Speed01.</summary>
        public float CurrentSpeed { get; private set; }

        /// <summary>True when within arrive distance of the current target (cleared with the target).</summary>
        public bool HasArrived { get; private set; }

        /// <summary>True after ~1.5 s without meaningful progress toward the target — the AI repicks its destination.</summary>
        public bool IsStuck => stuckTimer > 1.5f;

        public void SetTarget(Vector3 worldTarget, float speed)
        {
            // A genuinely new destination resets stuck tracking; per-frame
            // re-sets of a moving target (chase/flee) keep it accumulating.
            if (!hasTarget || (worldTarget - target).sqrMagnitude > 4f)
                stuckTimer = 0f;

            target = worldTarget;
            targetSpeed = speed;
            hasTarget = true;
            HasArrived = false;
        }

        public void ClearTarget()
        {
            hasTarget = false;
            HasArrived = false;
            stuckTimer = 0f;
        }

        /// <summary>Turns the body toward a world point without moving (Alert staring, the combat phase's attack facing).</summary>
        public void FaceTowards(Vector3 worldPoint, float deltaTime)
        {
            Vector3 flat = worldPoint - transform.position;
            flat.y = 0f;
            if (flat.sqrMagnitude < 0.01f)
                return;

            Quaternion desired = Quaternion.LookRotation(flat.normalized, Vector3.up);
            transform.rotation = Quaternion.RotateTowards(transform.rotation, desired, turnSpeed * deltaTime);
        }

        private void Update()
        {
            float deltaTime = Time.deltaTime;

            UpdateVertical(deltaTime);
            UpdateHorizontal(deltaTime);
        }

        // ------------------------------------------------------------------
        // Vertical: ground snap, steps, gravity
        // ------------------------------------------------------------------

        private void UpdateVertical(float deltaTime)
        {
            Vector3 position = transform.position;

            if (!VoxelNavigation.TryGetGroundHeight(position + Vector3.up * 0.5f, 2, 8, out float groundY, out bool onWater)
                || onWater)
            {
                // No standable ground (unloaded chunk, carved-away floor, or
                // water below): fall. Unloaded data can't happen in practice —
                // spawners and despawn ranges keep creatures inside the data
                // ring — but falling beats hovering if it ever does.
                verticalVelocity += gravity * deltaTime;
                position.y = Mathf.Max(0f, position.y + verticalVelocity * deltaTime);
                transform.position = position;
                TryFallRescue();
                return;
            }

            if (position.y > groundY + 0.08f && verticalVelocity <= 0f && position.y - groundY > stepHeight)
            {
                // Genuinely airborne (mined out from under us): gravity.
                verticalVelocity += gravity * deltaTime;
                position.y = Mathf.Max(groundY, position.y + verticalVelocity * deltaTime);
                transform.position = position;
                TryFallRescue();
                return;
            }

            // On or near ground: smooth toward the surface so 1-block
            // steps read as steps, not teleports.
            verticalVelocity = 0f;
            position.y = Mathf.MoveTowards(position.y, groundY, stepSmoothSpeed * deltaTime);
            transform.position = position;

            // This column is verified standable land — the fall-rescue's
            // recovery point.
            lastGroundedPosition = new Vector3(position.x, groundY, position.z);
            hasGroundedPosition = true;
        }

        /// <summary>
        /// Safety net, not navigation: a creature that somehow entered a
        /// water/void column (where the ground query can never succeed and the
        /// fall would continue to the world floor) is snapped back to the last
        /// spot it verifiably stood on. Step validation prevents this from
        /// happening; this catches whatever slips past it.
        /// </summary>
        private void TryFallRescue()
        {
            if (!hasGroundedPosition
                || transform.position.y >= lastGroundedPosition.y - (maxDropHeight + FallRescueMargin))
                return;

            verticalVelocity = 0f;
            transform.position = lastGroundedPosition;
        }

        // ------------------------------------------------------------------
        // Horizontal: probe-and-slide steering
        // ------------------------------------------------------------------

        private void UpdateHorizontal(float deltaTime)
        {
            CurrentSpeed = 0f;

            if (!hasTarget || verticalVelocity < -0.5f)
                return; // stand still (or currently falling)

            Vector3 position = transform.position;
            Vector3 toTarget = target - position;
            toTarget.y = 0f;
            float distance = toTarget.magnitude;

            if (distance <= arriveDistance)
            {
                HasArrived = true;
                stuckTimer = 0f;
                return;
            }

            Vector3 desired = toTarget / distance;
            float lookAhead = Mathf.Max(0.6f, targetSpeed * 0.3f);

            // Probe the direct bearing, then fan outward until a walkable
            // step is found. Blocked entirely = stand and accumulate stuck.
            for (int i = 0; i < ProbeAngles.Length; i++)
            {
                Vector3 candidate = Quaternion.Euler(0f, ProbeAngles[i], 0f) * desired;
                Vector3 probe = position + candidate * lookAhead;

                if (!VoxelNavigation.IsStepWalkable(position, probe, stepHeight, maxDropHeight, out _))
                    continue;

                float step = Mathf.Min(targetSpeed * deltaTime, distance);
                Vector3 next = position + candidate * step;

                // The look-ahead probe alone lets a heading that grazes a cell
                // corner walk into a column it never sampled — at shorelines
                // that's a water column one diagonal step wide, and standing
                // in it means falling. A step that crosses into a new column
                // must validate that column itself.
                bool changesColumn = Mathf.FloorToInt(next.x) != Mathf.FloorToInt(position.x)
                                     || Mathf.FloorToInt(next.z) != Mathf.FloorToInt(position.z);
                if (changesColumn
                    && !VoxelNavigation.IsStepWalkable(position, next, stepHeight, maxDropHeight, out _))
                    continue;

                transform.position = next;
                CurrentSpeed = targetSpeed;

                Quaternion heading = Quaternion.LookRotation(candidate, Vector3.up);
                transform.rotation = Quaternion.RotateTowards(transform.rotation, heading, turnSpeed * deltaTime);

                // Progress = closing on the target, not just moving: sliding
                // along a wall sideways still counts as stuck-ish over time.
                float newDistance = Vector3.ProjectOnPlane(target - transform.position, Vector3.up).magnitude;
                stuckTimer = newDistance < distance - step * 0.25f
                    ? Mathf.Max(0f, stuckTimer - deltaTime * 2f)
                    : stuckTimer + deltaTime;
                return;
            }

            stuckTimer += deltaTime;
        }
    }
}
