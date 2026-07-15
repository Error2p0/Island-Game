using UnityEngine;

namespace IslandGame.Player
{
    /// <summary>
    /// Procedural camera feel: head-bob (speed/state scaled), a spring-based
    /// landing dip, and a sprint FOV push. Owns ONLY the camera's local position
    /// and field of view — look rotation stays with FirstPersonCameraController,
    /// camera world placement stays with the head bone. Subtle by design: the
    /// reference games whisper these effects, they don't shout them.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PlayerCameraEffects : MonoBehaviour
    {
        [Header("Head Bob")]
        [SerializeField] private bool enableHeadBob = true;

        [Tooltip("Vertical bob amplitude at walk speed, meters.")]
        [SerializeField] private float walkBobAmplitude = 0.03f;

        [Tooltip("Vertical bob amplitude at full sprint, meters.")]
        [SerializeField] private float sprintBobAmplitude = 0.035f;

        [Tooltip("Bob amplitude multiplier while crouched. Prone and swimming disable bob entirely.")]
        [Range(0f, 1f)]
        [SerializeField] private float crouchBobMultiplier = 0.5f;

        [Tooltip("Bob cycles per second when moving at walk speed; frequency scales with actual speed.")]
        [SerializeField] private float bobFrequencyAtWalk = 1.6f;

        [Tooltip("How bob frequency grows with speed: 1 = proportional (sprint doubles the rate — frantic), lower = sub-linear like real strides, which lengthen instead of just quickening. 0.6 ≈ sprint bobs 1.5× walk rate.")]
        [Range(0.3f, 1f)]
        [SerializeField] private float bobFrequencySpeedExponent = 0.6f;

        [Tooltip("Lateral sway as a fraction of the vertical amplitude.")]
        [Range(0f, 1f)]
        [SerializeField] private float bobHorizontalFactor = 0.5f;

        [Tooltip("How fast bob amplitude blends when starting/stopping/changing state (1/s).")]
        [SerializeField] private float bobBlendSpeed = 6f;

        [Header("Landing Dip")]
        [Tooltip("Meters of camera dip per m/s of landing impact speed.")]
        [SerializeField] private float dipPerImpactSpeed = 0.012f;

        [SerializeField] private float maxDip = 0.18f;

        [Tooltip("Falls slower than this don't dip (walking down steps shouldn't nod the camera).")]
        [SerializeField] private float minImpactSpeed = 4f;

        [Tooltip("Spring stiffness pulling the dip back to zero.")]
        [SerializeField] private float dipSpring = 90f;

        [Tooltip("Spring damping — slightly under critical for one soft bounce.")]
        [SerializeField] private float dipDamping = 14f;

        [Header("Sprint FOV")]
        [Tooltip("Degrees added to the base FOV while sprinting (half while fast-stroke swimming).")]
        [SerializeField] private float sprintFovAdd = 8f;

        [SerializeField] private float fovBlendSpeed = 6f;

        private PlayerReferences references;
        private Transform cameraTransform;
        private Camera playerCamera;
        private float baseFov;

        private float bobPhase;
        private float bobAmplitude;
        private float dipOffset;      // negative while dipped
        private float dipVelocity;
        private bool wasGrounded = true;
        private float previousVerticalVelocity;
        private Vector3 externalSway;

        /// <summary>
        /// Visual-only additive camera offset for environmental effects
        /// (storm wind sway — weather phase). This component stays the ONLY
        /// writer of the camera's local position; environment systems feed
        /// their offset through here instead of fighting over the transform.
        /// The owner sets it every frame (and zero when calm) — keep it tiny
        /// (a couple of centimeters), it composes with bob and dip.
        /// </summary>
        public void SetExternalSway(Vector3 localOffset)
        {
            externalSway = localOffset;
        }

        private void Awake()
        {
            references = GetComponent<PlayerReferences>();
        }

        private void Start()
        {
            playerCamera = references.PlayerCamera;
            cameraTransform = playerCamera.transform;
            baseFov = playerCamera.fieldOfView;
        }

        private void LateUpdate()
        {
            float deltaTime = Time.deltaTime;
            PlayerLocomotion locomotion = references.Locomotion;
            PlayerState state = references.StateMachine.CurrentState;

            UpdateLandingDip(locomotion, state, deltaTime);
            UpdateHeadBob(locomotion, state, deltaTime);
            UpdateFov(locomotion, deltaTime);

            cameraTransform.localPosition = new Vector3(
                Mathf.Sin(bobPhase) * bobAmplitude * bobHorizontalFactor + externalSway.x,
                Mathf.Sin(bobPhase * 2f) * bobAmplitude + dipOffset + externalSway.y,
                externalSway.z);
        }

        private void UpdateLandingDip(PlayerLocomotion locomotion, PlayerState state, float deltaTime)
        {
            bool grounded = locomotion.IsGrounded;

            // Impact detection uses LAST frame's vertical velocity — by the time
            // grounded flips true, the current value is already the stick force.
            if (grounded && !wasGrounded && state != PlayerState.Swimming)
            {
                float impactSpeed = Mathf.Abs(previousVerticalVelocity);
                if (impactSpeed >= minImpactSpeed)
                {
                    float depth = Mathf.Min(impactSpeed * dipPerImpactSpeed, maxDip);
                    dipVelocity -= depth * 10f; // impulse; the spring below recovers it
                }
            }

            wasGrounded = grounded;
            previousVerticalVelocity = locomotion.VerticalVelocity;

            dipOffset += dipVelocity * deltaTime;
            dipVelocity += (-dipOffset * dipSpring - dipVelocity * dipDamping) * deltaTime;
        }

        private void UpdateHeadBob(PlayerLocomotion locomotion, PlayerState state, float deltaTime)
        {
            float speed = locomotion.CurrentSpeed;
            float targetAmplitude = 0f;

            if (enableHeadBob && locomotion.IsGrounded && speed > 0.2f)
            {
                switch (state)
                {
                    case PlayerState.Sprinting:
                        targetAmplitude = sprintBobAmplitude;
                        break;
                    case PlayerState.Walking:
                    case PlayerState.Idle: // decelerating remnants
                        targetAmplitude = walkBobAmplitude * Mathf.Clamp01(speed / locomotion.WalkSpeed);
                        break;
                    case PlayerState.Crouching:
                        targetAmplitude = walkBobAmplitude * crouchBobMultiplier
                                          * Mathf.Clamp01(speed / locomotion.CrouchSpeed);
                        break;
                    // Prone, Swimming, Airborne: no bob.
                }
            }

            bobAmplitude = Mathf.Lerp(bobAmplitude, targetAmplitude, bobBlendSpeed * deltaTime);

            // Frequency follows actual speed (so carry-weight slowdowns slow the
            // bob with the stride), but sub-linearly — sprinting lengthens the
            // stride rather than doubling the cadence.
            float frequency = bobFrequencyAtWalk * Mathf.Pow(Mathf.Max(0f, speed / locomotion.WalkSpeed), bobFrequencySpeedExponent);
            bobPhase += frequency * 2f * Mathf.PI * deltaTime;
            if (bobPhase > 1000f)
                bobPhase -= 1000f; // keep the accumulator small on long sessions
        }

        private void UpdateFov(PlayerLocomotion locomotion, float deltaTime)
        {
            float targetFov = baseFov;
            if (locomotion.IsSprinting)
                targetFov += sprintFovAdd;
            else if (locomotion.IsSwimFastStroke)
                targetFov += sprintFovAdd * 0.5f;

            playerCamera.fieldOfView = Mathf.Lerp(playerCamera.fieldOfView, targetFov, fovBlendSpeed * deltaTime);
        }
    }
}
