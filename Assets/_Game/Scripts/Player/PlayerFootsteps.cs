using System;
using UnityEngine;
using UnityEngine.Events;

namespace IslandGame.Player
{
    /// <summary>
    /// Footstep timing via a distance-traveled accumulator: fires an event every
    /// stride-length of actual horizontal movement, plus one on landing. Pure
    /// hook — no audio here. The future audio system subscribes to FootstepTaken
    /// (code) or onFootstep (Inspector) and decides what a step sounds like from
    /// the state, surface, etc. Distance-based timing means carry-weight or wade
    /// slowdowns automatically space steps out, matching the animation.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PlayerFootsteps : MonoBehaviour
    {
        [Serializable]
        public sealed class FootstepUnityEvent : UnityEvent<PlayerState> { }

        [Header("Stride Lengths (meters per step)")]
        [SerializeField] private float walkStride = 0.8f;
        [SerializeField] private float sprintStride = 1.5f;
        [SerializeField] private float crouchStride = 0.55f;
        [SerializeField] private float proneStride = 1.0f; // crawl "steps" — hands and knees shuffling

        [Header("Hooks")]
        [Tooltip("Inspector-assignable hook (audio, VFX, AI noise events later). Receives the movement state the step happened in.")]
        [SerializeField] private FootstepUnityEvent onFootstep = new FootstepUnityEvent();

        /// <summary>Code hook. Argument: the movement state the step happened in.</summary>
        public event Action<PlayerState> FootstepTaken;

        /// <summary>Alternates every step — lets audio pan or pick left/right variations.</summary>
        public bool NextFootIsLeft { get; private set; } = true;

        private PlayerReferences references;
        private float distanceSinceLastStep;

        private void Awake()
        {
            references = GetComponent<PlayerReferences>();
        }

        private void OnEnable()
        {
            references.StateMachine.StateChanged += OnStateChanged;
        }

        private void OnDisable()
        {
            references.StateMachine.StateChanged -= OnStateChanged;
        }

        private void Update()
        {
            PlayerLocomotion locomotion = references.Locomotion;
            PlayerState state = references.StateMachine.CurrentState;

            // No steps without ground contact; water strokes are the future
            // swim-audio system's job, not footsteps.
            if (!locomotion.IsGrounded || state == PlayerState.Swimming || state == PlayerState.Airborne)
            {
                distanceSinceLastStep = 0f;
                return;
            }

            distanceSinceLastStep += locomotion.CurrentSpeed * Time.deltaTime;

            float stride = StrideFor(state);
            if (distanceSinceLastStep >= stride)
            {
                distanceSinceLastStep -= stride;
                EmitFootstep(state);
            }
        }

        private void OnStateChanged(PlayerState previous, PlayerState current)
        {
            // Landing gets an immediate step (the touchdown thud hook) and a
            // fresh accumulator so the next real step isn't mistimed.
            if (previous == PlayerState.Airborne && current != PlayerState.Swimming)
            {
                distanceSinceLastStep = 0f;
                EmitFootstep(current);
            }
        }

        private void EmitFootstep(PlayerState state)
        {
            NextFootIsLeft = !NextFootIsLeft;
            FootstepTaken?.Invoke(state);
            onFootstep.Invoke(state);
        }

        private float StrideFor(PlayerState state)
        {
            switch (state)
            {
                case PlayerState.Sprinting: return sprintStride;
                case PlayerState.Crouching: return crouchStride;
                case PlayerState.Prone: return proneStride;
                default: return walkStride;
            }
        }
    }
}
