using UnityEngine;

namespace IslandGame.Building
{
    /// <summary>
    /// A real hinged door: the piece's ROOT sits at the hinge edge (the
    /// door_hinge socket mates the frame's doorway socket, both authored in
    /// Building Phase 1 for exactly this moment), so toggling is a yaw swing
    /// of the whole placed object — colliders ride along, no special
    /// navigation code. Interact toggles; the swing animates over a fraction
    /// of a second in Update.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class DoorBehavior : MonoBehaviour, IFunctionalPlaceable
    {
        [Tooltip("Yaw swing when open, degrees (negative = swings the other way).")]
        [SerializeField] private float openYaw = -110f;

        [Tooltip("Seconds for a full open/close swing.")]
        [Min(0.05f)]
        [SerializeField] private float swingSeconds = 0.35f;

        private BuildingPiece piece;
        private Quaternion closedRotation;
        private bool initialized;
        private bool open;

        public BuildingPiece Piece => piece;

        public bool IsOpen => open;

        public string InteractionPrompt => open ? "Close door" : "Open door";

        public void Init(BuildingPiece owner)
        {
            piece = owner;
            closedRotation = transform.rotation; // the placed pose IS closed
            initialized = true;
        }

        public void Interact(GameObject interactor)
        {
            if (initialized)
                open = !open;
        }

        private void Update()
        {
            if (!initialized)
                return;

            Quaternion target = open
                ? closedRotation * Quaternion.Euler(0f, openYaw, 0f)
                : closedRotation;

            transform.rotation = Quaternion.RotateTowards(
                transform.rotation, target, Mathf.Abs(openYaw) / swingSeconds * Time.deltaTime);
        }
    }
}
