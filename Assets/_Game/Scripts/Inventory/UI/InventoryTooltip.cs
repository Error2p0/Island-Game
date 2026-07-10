using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace IslandGame.Inventory.UI
{
    /// <summary>
    /// Hover tooltip for inventory slots: item name, description, per-unit and
    /// stack weight, and stack fill — all read from the ItemDefinition. Follows
    /// the pointer while visible; hidden during drags and when the panel closes.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class InventoryTooltip : MonoBehaviour
    {
        private static readonly Vector2 PointerOffset = new Vector2(18f, -12f);

        [Header("Wired by the UI builder")]
        [SerializeField] private Text nameText;
        [SerializeField] private Text bodyText;

        private RectTransform rectTransform;

        private void Awake()
        {
            rectTransform = (RectTransform)transform;
            gameObject.SetActive(false);
        }

        public void Show(InventorySlot slot)
        {
            if (slot == null || slot.IsEmpty)
            {
                Hide();
                return;
            }

            var item = slot.Item;
            nameText.text = string.IsNullOrEmpty(item.DisplayName) ? item.name : item.DisplayName;

            string description = string.IsNullOrEmpty(item.Description) ? string.Empty : item.Description + "\n";
            bodyText.text =
                $"{description}" +
                $"Weight: {item.WeightKg:0.##} kg each ({slot.TotalWeightKg:0.##} kg total)\n" +
                $"Stack: {slot.Count}/{item.MaxStackSize}";

            gameObject.SetActive(true);
            FollowPointer();
        }

        public void Hide()
        {
            gameObject.SetActive(false);
        }

        private void Update()
        {
            FollowPointer();
        }

        private void FollowPointer()
        {
            if (Mouse.current == null)
                return;

            rectTransform.position = Mouse.current.position.ReadValue() + PointerOffset;
        }
    }
}
