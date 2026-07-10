using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace IslandGame.Inventory.UI
{
    /// <summary>
    /// One slot cell (hotbar or grid): icon, stack count, optional selection
    /// highlight, plus all pointer interaction — tooltip on hover, left-drag to
    /// move/merge/swap (drop outside the UI to throw away), right-click to
    /// split half into the first empty slot. Instantiated from the builder's
    /// slot template by HotbarView/InventoryGridView and bound to a slot index.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class InventorySlotView : MonoBehaviour,
        IPointerEnterHandler, IPointerExitHandler, IPointerClickHandler,
        IBeginDragHandler, IDragHandler, IEndDragHandler, IDropHandler
    {
        [Header("Wired by the UI builder (on the template)")]
        [SerializeField] private Image icon;
        [SerializeField] private Text countText;
        [SerializeField] private Image selectionHighlight;

        private InventoryUIController controller;
        private int slotIndex = -1;

        public int SlotIndex => slotIndex;

        public void Bind(InventoryUIController owner, int index)
        {
            controller = owner;
            slotIndex = index;
            Refresh();
        }

        public void Refresh()
        {
            InventorySlot slot = controller.Inventory.GetSlot(slotIndex);

            if (slot.IsEmpty)
            {
                icon.enabled = false;
                countText.text = string.Empty;
                return;
            }

            icon.enabled = true;
            icon.sprite = slot.Item.Icon;
            // Icon-less items draw the raw white square dimmed — visible and
            // draggable rather than invisible; the tooltip carries the name.
            icon.color = slot.Item.Icon != null ? Color.white : new Color(1f, 1f, 1f, 0.35f);
            countText.text = slot.Count > 1 ? slot.Count.ToString() : string.Empty;
        }

        public void SetSelected(bool selected)
        {
            if (selectionHighlight != null)
                selectionHighlight.enabled = selected;
        }

        // ------------------------------------------------------------------
        // Pointer interaction
        // ------------------------------------------------------------------

        public void OnPointerEnter(PointerEventData eventData)
        {
            if (!controller.IsOpen || controller.DragSourceIndex >= 0)
                return;

            InventorySlot slot = controller.Inventory.GetSlot(slotIndex);
            if (!slot.IsEmpty)
                controller.Tooltip.Show(slot);
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            controller.Tooltip.Hide();
        }

        public void OnPointerClick(PointerEventData eventData)
        {
            if (!controller.IsOpen || eventData.button != PointerEventData.InputButton.Right)
                return;

            controller.Inventory.SplitStackToFirstEmpty(slotIndex);
        }

        public void OnBeginDrag(PointerEventData eventData)
        {
            controller.BeginDrag(slotIndex, eventData.position);
        }

        public void OnDrag(PointerEventData eventData)
        {
            controller.UpdateDrag(eventData.position);
        }

        public void OnDrop(PointerEventData eventData)
        {
            controller.CompleteDragOn(slotIndex);
        }

        public void OnEndDrag(PointerEventData eventData)
        {
            controller.EndDrag(eventData);
        }
    }
}
