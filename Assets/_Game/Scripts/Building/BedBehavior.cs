using IslandGame.Player;
using IslandGame.Sky;
using UnityEngine;

namespace IslandGame.Building
{
    /// <summary>
    /// Sleep-to-morning: interacting at night jumps the world clock to the
    /// configured wake time THROUGH TimeOfDayController's public API — no
    /// second clock, no duplicated time math (the jump deliberately fires no
    /// phase events, per that controller's SetTimeOfDay contract; night
    /// systems re-read IsNight, which is also why campfires keep burning
    /// through a skipped night). Daytime interaction just says no, like the
    /// reference games.
    ///
    /// RESPAWN (death phase): a successful sleep also claims this bed as the
    /// player's respawn point — the bed IS the respawn-point object, no
    /// separate marker type. The point is a position beside the bed (so
    /// respawning never wedges the player inside the frame), handed to
    /// PlayerRespawnController, which persists it through the save system.
    /// Sleeping in a different bed simply overwrites it — last bed wins.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class BedBehavior : MonoBehaviour, IFunctionalPlaceable
    {
        [Tooltip("Normalized time of day to wake at (0.30 = early morning, sun up).")]
        [Range(0f, 1f)]
        [SerializeField] private float wakeTime = 0.30f;

        private BuildingPiece piece;
        private TimeOfDayController timeOfDay;

        public BuildingPiece Piece => piece;

        public string InteractionPrompt => "Sleep until morning";

        public void Init(BuildingPiece owner)
        {
            piece = owner;
        }

        public void Interact(GameObject interactor)
        {
            if (timeOfDay == null)
                timeOfDay = FindFirstObjectByType<TimeOfDayController>();

            if (timeOfDay == null)
            {
                Debug.LogWarning("Bed: no TimeOfDayController in the scene — run Island Game/World/Create Day Night Cycle.", this);
                return;
            }

            if (!timeOfDay.IsNight)
            {
                Debug.Log("Bed: you can only sleep at night.");
                return;
            }

            timeOfDay.SetTimeOfDay(wakeTime);
            Debug.Log("Bed: slept until morning.");

            // Death phase: this bed is now home. Spawn beside the bed, facing
            // the way the bed faces — never inside the frame geometry.
            var respawn = interactor.GetComponent<PlayerRespawnController>();
            if (respawn != null)
            {
                Vector3 bedside = transform.position + transform.right * 1.1f + Vector3.up * 0.2f;
                respawn.SetRespawnPoint(bedside, transform.eulerAngles.y);
            }
        }
    }
}
