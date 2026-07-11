using IslandGame.Data.Items;
using IslandGame.Inventory;
using UnityEngine;

namespace IslandGame.Building
{
    /// <summary>
    /// Placed storage: the chest OWNS a real InventorySystem component on its
    /// prefab — the same slot/stack/merge logic the player uses, not a copy
    /// (InventorySystem null-guards its player references, so it runs as a
    /// plain container; the builder sizes it to 12 slots and it holds items
    /// for the session like every placed piece does).
    ///
    /// Interaction (deliberately UI-less until the dedicated container-UI
    /// pass, which will bind the existing inventory grid view to this same
    /// InventorySystem):
    ///   • Interact WITH an item equipped → deposits that whole stack.
    ///   • Interact EMPTY-HANDED → withdraws the last stored stack.
    /// Both directions round-trip through AddItem/ConsumeFromSlot, so partial
    /// fits behave correctly (leftover stays where it was).
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(InventorySystem))]
    public sealed class ChestBehavior : MonoBehaviour, IFunctionalPlaceable
    {
        private BuildingPiece piece;
        private InventorySystem storage;

        /// <summary>The chest's own inventory — the future container UI binds straight to this.</summary>
        public InventorySystem Storage => storage != null ? storage : storage = GetComponent<InventorySystem>();

        public BuildingPiece Piece => piece;

        public string InteractionPrompt => "Store held stack / take items";

        public void Init(BuildingPiece owner)
        {
            piece = owner;
            storage = GetComponent<InventorySystem>();
        }

        public void Interact(GameObject interactor)
        {
            var selector = interactor.GetComponent<HotbarSelector>();
            var playerInventory = interactor.GetComponent<InventorySystem>();
            if (selector == null || playerInventory == null || Storage == null)
                return;

            ItemDefinition equipped = selector.EquippedItem;
            if (equipped != null)
                Deposit(playerInventory, selector, equipped);
            else
                Withdraw(playerInventory);
        }

        private void Deposit(InventorySystem playerInventory, HotbarSelector selector, ItemDefinition item)
        {
            int taken = playerInventory.ConsumeFromSlot(selector.SelectedIndex, item.MaxStackSize);
            if (taken == 0)
                return;

            int stored = Storage.AddItem(item, taken);
            int leftover = taken - stored;
            if (leftover > 0)
                playerInventory.AddItem(item, leftover); // chest full: hand the rest back

            Debug.Log(stored > 0
                ? $"Chest: stored {stored} × {item.DisplayName}."
                : "Chest: full.");
        }

        private void Withdraw(InventorySystem playerInventory)
        {
            // Last non-empty slot: withdrawing unstacks in reverse deposit order.
            for (int i = Storage.SlotCount - 1; i >= 0; i--)
            {
                InventorySlot slot = Storage.GetSlot(i);
                if (slot.IsEmpty)
                    continue;

                ItemDefinition item = slot.Item;
                int moved = playerInventory.AddItem(item, slot.Count);
                if (moved > 0)
                {
                    Storage.ConsumeFromSlot(i, moved);
                    Debug.Log($"Chest: took {moved} × {item.DisplayName}.");
                }
                else
                {
                    Debug.Log("Chest: your inventory is full.");
                }

                return;
            }

            Debug.Log("Chest: empty.");
        }
    }
}
