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
            string durability = item.HasDurability
                ? $"\nDurability: {Mathf.CeilToInt(slot.Durability01 * item.MaxDurability)}/{item.MaxDurability:0}"
                : string.Empty;
            bodyText.text =
                $"{description}" +
                $"Weight: {item.WeightKg:0.##} kg each ({slot.TotalWeightKg:0.##} kg total)\n" +
                $"Stack: {slot.Count}/{item.MaxStackSize}" +
                durability;

            gameObject.SetActive(true);
            // The height comes from the layout group + fitter; rebuild now so
            // the first FollowPointer clamps against the real size instead of
            // last item's.
            LayoutRebuilder.ForceRebuildLayoutImmediate(rectTransform);
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

            Vector2 position = Mouse.current.position.ReadValue() + PointerOffset;

            // Keep the whole tooltip on screen (pivot is top-left, so the rect
            // extends right and down). rect size is in canvas units — scale to
            // screen pixels before clamping.
            float width = rectTransform.rect.width * rectTransform.lossyScale.x;
            float height = rectTransform.rect.height * rectTransform.lossyScale.y;
            position.x = Mathf.Clamp(position.x, 0f, Mathf.Max(0f, Screen.width - width));
            position.y = Mathf.Clamp(position.y, Mathf.Min(height, Screen.height), (float)Screen.height);

            rectTransform.position = position;
        }
    }
}
