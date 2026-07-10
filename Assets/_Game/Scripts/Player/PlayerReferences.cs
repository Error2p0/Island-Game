using UnityEngine;

namespace IslandGame.Player
{
    /// <summary>
    /// Central reference hub for the player rig. Every other player script pulls
    /// its dependencies from here instead of scattering GetComponent calls around.
    /// Fields are wired by the rig builder at edit time; ResolveReferences() is a
    /// safety net that fills in anything left unassigned.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PlayerReferences : MonoBehaviour
    {
        [Header("Components")]
        [SerializeField] private CharacterController controller;
        [SerializeField] private Animator animator;
        [SerializeField] private PlayerInputHandler inputHandler;
        [SerializeField] private PlayerStateMachine stateMachine;
        [SerializeField] private PlayerLocomotion locomotion;
        [SerializeField] private PlayerCrouch crouch;
        [SerializeField] private PlayerProne prone;
        [SerializeField] private PlayerAnimationController animationController;
        [SerializeField] private PlayerFootGrounder footGrounder;
        [SerializeField] private PlayerCameraEffects cameraEffects;
        [SerializeField] private PlayerFootsteps footsteps;
        [SerializeField] private FirstPersonCameraController cameraController;

        [Header("Camera")]
        [SerializeField] private Camera playerCamera;
        [SerializeField] private Transform cameraPivot;

        [Header("Key Bones")]
        [SerializeField] private Transform hipsBone;
        [SerializeField] private Transform headBone;

        public CharacterController Controller => controller;
        public Animator Animator => animator;
        public PlayerInputHandler InputHandler => inputHandler;
        public PlayerStateMachine StateMachine => stateMachine;
        public PlayerLocomotion Locomotion => locomotion;
        public PlayerCrouch Crouch => crouch;
        public PlayerProne Prone => prone;
        public PlayerAnimationController AnimationController => animationController;
        public PlayerFootGrounder FootGrounder => footGrounder;
        public PlayerCameraEffects CameraEffects => cameraEffects;
        public PlayerFootsteps Footsteps => footsteps;
        public FirstPersonCameraController CameraController => cameraController;
        public Camera PlayerCamera => playerCamera;
        public Transform CameraPivot => cameraPivot;
        public Transform HipsBone => hipsBone;
        public Transform HeadBone => headBone;

        private void Awake()
        {
            ResolveReferences();
        }

        /// <summary>
        /// Fills in any unassigned references. Public so the editor rig builder
        /// (and future tooling) can wire the component outside play mode.
        /// </summary>
        public void ResolveReferences()
        {
            if (controller == null) controller = GetComponent<CharacterController>();
            if (animator == null) animator = GetComponent<Animator>();
            if (inputHandler == null) inputHandler = GetComponent<PlayerInputHandler>();
            if (stateMachine == null) stateMachine = GetComponent<PlayerStateMachine>();
            if (locomotion == null) locomotion = GetComponent<PlayerLocomotion>();
            if (crouch == null) crouch = GetComponent<PlayerCrouch>();
            if (prone == null) prone = GetComponent<PlayerProne>();
            if (animationController == null) animationController = GetComponent<PlayerAnimationController>();
            if (footGrounder == null) footGrounder = GetComponent<PlayerFootGrounder>();
            if (cameraEffects == null) cameraEffects = GetComponent<PlayerCameraEffects>();
            if (footsteps == null) footsteps = GetComponent<PlayerFootsteps>();
            if (cameraController == null) cameraController = GetComponent<FirstPersonCameraController>();

            if (playerCamera == null) playerCamera = GetComponentInChildren<Camera>(true);
            if (cameraPivot == null) cameraPivot = FindDeep(transform, "CameraPivot");
            if (hipsBone == null) hipsBone = FindDeep(transform, "Hips");
            if (headBone == null) headBone = FindDeep(transform, "Head");

            ReportIfMissing(controller, nameof(controller));
            ReportIfMissing(animator, nameof(animator));
            ReportIfMissing(inputHandler, nameof(inputHandler));
            ReportIfMissing(stateMachine, nameof(stateMachine));
            ReportIfMissing(locomotion, nameof(locomotion));
            ReportIfMissing(crouch, nameof(crouch));
            ReportIfMissing(prone, nameof(prone));
            ReportIfMissing(animationController, nameof(animationController));
            ReportIfMissing(footGrounder, nameof(footGrounder));
            ReportIfMissing(cameraEffects, nameof(cameraEffects));
            ReportIfMissing(footsteps, nameof(footsteps));
            ReportIfMissing(cameraController, nameof(cameraController));
            ReportIfMissing(playerCamera, nameof(playerCamera));
            ReportIfMissing(cameraPivot, nameof(cameraPivot));
            ReportIfMissing(hipsBone, nameof(hipsBone));
            ReportIfMissing(headBone, nameof(headBone));
        }

        private void ReportIfMissing(Object reference, string fieldName)
        {
            if (reference == null)
                Debug.LogError($"[PlayerReferences] '{fieldName}' could not be resolved on '{name}'.", this);
        }

        private static Transform FindDeep(Transform parent, string childName)
        {
            for (int i = 0; i < parent.childCount; i++)
            {
                Transform child = parent.GetChild(i);
                if (child.name == childName)
                    return child;

                Transform found = FindDeep(child, childName);
                if (found != null)
                    return found;
            }

            return null;
        }
    }
}
