using IslandGame.Building;
using UnityEngine;
using UnityEngine.UI;

namespace IslandGame.Player.UI
{
    /// <summary>
    /// The "your stuff is over there" HUD marker: a small icon + distance
    /// label projected onto the screen at the dropped-loot location, clamped
    /// to the screen edge when the gravestone is off-screen or behind the
    /// camera — the reference games' convention of punishing death without
    /// hiding where the punishment went.
    ///
    /// Clears itself once the target's chest has been emptied (or the target
    /// is gone). Session-scoped by design: the gravestone itself persists
    /// through the save system, but a fresh session starts markerless —
    /// worth revisiting only if playtests say corpse runs get lost.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class DeathMarkerView : MonoBehaviour
    {
        [Header("Wired by the builder")]
        [SerializeField] private RectTransform markerRoot;
        [SerializeField] private Text distanceText;

        [Tooltip("Screen-edge margin, pixels, when clamping an off-screen marker.")]
        [Range(10f, 120f)]
        [SerializeField] private float edgeMargin = 48f;

        [Tooltip("Seconds between emptied-chest checks (the projection itself runs every frame).")]
        [Range(0.1f, 2f)]
        [SerializeField] private float emptyCheckInterval = 0.5f;

        private Transform target;
        private ChestBehavior targetChest;
        private PlayerReferences player;
        private float nextEmptyCheckTime;

        /// <summary>Points the marker at the dropped loot (called by the respawn controller on death).</summary>
        public void SetTarget(Transform lootTarget)
        {
            target = lootTarget;
            targetChest = lootTarget != null ? lootTarget.GetComponentInChildren<ChestBehavior>(true) : null;
        }

        public void ClearTarget()
        {
            target = null;
            targetChest = null;
            if (markerRoot != null)
                markerRoot.gameObject.SetActive(false);
        }

        private void LateUpdate()
        {
            if (markerRoot == null)
                return;

            if (target == null)
            {
                if (markerRoot.gameObject.activeSelf)
                    markerRoot.gameObject.SetActive(false);
                return;
            }

            // Recovered? Marker's job is done.
            if (Time.time >= nextEmptyCheckTime)
            {
                nextEmptyCheckTime = Time.time + emptyCheckInterval;
                if (IsChestEmpty())
                {
                    ClearTarget();
                    return;
                }
            }

            Camera playerCamera = ResolveCamera();
            if (playerCamera == null)
                return;

            if (!markerRoot.gameObject.activeSelf)
                markerRoot.gameObject.SetActive(true);

            Vector3 worldPoint = target.position + Vector3.up * 1.2f;
            Vector3 screenPoint = playerCamera.WorldToScreenPoint(worldPoint);

            // Behind the camera: mirror onto the screen edge so the arrow
            // still says "turn around", instead of projecting nonsense.
            bool behind = screenPoint.z < 0f;
            if (behind)
            {
                screenPoint.x = Screen.width - screenPoint.x;
                screenPoint.y = edgeMargin;
            }

            screenPoint.x = Mathf.Clamp(screenPoint.x, edgeMargin, Screen.width - edgeMargin);
            screenPoint.y = Mathf.Clamp(screenPoint.y, edgeMargin, Screen.height - edgeMargin);
            markerRoot.position = new Vector3(screenPoint.x, screenPoint.y, 0f);

            if (distanceText != null)
            {
                float distance = Vector3.Distance(playerCamera.transform.position, worldPoint);
                distanceText.text = $"Your remains · {distance:0} m";
            }
        }

        private bool IsChestEmpty()
        {
            if (targetChest == null || targetChest.Storage == null)
                return true; // no storage to recover from — nothing to point at

            var storage = targetChest.Storage;
            for (int i = 0; i < storage.SlotCount; i++)
            {
                if (!storage.GetSlot(i).IsEmpty)
                    return false;
            }

            return true;
        }

        private Camera ResolveCamera()
        {
            if (player == null)
                player = FindFirstObjectByType<PlayerReferences>();

            return player != null ? player.PlayerCamera : null;
        }
    }
}
