using IslandGame.Inventory;
using IslandGame.Player;
using UnityEngine;

namespace IslandGame.Building
{
    /// <summary>
    /// Interact-to-remove for placed building pieces: the deconstruct button
    /// (middle mouse — Valheim's remove binding — or a "Deconstruct" input
    /// action) removes the aimed piece within reach and refunds part of its
    /// material cost to the inventory; whatever doesn't fit drops as
    /// WorldItems at the piece, the same overflow rule mining uses.
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

        [Tooltip("Fraction of each cost line (linked recipe ingredients, or legacy MaterialCost) returned on removal.")]
        [Range(0f, 1f)]
        [SerializeField] private float refundRatio = 0.5f;

        private PlayerReferences references;
        private InventorySystem inventory;
        private readonly RaycastHit[] rayBuffer = new RaycastHit[16];

        private void Awake()
        {
            references = GetComponent<PlayerReferences>();
            inventory = GetComponent<InventorySystem>();
        }

        private void OnEnable()
        {
            references.InputHandler.DeconstructPressed += TryDeconstruct;
        }

        private void OnDisable()
        {
            references.InputHandler.DeconstructPressed -= TryDeconstruct;
        }

        private void TryDeconstruct()
        {
            if (!PlayerAimRaycast.Raycast(references.CameraPivot, transform, reach, rayBuffer, out RaycastHit hit))
                return;

            var piece = hit.collider.GetComponentInParent<BuildingPiece>();
            if (piece == null)
                return;

            BuildingRefund.Refund(piece, inventory, refundRatio);
            Destroy(piece.gameObject); // OnDestroy unregisters from the registry
        }
    }
}
