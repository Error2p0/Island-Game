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

        [Tooltip("Durability strip root (background). Null-safe: canvases built before the durability phase simply show no bar until rebuilt.")]
        [SerializeField] private GameObject durabilityRoot;

        [SerializeField] private Image durabilityFill;

        private static readonly Color DurabilityFull = new Color(0.35f, 0.78f, 0.25f);
        private static readonly Color DurabilityMid = new Color(0.92f, 0.80f, 0.20f);
        private static readonly Color DurabilityLow = new Color(0.90f, 0.18f, 0.12f);

        /// <summary>Below this condition the bar renders in the warning color.</summary>
        private const float LowDurabilityThreshold = 0.25f;

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
                SetDurabilityBar(false, 0f);
                return;
            }

            icon.enabled = true;
            icon.sprite = slot.Item.Icon;
            // Icon-less items draw the raw white square dimmed — visible and
            // draggable rather than invisible; the tooltip carries the name.
            icon.color = slot.Item.Icon != null ? Color.white : new Color(1f, 1f, 1f, 0.35f);
            countText.text = slot.Count > 1 ? slot.Count.ToString() : string.Empty;

            // Minecraft-style: the strip appears only once the item has taken
            // wear — pristine tools keep the slot clean.
            SetDurabilityBar(slot.Item.HasDurability && slot.Durability01 < 1f, slot.Durability01);
        }

        private void SetDurabilityBar(bool visible, float durability01)
        {
            if (durabilityRoot == null)
                return; // pre-durability canvas — rebuild the inventory UI to get the bar

            durabilityRoot.SetActive(visible);
            if (!visible || durabilityFill == null)
                return;

            durabilityFill.fillAmount = durability01;
            // Green → yellow across the healthy range, hard warning red below
            // the threshold (a color STATE, not just a darker green).
            durabilityFill.color = durability01 < LowDurabilityThreshold
                ? DurabilityLow
                : Color.Lerp(DurabilityMid, DurabilityFull,
                    Mathf.InverseLerp(LowDurabilityThreshold, 1f, durability01));
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
