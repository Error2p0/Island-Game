using IslandGame.Building.UI;
using IslandGame.Inventory;
using IslandGame.Player;
using UnityEngine;

namespace IslandGame.Building
{
    /// <summary>
    /// Hold-to-remove for placed building pieces: holding the deconstruct
    /// button (middle mouse — Valheim's remove binding — or a "Deconstruct"
    /// input action) on a piece within reach runs a timer with a progress bar
    /// under the crosshair (DeconstructProgressView); the piece is removed
    /// and refunded ONLY on completion. Releasing early, aiming away,
    /// switching to another piece, or leaving reach cancels and fully resets
    /// the progress — deconstruction has no partial state, and a menu opening
    /// mid-hold cancels too (DeconstructHeld is gated by GameplayBlocked).
    ///
    /// DURATION is one flat serialized value for every piece — predictability
    /// over simulation: build costs are all the same order of magnitude, so
    /// cost-scaled timers would add variance without communicating anything.
    ///
    /// REFUND is Refund Ratio × what the piece actually costs, computed by
    /// the shared BuildingRefund helper (damage-destruction drops through the
    /// same math, so the two removal paths can never drift apart).
    ///
    /// Registry cleanup needs no code here: destroying the piece unregisters
    /// it via BuildingPiece.OnDestroy, the same path damage-destruction takes.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(PlayerReferences))]
    public sealed class BuildingDeconstructor : MonoBehaviour
    {
        [Tooltip("Maximum deconstruct distance from the camera, meters.")]
        [SerializeField] private float reach = 5f;

        [Tooltip("Seconds the deconstruct button must be held on one piece before it is removed.")]
        [Min(0.1f)]
        [SerializeField] private float deconstructSeconds = 1.5f;

        [Tooltip("Fraction of each cost line (linked recipe ingredients, or legacy MaterialCost) returned on removal.")]
        [Range(0f, 1f)]
        [SerializeField] private float refundRatio = 0.5f;

        private PlayerReferences references;
        private InventorySystem inventory;
        private readonly RaycastHit[] rayBuffer = new RaycastHit[16];

        private BuildingPiece targetPiece;
        private float holdSeconds;

        private void Awake()
        {
            references = GetComponent<PlayerReferences>();
            inventory = GetComponent<InventorySystem>();
        }

        private void Update()
        {
            if (!references.InputHandler.DeconstructHeld)
            {
                ResetHold();
                return;
            }

            BuildingPiece aimed = AimedPiece();
            if (aimed == null)
            {
                ResetHold();
                return;
            }

            if (aimed != targetPiece)
            {
                // Progress belongs to one piece: switching targets starts over.
                targetPiece = aimed;
                holdSeconds = 0f;
            }

            holdSeconds += Time.deltaTime;
            if (holdSeconds >= deconstructSeconds)
            {
                BuildingPiece finished = targetPiece;
                ResetHold();
                BuildingRefund.Refund(finished, inventory, refundRatio);
                Destroy(finished.gameObject); // OnDestroy unregisters from the registry
                return;
            }

            string pieceName = targetPiece.Definition != null
                ? targetPiece.Definition.DisplayName
                : targetPiece.name;
            DeconstructProgressView.GetOrCreate().Show(pieceName, holdSeconds / deconstructSeconds);
        }

        private void OnDisable()
        {
            ResetHold();
        }

        private void ResetHold()
        {
            targetPiece = null;
            holdSeconds = 0f;
            DeconstructProgressView.HideIfAlive();
        }

        private BuildingPiece AimedPiece()
        {
            if (!PlayerAimRaycast.Raycast(references.CameraPivot, transform, reach, rayBuffer, out RaycastHit hit))
                return null;

            return hit.collider.GetComponentInParent<BuildingPiece>();
        }
    }
}
