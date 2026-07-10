using System;
using UnityEngine;

namespace IslandGame.Player
{
    /// <summary>
    /// High-level movement states. Extend by adding members (e.g. Climbing);
    /// transition rules live in PlayerStateMachine.IsTransitionAllowed.
    /// </summary>
    public enum PlayerState
    {
        Idle,
        Walking,
        Sprinting,
        Crouching,
        Prone,
        Swimming,
        Airborne
    }

    /// <summary>
    /// Single source of truth for the player's movement state. Other systems
    /// request transitions via TryChangeState and react through StateChanged;
    /// nobody mutates state directly.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PlayerStateMachine : MonoBehaviour
    {
        public PlayerState CurrentState { get; private set; } = PlayerState.Idle;
        public PlayerState PreviousState { get; private set; } = PlayerState.Idle;

        /// <summary>Seconds spent in the current state.</summary>
        public float TimeInState { get; private set; }

        /// <summary>Raised after a transition. Arguments: (previous, current).</summary>
        public event Action<PlayerState, PlayerState> StateChanged;

        public bool IsGroundedState =>
            CurrentState != PlayerState.Airborne && CurrentState != PlayerState.Swimming;

        /// <summary>
        /// True while the state is owned by core locomotion (walk/sprint/jump).
        /// Crouch, prone and swim systems manage their own transitions, and core
        /// locomotion must not stomp them while they are active.
        /// </summary>
        public bool IsLocomotionDrivenState =>
            CurrentState == PlayerState.Idle
            || CurrentState == PlayerState.Walking
            || CurrentState == PlayerState.Sprinting
            || CurrentState == PlayerState.Airborne;

        private void Update()
        {
            TimeInState += Time.deltaTime;
        }

        /// <summary>
        /// Attempts a state change. Returns false when the transition is a no-op
        /// or disallowed, so callers can react (e.g. deny prone while airborne).
        /// </summary>
        public bool TryChangeState(PlayerState next)
        {
            if (next == CurrentState)
                return false;

            if (!IsTransitionAllowed(CurrentState, next))
                return false;

            PreviousState = CurrentState;
            CurrentState = next;
            TimeInState = 0f;
            StateChanged?.Invoke(PreviousState, CurrentState);
            return true;
        }

        /// <summary>
        /// Centralized transition rules. Later phases extend the switch with new
        /// cases; keep rules here rather than scattered across callers.
        /// </summary>
        private static bool IsTransitionAllowed(PlayerState from, PlayerState to)
        {
            // Prone is a dead end except through crouch: getting up always goes
            // prone -> crouch, and the crouch system continues to stand from there.
            if (from == PlayerState.Prone)
                return to == PlayerState.Crouching;

            // Leaving the water lands in the plain grounded set (shallow exit)
            // or Airborne (swam off an edge); never straight into a stance/sprint.
            if (from == PlayerState.Swimming)
                return to == PlayerState.Idle
                       || to == PlayerState.Walking
                       || to == PlayerState.Airborne;

            switch (to)
            {
                // Prone only from calm grounded stances — never straight from a
                // sprint (decelerate through crouch/walk first), the air or water.
                case PlayerState.Prone:
                    return from == PlayerState.Idle
                           || from == PlayerState.Walking
                           || from == PlayerState.Crouching;

                // Sprint requires standing: the crouch/prone systems must raise
                // the stance (Crouching -> Idle) before sprint can engage.
                case PlayerState.Sprinting:
                    return from != PlayerState.Crouching;

                // Crouching can be entered from any grounded stance and from
                // Airborne (landing with crouch held), but not from water.
                case PlayerState.Crouching:
                    return from != PlayerState.Swimming;

                // Deep water swallows any non-prone state: walking, sprinting,
                // crouching in a rising riverbed, or falling in from the air.
                case PlayerState.Swimming:
                    return true;

                default:
                    return true;
            }
        }
    }
}
