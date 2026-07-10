using IslandGame.Player;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace IslandGame.Inventory.UI
{
    /// <summary>
    /// Root of the inventory uGUI: owns the open/closed state (Tab or I via
    /// PlayerInputHandler), the cursor lock, gameplay input suppression, and
    /// the shared drag operation (ghost icon following the pointer, slot→slot
    /// completion, drop-into-world when released outside the UI).
    ///
    /// While open it re-asserts the unlocked cursor every frame, because the
    /// camera controller re-locks on application focus and must keep doing so
    /// for normal gameplay — this is the cheaper side to make idempotent.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class InventoryUIController : MonoBehaviour
    {
        [Header("Scene References (wired by the UI builder)")]
        [SerializeField] private InventorySystem inventory;
        [SerializeField] private PlayerReferences playerReferences;

        [Header("View References (wired by the UI builder)")]
        [SerializeField] private GameObject inventoryPanel;
        [SerializeField] private InventoryTooltip tooltip;
        [SerializeField] private RectTransform dragGhost;
        [SerializeField] private Image dragGhostIcon;
        [SerializeField] private Text dragGhostCount;

        public bool IsOpen { get; private set; }

        public InventorySystem Inventory => inventory;
        public InventoryTooltip Tooltip => tooltip;

        /// <summary>Slot index a drag started from; -1 when no drag is active.</summary>
        public int DragSourceIndex { get; private set; } = -1;

        private bool dragCompletedOnSlot;

        private void Start()
        {
            inventoryPanel.SetActive(false);
            dragGhost.gameObject.SetActive(false);
        }

        private void OnEnable()
        {
            if (playerReferences != null && playerReferences.InputHandler != null)
                playerReferences.InputHandler.InventoryTogglePressed += Toggle;
        }

        private void OnDisable()
        {
            if (playerReferences != null && playerReferences.InputHandler != null)
                playerReferences.InputHandler.InventoryTogglePressed -= Toggle;
        }

        private void Update()
        {
            if (IsOpen)
                UIInputFocus.EnforceCursor();
        }

        public void Toggle()
        {
            IsOpen = !IsOpen;
            inventoryPanel.SetActive(IsOpen);

            // Focus is refcounted so this panel and the creative menu (and any
            // future screen) can open/close in any order without stomping the
            // cursor or the gameplay-input block.
            if (IsOpen)
                UIInputFocus.Acquire(playerReferences.InputHandler);
            else
                UIInputFocus.Release(playerReferences.InputHandler);

            tooltip.Hide();
            CancelDrag();
        }

        // ------------------------------------------------------------------
        // Drag operation (slot views call in; state lives here once)
        // ------------------------------------------------------------------

        public void BeginDrag(int slotIndex, Vector2 pointerPosition)
        {
            InventorySlot slot = inventory.GetSlot(slotIndex);
            if (!IsOpen || slot.IsEmpty)
                return;

            DragSourceIndex = slotIndex;
            dragCompletedOnSlot = false;

            dragGhostIcon.sprite = slot.Item.Icon;
            dragGhostIcon.color = slot.Item.Icon != null ? Color.white : new Color(1f, 1f, 1f, 0.35f);
            dragGhostCount.text = slot.Count > 1 ? slot.Count.ToString() : string.Empty;
            dragGhost.gameObject.SetActive(true);
            dragGhost.position = pointerPosition;

            tooltip.Hide();
        }

        public void UpdateDrag(Vector2 pointerPosition)
        {
            if (DragSourceIndex >= 0)
                dragGhost.position = pointerPosition;
        }

        /// <summary>Called by the slot view under the pointer on release (fires before the source's EndDrag).</summary>
        public void CompleteDragOn(int targetSlotIndex)
        {
            if (DragSourceIndex < 0)
                return;

            inventory.MoveOrMergeSlot(DragSourceIndex, targetSlotIndex);
            dragCompletedOnSlot = true;
        }

        /// <summary>Called by the source slot view when the drag ends anywhere.</summary>
        public void EndDrag(PointerEventData eventData)
        {
            if (DragSourceIndex < 0)
                return;

            // Released over no slot: outside the UI entirely = throw the whole
            // stack into the world; over panel chrome = snap back (do nothing).
            if (!dragCompletedOnSlot && eventData.pointerCurrentRaycast.gameObject == null)
            {
                InventorySlot source = inventory.GetSlot(DragSourceIndex);
                if (!source.IsEmpty)
                    inventory.DropFromSlot(DragSourceIndex, source.Count);
            }

            CancelDrag();
        }

        private void CancelDrag()
        {
            DragSourceIndex = -1;
            dragCompletedOnSlot = false;
            dragGhost.gameObject.SetActive(false);
        }
    }
}
