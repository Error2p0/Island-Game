using System.Collections.Generic;
using UnityEngine;

namespace IslandGame.Player
{
    /// <summary>
    /// Moves the CharacterController. Phase 2: camera-relative walk/sprint with
    /// smooth acceleration, stamina hook, carry-weight scaling, jumping with
    /// coyote time and input buffer. Phase 3/4: crouch/prone speed shaping from
    /// the stance blends. Phase 5: water — trigger-volume sensing, wading slow-
    /// down on the normal grounded path, and a full swimming mode with buoyancy.
    ///
    /// WATER VOLUME CONVENTION (for level designers):
    ///   - GameObject tagged "Water" (built-in tag).
    ///   - A BoxCollider with Is Trigger enabled.
    ///   - Keep the volume UNROTATED (axis-aligned): the world-space top face of
    ///     the box IS the water surface. Height of the box = water depth.
    ///   - Volumes may overlap; the highest surface among touched volumes wins.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PlayerLocomotion : MonoBehaviour
    {
        [Header("Speeds (m/s)")]
        [Tooltip("Default deliberate walk — no key needed.")]
        [SerializeField] private float walkSpeed = 3.0f;

        [SerializeField] private float sprintSpeed = 6.0f;

        [Tooltip("Speed at full crouch. Blended in smoothly with crouch depth.")]
        [SerializeField] private float crouchSpeed = 1.6f;

        [Tooltip("Crawl speed at full prone — deliberately much slower than crouch.")]
        [SerializeField] private float proneSpeed = 0.8f;

        [Tooltip("Backpedal speed as a fraction of forward speed.")]
        [Range(0.3f, 1f)]
        [SerializeField] private float backwardSpeedMultiplier = 0.75f;

        [Header("Acceleration (m/s²)")]
        [Tooltip("Rate when speeding up. Lower = weightier starts.")]
        [SerializeField] private float acceleration = 16f;

        [Tooltip("Rate when slowing down. Slightly higher than acceleration so stops feel planted, not slippery.")]
        [SerializeField] private float deceleration = 20f;

        [Tooltip("Acceleration multiplier at full crouch — a little softer, still responsive.")]
        [Range(0.3f, 1f)]
        [SerializeField] private float crouchAccelerationMultiplier = 0.85f;

        [Tooltip("Acceleration multiplier at full prone — crawling starts and stops sluggishly by design.")]
        [Range(0.2f, 1f)]
        [SerializeField] private float proneAccelerationMultiplier = 0.6f;

        [Tooltip("Fraction of ground acceleration available while airborne.")]
        [Range(0f, 1f)]
        [SerializeField] private float airControl = 0.35f;

        [Header("Sprint")]
        [Tooltip("How forward the move input must point to sprint (cosine of angle from straight ahead). 0.5 ≈ within 60°.")]
        [Range(0f, 1f)]
        [SerializeField] private float sprintForwardnessThreshold = 0.5f;

        [Header("Stamina Hook")]
        [Tooltip("Stamina consumed per second of sprinting or fast swim strokes (1 = full bar).")]
        [SerializeField] private float staminaDrainPerSecond = 0.125f;

        [SerializeField] private float staminaRegenPerSecond = 0.2f;

        [Tooltip("Seconds after sprinting/fast strokes stop before regen begins.")]
        [SerializeField] private float staminaRegenDelay = 0.75f;

        [Tooltip("After draining to zero, stamina must recover to this fraction before sprint/fast stroke re-engages (prevents flickering at empty).")]
        [Range(0.05f, 0.5f)]
        [SerializeField] private float exhaustionRecoveryThreshold = 0.25f;

        [Header("Jump")]
        [Tooltip("Apex height in meters, converted to launch velocity from gravity.")]
        [SerializeField] private float jumpHeight = 1.1f;

        [Tooltip("Grace window for jumping after walking off a ledge.")]
        [SerializeField] private float coyoteTime = 0.15f;

        [Tooltip("A jump pressed this long before landing still fires on touchdown.")]
        [SerializeField] private float jumpBufferTime = 0.12f;

        [Header("Gravity")]
        [Tooltip("Stronger than physical gravity on purpose; real -9.81 feels floaty in first person.")]
        [SerializeField] private float gravity = -25f;

        [Tooltip("Small constant downward velocity while grounded so the controller stays glued to slopes and steps.")]
        [SerializeField] private float groundedStickVelocity = -2f;

        [SerializeField] private float terminalVelocity = -55f;

        [Tooltip("Brief loss of ground contact tolerated before entering Airborne, so slopes/steps don't flicker the state.")]
        [SerializeField] private float groundedGraceTime = 0.1f;

        [Header("Water — Wading")]
        [Tooltip("Grounded speed multiplier when the water reaches swim depth (blends in linearly with depth). Wading reuses Walking/Sprinting — no separate state.")]
        [Range(0.2f, 1f)]
        [SerializeField] private float wadeSpeedMultiplierAtSwimDepth = 0.5f;

        [Header("Water — Swimming")]
        [Tooltip("Water depth (surface above feet) at which full swimming engages.")]
        [SerializeField] private float swimEnterDepth = 1.35f;

        [Tooltip("Depth at which swimming disengages — slightly below the enter depth so the boundary can't flicker.")]
        [SerializeField] private float swimExitDepth = 1.15f;

        [SerializeField] private float swimSpeed = 2.0f;

        [Tooltip("Speed while holding Sprint for fast strokes. Drains stamina.")]
        [SerializeField] private float fastSwimSpeed = 3.5f;

        [SerializeField] private float swimAcceleration = 8f;

        [Tooltip("Water drag: how fast momentum (including a dive plunge) bleeds off.")]
        [SerializeField] private float swimDeceleration = 10f;

        [Tooltip("How deep the feet float below the surface at rest. 1.45 keeps the eyes just above water.")]
        [SerializeField] private float floatDepth = 1.45f;

        [Tooltip("Buoyancy spring strength pulling the body toward its float line (per meter of offset).")]
        [SerializeField] private float buoyancySpring = 2.5f;

        [Tooltip("Maximum buoyant rise speed, m/s.")]
        [SerializeField] private float buoyancyMaxRise = 1.5f;

        [Tooltip("Maximum settle-down speed when bobbing above the float line, m/s.")]
        [SerializeField] private float buoyancyMaxSink = 0.8f;

        [Header("Carry Weight")]
        [Tooltip("0 = unburdened, 1 = maximum load. Driven by the inventory/carrying systems in later phases. Also slows swimming — heavy loads are dangerous in deep water.")]
        [Range(0f, 1f)]
        [SerializeField] private float carryWeight01 = 0f;

        [Tooltip("Max speed multiplier at full load.")]
        [Range(0.1f, 1f)]
        [SerializeField] private float fullLoadSpeedMultiplier = 0.55f;

        [Tooltip("Acceleration multiplier at full load — heavy loads also start/stop slower.")]
        [Range(0.1f, 1f)]
        [SerializeField] private float fullLoadAccelerationMultiplier = 0.6f;

        [Tooltip("Sprinting and fast swim strokes are blocked above this load.")]
        [Range(0f, 1f)]
        [SerializeField] private float sprintMaxCarryWeight = 0.75f;

        [Tooltip("Going prone is blocked above this load — no diving flat while hauling a log.")]
        [Range(0f, 1f)]
        [SerializeField] private float proneMaxCarryWeight = 0.85f;

        private const float MoveInputDeadzone = 0.1f;
        private const string WaterTag = "Water";

        /// <summary>Grounded result of the most recent controller move.</summary>
        public bool IsGrounded { get; private set; } = true;

        /// <summary>Grounded, or within the short grace window after losing contact (slopes, steps, coyote logic).</summary>
        public bool IsEffectivelyGrounded => IsGrounded || Time.time - lastGroundedTime <= groundedGraceTime;

        /// <summary>True while sprint is actually applied (input + stamina + state all allow it).</summary>
        public bool IsSprinting { get; private set; }

        /// <summary>True while swimming with the fast stroke engaged (drains stamina).</summary>
        public bool IsSwimFastStroke { get; private set; }

        /// <summary>Stamina hook for the future stamina system/UI. 0-1. Shared by sprinting and fast swim strokes.</summary>
        public float Stamina => stamina;

        /// <summary>True after stamina hit zero, until it recovers past the threshold.</summary>
        public bool IsExhausted => isExhausted;

        /// <summary>True while in any water source — a touched Water trigger volume OR voxel water reported via SetExternalWater (wading OR swimming).</summary>
        public bool IsInWater => waterVolumes.Count > 0 || externalWaterActive;

        /// <summary>How far the water surface is above the feet, meters. 0 when not in water.</summary>
        public float WaterDepth { get; private set; }

        /// <summary>World Y of the active water surface. Only meaningful while IsInWater.</summary>
        public float WaterSurfaceY { get; private set; }

        /// <summary>True while swimming with the camera below the surface — hook for underwater rendering/audio and item rules (e.g. torches extinguish).</summary>
        public bool IsEyeUnderwater =>
            references.StateMachine.CurrentState == PlayerState.Swimming
            && references.CameraPivot.position.y < WaterSurfaceY - 0.05f;

        /// <summary>Carry load 0-1; inventory/carrying systems drive this later. Scales max speed and acceleration on land and in water.</summary>
        public float CarryWeight01
        {
            get => carryWeight01;
            set => carryWeight01 = Mathf.Clamp01(value);
        }

        /// <summary>
        /// Primary API for the future inventory/carrying systems: 0 = unburdened,
        /// 1 = maximum load. Scales every stance's speed and acceleration, and
        /// gates sprint / prone entry via the serialized thresholds.
        /// </summary>
        public void SetCarryWeight(float weight01) => CarryWeight01 = weight01;

        /// <summary>False when the current load is too heavy to sprint or fast-stroke swim.</summary>
        public bool SprintAllowedByLoad => carryWeight01 <= sprintMaxCarryWeight;

        /// <summary>False when the current load is too heavy to go prone.</summary>
        public bool ProneAllowedByLoad => carryWeight01 <= proneMaxCarryWeight;

        /// <summary>Current horizontal speed in m/s (for animation blend trees later).</summary>
        public float CurrentSpeed => horizontalVelocity.magnitude;

        // Configured speeds, exposed so PlayerAnimationController can normalize
        // blend parameters against them (animation follows actual velocity).
        public float WalkSpeed => walkSpeed;
        public float SprintSpeed => sprintSpeed;
        public float CrouchSpeed => crouchSpeed;
        public float ProneSpeed => proneSpeed;
        public float SwimSpeed => swimSpeed;
        public float FastSwimSpeed => fastSwimSpeed;

        /// <summary>Current vertical velocity in m/s (negative = falling).</summary>
        public float VerticalVelocity => verticalVelocity;

        /// <summary>Actual velocity of the controller from its last move.</summary>
        public Vector3 Velocity => references.Controller.velocity;

        private PlayerReferences references;
        private Vector3 horizontalVelocity;
        private float verticalVelocity;
        private Vector3 swimVelocity;
        private float stamina = 1f;
        private bool isExhausted;
        private float lastSprintTime = float.NegativeInfinity;
        private float lastGroundedTime = float.NegativeInfinity;
        private float lastJumpPressedTime = float.NegativeInfinity;
        private float lastGroundedMaxSpeed;
        private bool hasMoveInput;
        private readonly List<Collider> waterVolumes = new List<Collider>();
        private bool externalWaterActive;
        private float externalWaterSurfaceY;

        /// <summary>
        /// External water source hook: the voxel terrain's water sensor reports
        /// ocean water at the player's column here every frame, alongside the
        /// Phase 5 trigger-volume convention which keeps working unchanged for
        /// hand-placed pools. The highest surface among all sources wins.
        /// </summary>
        public void SetExternalWater(bool inWater, float surfaceY)
        {
            externalWaterActive = inWater;
            externalWaterSurfaceY = surfaceY;
        }

        private float SpeedWeightMultiplier => Mathf.Lerp(1f, fullLoadSpeedMultiplier, carryWeight01);
        private float AccelerationWeightMultiplier => Mathf.Lerp(1f, fullLoadAccelerationMultiplier, carryWeight01);

        private float CrouchBlend =>
            references.Crouch != null ? references.Crouch.CrouchBlend01 : 0f;

        private float ProneBlend =>
            references.Prone != null ? references.Prone.ProneBlend01 : 0f;

        private void Awake()
        {
            references = GetComponent<PlayerReferences>();
            lastGroundedMaxSpeed = walkSpeed;
        }

        private void OnEnable()
        {
            references.InputHandler.JumpPressed += OnJumpPressed;
        }

        private void OnDisable()
        {
            references.InputHandler.JumpPressed -= OnJumpPressed;
        }

        private void Update()
        {
            float deltaTime = Time.deltaTime;

            UpdateWater();

            // Clamp (not normalize) so partial gamepad deflection still gives
            // analog speed, while keyboard diagonals can't exceed magnitude 1.
            Vector2 move = Vector2.ClampMagnitude(references.InputHandler.MoveInput, 1f);
            float inputMagnitude = move.magnitude;
            hasMoveInput = inputMagnitude > MoveInputDeadzone;

            // Cosine of the angle between move input and straight ahead;
            // 1 = pure forward, 0 = pure strafe, -1 = pure backpedal.
            float forwardness = hasMoveInput ? move.y / inputMagnitude : 0f;

            if (references.StateMachine.CurrentState == PlayerState.Swimming)
            {
                UpdateSwimming(move, deltaTime);
                UpdateStamina(deltaTime);
                return;
            }

            IsSwimFastStroke = false;

            UpdateSprint(forwardness);
            UpdateStamina(deltaTime);
            ApplyGravity(deltaTime);
            TryConsumeJump();
            UpdateHorizontalVelocity(move, forwardness, deltaTime);

            references.Controller.Move((horizontalVelocity + Vector3.up * verticalVelocity) * deltaTime);

            // isGrounded is only valid immediately after a Move call.
            IsGrounded = references.Controller.isGrounded;
            if (IsGrounded)
                lastGroundedTime = Time.time;

            // Bumping the ceiling kills upward velocity so the jump doesn't "hang".
            if ((references.Controller.collisionFlags & CollisionFlags.Above) != 0 && verticalVelocity > 0f)
                verticalVelocity = 0f;

            UpdateStateMachine();
        }

        // ------------------------------------------------------------------
        // Water sensing (trigger volume convention — see class summary)
        // ------------------------------------------------------------------

        private void OnTriggerEnter(Collider other)
        {
            if (other.CompareTag(WaterTag) && !waterVolumes.Contains(other))
                waterVolumes.Add(other);
        }

        private void OnTriggerExit(Collider other)
        {
            if (other.CompareTag(WaterTag))
                waterVolumes.Remove(other);
        }

        private void UpdateWater()
        {
            // Prune destroyed/disabled volumes (OnTriggerExit doesn't fire for those).
            for (int i = waterVolumes.Count - 1; i >= 0; i--)
            {
                if (waterVolumes[i] == null || !waterVolumes[i].enabled || !waterVolumes[i].gameObject.activeInHierarchy)
                    waterVolumes.RemoveAt(i);
            }

            // Highest surface among all sources wins: trigger volumes are AABB
            // by convention (bounds.max.y = surface plane); voxel water reports
            // its surface through SetExternalWater.
            float surfaceY = float.NegativeInfinity;
            for (int i = 0; i < waterVolumes.Count; i++)
                surfaceY = Mathf.Max(surfaceY, waterVolumes[i].bounds.max.y);
            if (externalWaterActive)
                surfaceY = Mathf.Max(surfaceY, externalWaterSurfaceY);

            if (float.IsNegativeInfinity(surfaceY))
            {
                WaterDepth = 0f;
                return;
            }

            WaterSurfaceY = surfaceY;
            WaterDepth = Mathf.Max(0f, surfaceY - transform.position.y);

            TryEnterSwimming();
        }

        private void TryEnterSwimming()
        {
            PlayerStateMachine stateMachine = references.StateMachine;

            if (stateMachine.CurrentState == PlayerState.Swimming || WaterDepth < swimEnterDepth)
                return;

            // Note: Prone is a dead-end state (exits only through crouch), so a
            // prone player cannot be forced into Swimming. Water volumes are
            // static, so "water rises over a prone player" cannot happen yet.
            if (stateMachine.TryChangeState(PlayerState.Swimming))
            {
                // Carry the current velocity into the water so a fall becomes a
                // plunge that water drag and buoyancy then settle — no snapping.
                swimVelocity = horizontalVelocity + Vector3.up * verticalVelocity;
                IsSprinting = false;
            }
        }

        // ------------------------------------------------------------------
        // Swimming
        // ------------------------------------------------------------------

        private void UpdateSwimming(Vector2 move, float deltaTime)
        {
            if (isExhausted && stamina >= exhaustionRecoveryThreshold)
                isExhausted = false;

            PlayerInputHandler input = references.InputHandler;

            IsSwimFastStroke = input.SprintHeld && hasMoveInput && !isExhausted && stamina > 0f
                               && SprintAllowedByLoad;

            // Camera-relative 3D movement: looking up + forward swims upward.
            // Space surfaces, Crouch dives.
            Vector3 direction = references.CameraPivot.forward * move.y + transform.right * move.x;
            float verticalInput = (input.JumpHeld ? 1f : 0f) - (input.CrouchHeld ? 1f : 0f);
            direction += Vector3.up * verticalInput;
            direction = Vector3.ClampMagnitude(direction, 1f);

            float speed = (IsSwimFastStroke ? fastSwimSpeed : swimSpeed) * SpeedWeightMultiplier;
            Vector3 targetVelocity = direction * speed;

            float rate = targetVelocity.sqrMagnitude > swimVelocity.sqrMagnitude
                ? swimAcceleration
                : swimDeceleration;
            rate *= AccelerationWeightMultiplier;
            swimVelocity = Vector3.MoveTowards(swimVelocity, targetVelocity, rate * deltaTime);

            Vector3 velocity = swimVelocity;

            // Buoyancy spring toward the float line — skipped while the player
            // deliberately swims downward, so diving doesn't fight the water.
            float floatTargetY = WaterSurfaceY - floatDepth;
            float feetY = transform.position.y;
            if (targetVelocity.y >= -0.05f)
            {
                float buoyancy = Mathf.Clamp((floatTargetY - feetY) * buoyancySpring, -buoyancyMaxSink, buoyancyMaxRise);
                velocity.y += buoyancy;
            }

            // Never breach the surface by swimming — leaving the water goes
            // through the shallow-exit or airborne-exit paths below.
            if (feetY >= floatTargetY && velocity.y > 0f)
                velocity.y = 0f;

            references.Controller.Move(velocity * deltaTime);
            IsGrounded = references.Controller.isGrounded;
            if (IsGrounded)
                lastGroundedTime = Time.time;

            // Keep the land-movement fields in sync so exiting the water hands
            // over the exact current velocity — no snapping.
            horizontalVelocity = new Vector3(velocity.x, 0f, velocity.z);
            verticalVelocity = velocity.y;

            UpdateSwimExit();
        }

        private void UpdateSwimExit()
        {
            PlayerStateMachine stateMachine = references.StateMachine;
            bool exited = false;

            // Left every water source sideways (e.g. over a waterfall edge)?
            if (!IsInWater)
            {
                exited = stateMachine.TryChangeState(IsGrounded ? PlayerState.Idle : PlayerState.Airborne);
            }
            else if (WaterDepth <= swimExitDepth)
            {
                // Shallow enough to stand: back to the grounded set; the wade
                // multiplier takes over from here. Not grounded (ledge under the
                // surface ended) -> Airborne.
                exited = IsGrounded
                    ? stateMachine.TryChangeState(hasMoveInput ? PlayerState.Walking : PlayerState.Idle)
                    : stateMachine.TryChangeState(PlayerState.Airborne);
            }

            // A jump press while swimming means "surface", never "jump" — clear
            // the buffer so it can't auto-fire the instant we reach the shore.
            if (exited)
                lastJumpPressedTime = float.NegativeInfinity;
        }

        // ------------------------------------------------------------------
        // Land movement (Phases 2-4)
        // ------------------------------------------------------------------

        /// <summary>
        /// Overrides vertical velocity (knockback, launch pads, future systems).
        /// </summary>
        public void SetVerticalVelocity(float velocity)
        {
            verticalVelocity = velocity;
        }

        /// <summary>
        /// Adjusts stamina by a delta (consumables, damage effects later). Clamped 0-1.
        /// </summary>
        public void ModifyStamina(float delta)
        {
            stamina = Mathf.Clamp01(stamina + delta);
            if (stamina <= 0f)
                isExhausted = true;
        }

        private void OnJumpPressed()
        {
            lastJumpPressedTime = Time.time;
        }

        private void UpdateSprint(float forwardness)
        {
            if (isExhausted && stamina >= exhaustionRecoveryThreshold)
                isExhausted = false;

            bool wantsSprint = references.InputHandler.SprintHeld
                               && hasMoveInput
                               && forwardness >= sprintForwardnessThreshold;

            bool canAffordSprint = !isExhausted && stamina > 0f;

            // Sprint only applies in locomotion-owned states; Crouching, Prone
            // and Swimming are not among them. Heavy loads also block it.
            IsSprinting = wantsSprint
                          && canAffordSprint
                          && SprintAllowedByLoad
                          && IsGrounded
                          && references.StateMachine.IsLocomotionDrivenState;
        }

        private void UpdateStamina(float deltaTime)
        {
            // One shared resource: sprinting on land and fast swim strokes both
            // drain it (requirement: never a second stamina variable).
            if (IsSprinting || IsSwimFastStroke)
            {
                lastSprintTime = Time.time;
                stamina = Mathf.Max(0f, stamina - staminaDrainPerSecond * deltaTime);
                if (stamina <= 0f)
                    isExhausted = true;
            }
            else if (Time.time - lastSprintTime >= staminaRegenDelay)
            {
                stamina = Mathf.Min(1f, stamina + staminaRegenPerSecond * deltaTime);
            }
        }

        private void ApplyGravity(float deltaTime)
        {
            if (IsGrounded && verticalVelocity < 0f)
            {
                verticalVelocity = groundedStickVelocity;
                return;
            }

            verticalVelocity = Mathf.Max(verticalVelocity + gravity * deltaTime, terminalVelocity);
        }

        private void TryConsumeJump()
        {
            if (Time.time - lastJumpPressedTime > jumpBufferTime)
                return;

            bool withinCoyote = IsGrounded || Time.time - lastGroundedTime <= coyoteTime;
            if (!withinCoyote || !references.StateMachine.IsLocomotionDrivenState)
                return;

            verticalVelocity = Mathf.Sqrt(2f * -gravity * jumpHeight);

            // Consume the buffer and kill coyote so one press can't double-fire.
            lastJumpPressedTime = float.NegativeInfinity;
            lastGroundedTime = float.NegativeInfinity;
            IsGrounded = false;
        }

        private void UpdateHorizontalVelocity(Vector2 move, float forwardness, float deltaTime)
        {
            // Yaw lives on the root (see FirstPersonCameraController), so root
            // axes ARE the camera's horizontal facing.
            Vector3 worldDirection = transform.right * move.x + transform.forward * move.y;

            float maxSpeed = ComputeTargetMaxSpeed(forwardness);
            if (IsGrounded)
                lastGroundedMaxSpeed = maxSpeed;
            else
                maxSpeed = lastGroundedMaxSpeed; // keep takeoff speed; air steering only redirects it

            Vector3 targetVelocity = worldDirection * maxSpeed;

            float rate = targetVelocity.sqrMagnitude > horizontalVelocity.sqrMagnitude
                ? acceleration
                : deceleration;
            rate *= AccelerationWeightMultiplier;
            rate *= Mathf.Lerp(1f, crouchAccelerationMultiplier, CrouchBlend);
            rate *= Mathf.Lerp(1f, proneAccelerationMultiplier, ProneBlend);
            if (!IsGrounded)
                rate *= airControl;

            horizontalVelocity = Vector3.MoveTowards(horizontalVelocity, targetVelocity, rate * deltaTime);
        }

        private float ComputeTargetMaxSpeed(float forwardness)
        {
            float speed = walkSpeed;

            if (IsSprinting)
            {
                // Full sprint speed only near straight ahead; sprinting on a
                // diagonal blends back toward walk so strafing can't fly.
                float sprintBlend = Mathf.InverseLerp(sprintForwardnessThreshold, 1f, forwardness);
                speed = Mathf.Lerp(walkSpeed, sprintSpeed, sprintBlend);
            }

            // Speed follows stance depth, so stance changes decelerate smoothly
            // with the height blend instead of snapping. Prone layers on top of
            // crouch the same way the controller height does.
            speed = Mathf.Lerp(speed, crouchSpeed, CrouchBlend);
            speed = Mathf.Lerp(speed, proneSpeed, ProneBlend);

            // Wading: deeper water drags harder. Reaches the full multiplier at
            // swim depth, at which point full swimming takes over anyway.
            if (IsInWater)
                speed *= Mathf.Lerp(1f, wadeSpeedMultiplierAtSwimDepth, Mathf.Clamp01(WaterDepth / swimEnterDepth));

            speed *= Mathf.Lerp(1f, backwardSpeedMultiplier, Mathf.Clamp01(-forwardness));
            speed *= SpeedWeightMultiplier;
            return speed;
        }

        private void UpdateStateMachine()
        {
            PlayerStateMachine stateMachine = references.StateMachine;

            if (!stateMachine.IsLocomotionDrivenState)
                return; // crouch/prone/swim systems own their transitions

            if (!IsEffectivelyGrounded)
            {
                stateMachine.TryChangeState(PlayerState.Airborne);
                return;
            }

            if (IsGrounded)
            {
                // Landing while the crouch stance is active returns to Crouching;
                // PlayerCrouch owns that state from then on.
                if (references.Crouch != null && references.Crouch.IsCrouched)
                {
                    stateMachine.TryChangeState(PlayerState.Crouching);
                    return;
                }

                PlayerState target = IsSprinting && hasMoveInput
                    ? PlayerState.Sprinting
                    : hasMoveInput ? PlayerState.Walking : PlayerState.Idle;
                stateMachine.TryChangeState(target);
            }
        }
    }
}
