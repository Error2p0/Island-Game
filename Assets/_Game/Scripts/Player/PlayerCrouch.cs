using UnityEngine;

namespace IslandGame.Player
{
    /// <summary>
    /// Owns the crouch stance: hold/toggle input, the overhead clearance check,
    /// and the Crouching state transitions. Also the single writer of the
    /// CharacterController's dimensions and the rig's placeholder stance pose:
    /// crouch and prone blends are composed here in one deterministic pass so
    /// the two stance systems can never fight over the controller or the bones.
    /// Locomotion reads CrouchBlend01 to shape speed; it never resizes anything.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PlayerCrouch : MonoBehaviour
    {
        [Header("Mode")]
        [Tooltip("On: crouch while the key is held (Rust/Valheim feel). Off: each press toggles.")]
        [SerializeField] private bool holdToCrouch = true;

        [Header("Dimensions")]
        [Tooltip("CharacterController height at full crouch. Standing values are read from the controller itself.")]
        [SerializeField] private float crouchedControllerHeight = 1.2f;

        [Tooltip("Seconds for the full stand <-> crouch blend.")]
        [SerializeField] private float transitionSeconds = 0.22f;

        [Header("Stand-Up Clearance")]
        [Tooltip("Layers that can block standing up. The player's own collider is excluded explicitly, so 'Everything' is safe.")]
        [SerializeField] private LayerMask obstructionMask = ~0;

        [Header("Placeholder Pose (removed when real animations land in Phase 6)")]
        [Tooltip("How far the hips sink at full crouch. Lowers the whole upper body, including the head bone the camera hangs from.")]
        [SerializeField] private float hipsDrop = 0.45f;

        [Tooltip("Thigh swing forward at full crouch, degrees.")]
        [SerializeField] private float thighForwardAngle = 50f;

        [Tooltip("Knee bend at full crouch, degrees. Feet auto-counter-rotate to stay flat.")]
        [SerializeField] private float kneeBendAngle = 124f;

        /// <summary>Stance target. True from the moment crouch is requested until a clear stand-up completes its start.</summary>
        public bool IsCrouched { get; private set; }

        /// <summary>0 = fully standing, 1 = fully crouched. Locomotion uses this to blend speed with height.</summary>
        public float CrouchBlend01 => crouchBlend;

        /// <summary>Controller height at full crouch — the clearance target for prone -> crouch.</summary>
        public float CrouchedControllerHeight => crouchedControllerHeight;

        private const float StandUpRadiusFactor = 0.95f;

        private PlayerReferences references;
        private readonly Collider[] overlapBuffer = new Collider[8];

        private float crouchBlend;
        private bool toggleCrouchActive;
        private float standingHeight;
        private float controllerBottomY;

        private Transform hips;
        private Transform leftThigh, leftShin, leftFoot;
        private Transform rightThigh, rightShin, rightFoot;
        private Transform leftUpperArm, rightUpperArm;
        private Vector3 hipsDefaultLocalPosition;
        private Quaternion hipsDefaultLocalRotation;
        private Quaternion thighDefaultL, shinDefaultL, footDefaultL;
        private Quaternion thighDefaultR, shinDefaultR, footDefaultR;
        private Quaternion armDefaultL, armDefaultR;

        private float ProneBlend =>
            references.Prone != null ? references.Prone.ProneBlend01 : 0f;

        private void Awake()
        {
            references = GetComponent<PlayerReferences>();

            CharacterController controller = GetComponent<CharacterController>();
            standingHeight = controller.height;
            controllerBottomY = controller.center.y - controller.height * 0.5f;

            CacheBones();
        }

        private void OnEnable()
        {
            references.InputHandler.CrouchPressed += OnCrouchPressed;
        }

        private void OnDisable()
        {
            references.InputHandler.CrouchPressed -= OnCrouchPressed;
        }

        private void Update()
        {
            UpdateStance();
            crouchBlend = Mathf.MoveTowards(crouchBlend, IsCrouched ? 1f : 0f, Time.deltaTime / transitionSeconds);
            ApplyControllerDimensions();
            ApplyPose();
            UpdateStateMachine();
        }

        private void OnCrouchPressed()
        {
            // While prone, the crouch key belongs to PlayerProne (exit-to-crouch).
            // Consuming it here too would flip the toggle stance out from under
            // the forced crouch and double-fire the exit.
            if (references.StateMachine.CurrentState == PlayerState.Prone)
                return;

            if (!holdToCrouch)
                toggleCrouchActive = !toggleCrouchActive;
        }

        /// <summary>
        /// Engages the crouch stance from code — used by PlayerProne when the
        /// player rises out of prone, so the normal crouch flow (including the
        /// auto-stand once headroom allows and the key is released) takes over.
        /// </summary>
        public void ForceCrouchStance()
        {
            IsCrouched = true;
            if (!holdToCrouch)
                toggleCrouchActive = true;
        }

        private void UpdateStance()
        {
            PlayerStateMachine stateMachine = references.StateMachine;

            // Crouch only participates in core locomotion states (+ its own).
            // The prone/swim systems own their stances.
            bool stateAllowsCrouch = stateMachine.IsLocomotionDrivenState
                                     || stateMachine.CurrentState == PlayerState.Crouching;
            if (!stateAllowsCrouch)
            {
                toggleCrouchActive = false;
                if (IsCrouched && CanStandUp())
                    IsCrouched = false;
                return;
            }

            bool wantsCrouch = holdToCrouch ? references.InputHandler.CrouchHeld : toggleCrouchActive;

            if (wantsCrouch)
            {
                IsCrouched = true;
            }
            else if (IsCrouched)
            {
                // Blocked stand-ups simply stay crouched and retry every frame
                // until the overhead space clears.
                if (CanStandUp())
                    IsCrouched = false;
                else if (!holdToCrouch)
                    toggleCrouchActive = false; // keep toggle consistent with the forced stance
            }
        }

        /// <summary>
        /// True when a capsule of the given height at the current position is
        /// free of everything except the player's own collider.
        /// </summary>
        public bool HasClearance(float targetHeight)
        {
            CharacterController controller = references.Controller;
            float radius = controller.radius * StandUpRadiusFactor;

            Vector3 basePosition = transform.position;
            Vector3 bottomSphere = basePosition + Vector3.up * (controllerBottomY + controller.radius);
            Vector3 topSphere = basePosition + Vector3.up * (controllerBottomY + targetHeight - controller.radius);

            int hitCount = Physics.OverlapCapsuleNonAlloc(
                bottomSphere, topSphere, radius, overlapBuffer, obstructionMask, QueryTriggerInteraction.Ignore);

            for (int i = 0; i < hitCount; i++)
            {
                if (overlapBuffer[i] != controller)
                    return false;
            }

            return true;
        }

        /// <summary>True when the full standing capsule is clear.</summary>
        public bool CanStandUp() => HasClearance(standingHeight);

        private void ApplyControllerDimensions()
        {
            CharacterController controller = references.Controller;

            float height = Mathf.Lerp(standingHeight, crouchedControllerHeight, crouchBlend);

            // Prone lowers further, layered on whatever the crouch blend gives —
            // transitions between the two stances stay continuous.
            if (references.Prone != null)
                height = Mathf.Lerp(height, references.Prone.ProneControllerHeight, ProneBlend);

            controller.height = height;

            // Resize around the controller's bottom so the feet never leave the ground.
            Vector3 center = controller.center;
            center.y = controllerBottomY + height * 0.5f;
            controller.center = center;
        }

        private void ApplyPose()
        {
            // Phase 6+: once an Animator Controller drives the rig, stance poses
            // come from animation clips — the procedural pose below would be
            // overwritten every frame anyway. It remains functional for rigs
            // running without a controller (tests, early scenes).
            if (references.Animator != null && references.Animator.runtimeAnimatorController != null)
                return;

            if (hips == null)
                return;

            float proneBlend = ProneBlend;
            PlayerProne prone = references.Prone;

            // --- Crouch layer -------------------------------------------------
            // Lowering the hips carries the spine, head bone and therefore the
            // camera pivot down with it — one motion source, no double-dipping.
            Vector3 hipsPosition = hipsDefaultLocalPosition + Vector3.down * (hipsDrop * crouchBlend);
            Quaternion hipsRotation = hipsDefaultLocalRotation;

            float footAngle = thighForwardAngle - kneeBendAngle;
            Quaternion thighCrouch = Quaternion.Euler(-thighForwardAngle, 0f, 0f);
            Quaternion shinCrouch = Quaternion.Euler(kneeBendAngle, 0f, 0f);
            Quaternion footCrouch = Quaternion.Euler(footAngle, 0f, 0f);

            Quaternion thighL = Quaternion.Slerp(thighDefaultL, thighDefaultL * thighCrouch, crouchBlend);
            Quaternion shinL = Quaternion.Slerp(shinDefaultL, shinDefaultL * shinCrouch, crouchBlend);
            Quaternion footL = Quaternion.Slerp(footDefaultL, footDefaultL * footCrouch, crouchBlend);
            Quaternion thighR = Quaternion.Slerp(thighDefaultR, thighDefaultR * thighCrouch, crouchBlend);
            Quaternion shinR = Quaternion.Slerp(shinDefaultR, shinDefaultR * shinCrouch, crouchBlend);
            Quaternion footR = Quaternion.Slerp(footDefaultR, footDefaultR * footCrouch, crouchBlend);
            Quaternion armL = armDefaultL;
            Quaternion armR = armDefaultR;

            // --- Prone layer (overrides toward lying flat) --------------------
            if (prone != null && proneBlend > 0f)
            {
                hipsPosition = Vector3.Lerp(hipsPosition, prone.HipsPoseLocalPosition, proneBlend);
                hipsRotation = Quaternion.Slerp(hipsRotation, hipsDefaultLocalRotation * prone.HipsPoseLocalRotation, proneBlend);

                Quaternion thighProne = prone.ThighPoseLocalRotation;
                thighL = Quaternion.Slerp(thighL, thighDefaultL * thighProne, proneBlend);
                thighR = Quaternion.Slerp(thighR, thighDefaultR * thighProne, proneBlend);
                shinL = Quaternion.Slerp(shinL, shinDefaultL, proneBlend);
                shinR = Quaternion.Slerp(shinR, shinDefaultR, proneBlend);
                footL = Quaternion.Slerp(footL, footDefaultL, proneBlend);
                footR = Quaternion.Slerp(footR, footDefaultR, proneBlend);
                armL = Quaternion.Slerp(armDefaultL, armDefaultL * prone.LeftArmPoseLocalRotation, proneBlend);
                armR = Quaternion.Slerp(armDefaultR, armDefaultR * prone.RightArmPoseLocalRotation, proneBlend);
            }

            hips.localPosition = hipsPosition;
            hips.localRotation = hipsRotation;
            leftThigh.localRotation = thighL;
            leftShin.localRotation = shinL;
            leftFoot.localRotation = footL;
            rightThigh.localRotation = thighR;
            rightShin.localRotation = shinR;
            rightFoot.localRotation = footR;
            leftUpperArm.localRotation = armL;
            rightUpperArm.localRotation = armR;
        }

        private void UpdateStateMachine()
        {
            PlayerStateMachine stateMachine = references.StateMachine;
            PlayerLocomotion locomotion = references.Locomotion;

            if (stateMachine.CurrentState == PlayerState.Crouching)
            {
                // Falling while crouched hands the state to Airborne; the stance
                // (short controller) is kept, and landing re-enters Crouching.
                if (!locomotion.IsEffectivelyGrounded)
                    stateMachine.TryChangeState(PlayerState.Airborne);
                else if (!IsCrouched)
                    stateMachine.TryChangeState(PlayerState.Idle); // locomotion refines to Walking/Sprinting
            }
            else if (IsCrouched && locomotion.IsGrounded && stateMachine.IsLocomotionDrivenState)
            {
                stateMachine.TryChangeState(PlayerState.Crouching);
            }
        }

        private void CacheBones()
        {
            hips = references.HipsBone != null ? references.HipsBone : transform.Find("Hips");
            if (hips == null)
            {
                Debug.LogError("[PlayerCrouch] Hips bone not found; crouch pose disabled.", this);
                return;
            }

            leftThigh = hips.Find("LeftUpperLeg");
            leftShin = leftThigh.Find("LeftLowerLeg");
            leftFoot = leftShin.Find("LeftFoot");
            rightThigh = hips.Find("RightUpperLeg");
            rightShin = rightThigh.Find("RightLowerLeg");
            rightFoot = rightShin.Find("RightFoot");
            leftUpperArm = hips.Find("Spine/Chest/LeftUpperArm");
            rightUpperArm = hips.Find("Spine/Chest/RightUpperArm");

            hipsDefaultLocalPosition = hips.localPosition;
            hipsDefaultLocalRotation = hips.localRotation;
            thighDefaultL = leftThigh.localRotation;
            shinDefaultL = leftShin.localRotation;
            footDefaultL = leftFoot.localRotation;
            thighDefaultR = rightThigh.localRotation;
            shinDefaultR = rightShin.localRotation;
            footDefaultR = rightFoot.localRotation;
            armDefaultL = leftUpperArm.localRotation;
            armDefaultR = rightUpperArm.localRotation;
        }
    }
}
