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
        }
    }
}
