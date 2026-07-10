using System;
using IslandGame.Data.Items;
using IslandGame.Player;
using UnityEngine;

namespace IslandGame.Inventory
{
    /// <summary>
    /// Owns which hotbar slot is "equipped": number keys 1-9 select directly,
    /// the scroll wheel cycles (up = previous, down = next), and G drops one
    /// unit of the equipped item. Pure selection state + events — the visual
    /// holding (socket attachment, upper-body pose) is the held-item phase,
    /// which subscribes to EquippedItemChanged and reads EquippedItem.
    ///
    /// EquippedItemChanged fires when the selection moves AND when the content
    /// of the selected slot changes under it (last of a stack dropped, pickup
    /// into the empty selected slot, drag-and-drop over it, ...).
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(InventorySystem))]
    public sealed class HotbarSelector : MonoBehaviour
    {
        [Tooltip("Minimum scroll input treated as one hotbar step.")]
        [SerializeField] private float scrollThreshold = 0.01f;

        /// <summary>The equipped ItemDefinition changed (null = empty hand). The held-item phase subscribes here.</summary>
        public event Action<ItemDefinition> EquippedItemChanged;

        /// <summary>The selected hotbar INDEX changed (UI highlight). Content changes don't fire this.</summary>
        public event Action<int> SelectedSlotChanged;

        public int SelectedIndex { get; private set; }

        /// <summary>Item in the selected hotbar slot; null for an empty hand.</summary>
        public ItemDefinition EquippedItem =>
            inventory != null ? inventory.GetSlot(SelectedIndex).Item : null;

        private InventorySystem inventory;
        private PlayerReferences references;
        private ItemDefinition lastEquippedItem;

        private void Awake()
        {
            inventory = GetComponent<InventorySystem>();
            references = GetComponent<PlayerReferences>();
        }

        private void OnEnable()
        {
            references.InputHandler.HotbarSlotPressed += SelectSlot;
            references.InputHandler.DropPressed += OnDropPressed;
            inventory.InventoryChanged += OnInventoryChanged;
        }

        private void OnDisable()
        {
            references.InputHandler.HotbarSlotPressed -= SelectSlot;
            references.InputHandler.DropPressed -= OnDropPressed;
            inventory.InventoryChanged -= OnInventoryChanged;
        }

        private void Update()
        {
            float scroll = references.InputHandler.HotbarScrollDelta;
            if (scroll > scrollThreshold)
                SelectSlot((SelectedIndex - 1 + inventory.HotbarSize) % inventory.HotbarSize);
            else if (scroll < -scrollThreshold)
                SelectSlot((SelectedIndex + 1) % inventory.HotbarSize);
        }

        /// <summary>Selects a hotbar slot by 0-based index (out-of-range presses on a smaller hotbar are ignored).</summary>
        public void SelectSlot(int index)
        {
            if (index < 0 || index >= inventory.HotbarSize || index == SelectedIndex)
                return;

            SelectedIndex = index;
            SelectedSlotChanged?.Invoke(SelectedIndex);
            RaiseEquippedIfChanged();
        }

        private void OnInventoryChanged()
        {
            RaiseEquippedIfChanged();
        }

        private void OnDropPressed()
        {
            inventory.DropFromSlot(SelectedIndex, 1);
        }

        private void RaiseEquippedIfChanged()
        {
            ItemDefinition current = EquippedItem;
            if (ReferenceEquals(current, lastEquippedItem))
                return;

            lastEquippedItem = current;
            EquippedItemChanged?.Invoke(current);
        }
    }
}
