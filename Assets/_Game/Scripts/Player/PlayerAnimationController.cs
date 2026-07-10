using UnityEngine;

namespace IslandGame.Player
{
    /// <summary>
    /// Bridge between gameplay and the Animator: reads PlayerStateMachine /
    /// PlayerLocomotion every frame and writes Animator parameters — no gameplay
    /// logic lives here, and no animation logic lives in locomotion.
    /// Foot grounding is handled separately by PlayerFootGrounder (bone-based,
    /// in LateUpdate) — this component does not use Mecanim's IK pass.
    ///
    /// All parameters are derived from ACTUAL velocity, not input, so anything
    /// that scales movement speed (carry weight, wading, stance blends) scales
    /// the animation automatically — a half-speed walk blends halfway to idle
    /// instead of playing a full-rate walk cycle in place.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PlayerAnimationController : MonoBehaviour
    {
        [Header("Parameter Damping")]
        [Tooltip("Seconds of smoothing on the blend-tree floats. Keeps transitions fluid without feeling laggy.")]
        [SerializeField] private float floatDampTime = 0.1f;

        [Header("Airborne")]
        [Tooltip("Descents shorter than this never show the Fall state — walking down 1-block voxel steps (~0.2 s airborne) keeps the walk cycle playing. Jumps (upward velocity) always show airborne immediately.")]
        [SerializeField] private float fallAnimationDelay = 0.3f;

        private static readonly int MoveXHash = Animator.StringToHash("MoveX");
        private static readonly int MoveZHash = Animator.StringToHash("MoveZ");
        private static readonly int Speed01Hash = Animator.StringToHash("Speed01");
        private static readonly int IsCrouchingHash = Animator.StringToHash("IsCrouching");
        private static readonly int IsProneHash = Animator.StringToHash("IsProne");
        private static readonly int IsSwimmingHash = Animator.StringToHash("IsSwimming");
        private static readonly int IsGroundedHash = Animator.StringToHash("IsGrounded");
        private static readonly int VerticalVelocityHash = Animator.StringToHash("VerticalVelocity");

        private PlayerReferences references;
        private Animator animator;
        private float ungroundedTime;

        private void Awake()
        {
            references = GetComponent<PlayerReferences>();
            animator = GetComponent<Animator>();
        }

        private void Update()
        {
            if (animator.runtimeAnimatorController == null)
                return;

            PlayerLocomotion locomotion = references.Locomotion;
            PlayerState state = references.StateMachine.CurrentState;
            float deltaTime = Time.deltaTime;

            // Strafe-relative velocity, normalized so walk speed = 1 and sprint
            // speed lands at its blend-tree position (sprintSpeed / walkSpeed).
            Vector3 localVelocity = transform.InverseTransformDirection(locomotion.Velocity);
            animator.SetFloat(MoveXHash, localVelocity.x / locomotion.WalkSpeed, floatDampTime, deltaTime);
            animator.SetFloat(MoveZHash, localVelocity.z / locomotion.WalkSpeed, floatDampTime, deltaTime);

            animator.SetFloat(Speed01Hash, ComputeStanceSpeed01(state, locomotion), floatDampTime, deltaTime);

            animator.SetBool(IsCrouchingHash, state == PlayerState.Crouching);
            animator.SetBool(IsProneHash, state == PlayerState.Prone);
            animator.SetBool(IsSwimmingHash, state == PlayerState.Swimming);
            animator.SetBool(IsGroundedHash, ComputeAnimatorGrounded(locomotion, deltaTime));
            animator.SetFloat(VerticalVelocityHash, locomotion.VerticalVelocity);
        }

        /// <summary>
        /// What the ANIMATOR should treat as grounded: gameplay grounding, plus
        /// a short mask over downward airtime so walking down voxel-terrain
        /// steps (a real but brief mini-fall) never flickers into the Fall/Land
        /// states mid-walk-cycle. Ascending (a jump) bypasses the mask so the
        /// Jump state still fires on launch. Gameplay logic keeps using the
        /// locomotion's own grounding untouched.
        /// </summary>
        private bool ComputeAnimatorGrounded(PlayerLocomotion locomotion, float deltaTime)
        {
            if (locomotion.IsEffectivelyGrounded)
            {
                ungroundedTime = 0f;
                return true;
            }

            ungroundedTime += deltaTime;
            return locomotion.VerticalVelocity <= 0f && ungroundedTime < fallAnimationDelay;
        }

        /// <summary>
        /// 0-1 intensity for the crouch/prone/swim blend trees, normalized by
        /// that mode's own top speed so carry-weight slowdowns read visually.
        /// </summary>
        private static float ComputeStanceSpeed01(PlayerState state, PlayerLocomotion locomotion)
        {
            switch (state)
            {
                case PlayerState.Crouching:
                    return Mathf.Clamp01(locomotion.CurrentSpeed / locomotion.CrouchSpeed);
                case PlayerState.Prone:
                    return Mathf.Clamp01(locomotion.CurrentSpeed / locomotion.ProneSpeed);
                case PlayerState.Swimming:
                    return Mathf.Clamp01(locomotion.Velocity.magnitude / locomotion.FastSwimSpeed);
                default:
                    return Mathf.Clamp01(locomotion.CurrentSpeed / locomotion.SprintSpeed);
            }
        }
    }
}
