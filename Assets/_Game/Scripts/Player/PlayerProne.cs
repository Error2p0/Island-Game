using UnityEngine;

namespace IslandGame.Player
{
    /// <summary>
    /// Owns the prone stance: enter/exit rules (staged exit through crouch with
    /// clearance checks at each height), the prone blend, and the pose targets
    /// PlayerCrouch layers onto the rig. PlayerCrouch remains the single writer
    /// of CharacterController dimensions and bone poses so the two stances can
    /// never fight over them; locomotion reads ProneBlend01 for crawl speed.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PlayerProne : MonoBehaviour
    {
        [Header("Dimensions")]
        [Tooltip("CharacterController height while prone. Cannot go below 2x the controller radius (0.7 for the default 0.35 radius).")]
        [SerializeField] private float proneControllerHeight = 0.7f;

        [Tooltip("Seconds for the full stand <-> prone blend. Slower than crouch — dropping flat is a deliberate move.")]
        [SerializeField] private float transitionSeconds = 0.35f;

        [Header("Placeholder Pose (removed when real animations land in Phase 6)")]
        [Tooltip("Hips local height at full prone.")]
        [SerializeField] private float hipsLocalHeight = 0.2f;

        [Tooltip("Hips shift backwards so the head (and camera) stays inside the controller radius instead of poking through walls ahead.")]
        [SerializeField] private float hipsLocalBack = 0.3f;

        [Tooltip("Torso incline from vertical, degrees. 90 = fully flat; less keeps the head raised sphinx-style for a usable near-ground camera.")]
        [SerializeField] private float torsoInclineAngle = 65f;

        [Tooltip("Extra leg rotation so the legs lie flat behind the body instead of digging into the ground.")]
        [SerializeField] private float legCounterAngle = 23f;

        [Tooltip("Arms fold to the sides of the body at full prone.")]
        [SerializeField] private float armFoldAngle = 80f;

        [Header("Edge Cases")]
        [Tooltip("Losing ground contact for this long while prone (slid off a slope/ledge) auto-exits to crouch so the character doesn't fall while lying flat.")]
        [SerializeField] private float airborneExitDelay = 0.25f;

        /// <summary>Stance flag. True from prone entry until a clear exit to crouch begins.</summary>
        public bool IsProne { get; private set; }

        /// <summary>0 = not prone, 1 = fully prone. Read by PlayerCrouch (dimensions/pose), locomotion (speed) and the camera (look limits).</summary>
        public float ProneBlend01 => proneBlend;

        public float ProneControllerHeight => proneControllerHeight;

        // Pose targets consumed by PlayerCrouch.ApplyPose. All are local-space,
        // composed on top of the rig's default pose.
        public Vector3 HipsPoseLocalPosition => new Vector3(0f, hipsLocalHeight, -hipsLocalBack);
        public Quaternion HipsPoseLocalRotation => Quaternion.Euler(torsoInclineAngle, 0f, 0f);
        public Quaternion ThighPoseLocalRotation => Quaternion.Euler(legCounterAngle, 0f, 0f);
        public Quaternion LeftArmPoseLocalRotation => Quaternion.Euler(0f, 0f, armFoldAngle);
        public Quaternion RightArmPoseLocalRotation => Quaternion.Euler(0f, 0f, -armFoldAngle);

        private PlayerReferences references;
        private float proneBlend;
        private bool pendingExit;
        private float airborneTime;

        private void Awake()
        {
            references = GetComponent<PlayerReferences>();
        }

        private void OnEnable()
        {
            references.InputHandler.PronePressed += OnPronePressed;
            references.InputHandler.CrouchPressed += OnCrouchPressed;
        }

        private void OnDisable()
        {
            references.InputHandler.PronePressed -= OnPronePressed;
            references.InputHandler.CrouchPressed -= OnCrouchPressed;
        }

        private void Update()
        {
            // If some other system forced the state away from Prone, drop the stance.
            if (IsProne && references.StateMachine.CurrentState != PlayerState.Prone)
            {
                IsProne = false;
                pendingExit = false;
            }

            // A blocked exit keeps retrying until the crouch-height space clears,
            // mirroring the crouch component's blocked stand-up behavior.
            if (pendingExit)
                TryExitProne();

            // Slid off a slope/ledge while flat: hand off to crouch (whose
            // Airborne handling then takes over) instead of falling while prone.
            if (IsProne && !references.Locomotion.IsEffectivelyGrounded)
            {
                airborneTime += Time.deltaTime;
                if (airborneTime >= airborneExitDelay)
                    TryExitProne();
            }
            else
            {
                airborneTime = 0f;
            }

            proneBlend = Mathf.MoveTowards(proneBlend, IsProne ? 1f : 0f, Time.deltaTime / transitionSeconds);
        }

        private void OnPronePressed()
        {
            if (IsProne)
            {
                if (pendingExit)
                    pendingExit = false; // second press cancels a queued exit — stay prone
                else
                    TryExitProne();
            }
            else
            {
                TryEnterProne();
            }
        }

        private void OnCrouchPressed()
        {
            if (IsProne)
                TryExitProne();
        }

        private void TryEnterProne()
        {
            PlayerStateMachine stateMachine = references.StateMachine;

            // Prone only from calm grounded stances. Sprinting players must
            // decelerate through crouch (or walking) first — no dive-to-prone.
            bool stateAllows = stateMachine.CurrentState == PlayerState.Idle
                               || stateMachine.CurrentState == PlayerState.Walking
                               || stateMachine.CurrentState == PlayerState.Crouching;

            // Heavy loads (carrying a log etc.) block going prone entirely.
            if (!stateAllows || !references.Locomotion.IsGrounded || !references.Locomotion.ProneAllowedByLoad)
                return;

            if (stateMachine.TryChangeState(PlayerState.Prone))
            {
                IsProne = true;
                pendingExit = false;
            }
        }

        /// <summary>
        /// Stage 1 of getting up: prone -> crouch, gated on clearance at crouch
        /// height. Stage 2 (crouch -> stand) is owned by PlayerCrouch, which
        /// re-checks clearance at full standing height — so a full stand-up only
        /// happens when both stages are clear, and the player parks at crouch
        /// when only the lower stage is.
        /// </summary>
        private void TryExitProne()
        {
            if (!IsProne)
                return;

            if (!references.Crouch.HasClearance(references.Crouch.CrouchedControllerHeight))
            {
                pendingExit = true;
                return;
            }

            IsProne = false;
            pendingExit = false;

            // Hand the stance to the crouch system. If the crouch key isn't held
            // and standing headroom is clear, it will smoothly continue to stand.
            references.Crouch.ForceCrouchStance();
            references.StateMachine.TryChangeState(PlayerState.Crouching);
        }
    }
}
