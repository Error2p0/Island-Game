using IslandGame.Crafting;
using IslandGame.Crafting.UI;
using IslandGame.Data.Items;
using IslandGame.Inventory;
using UnityEngine;

namespace IslandGame.Building
{
    /// <summary>
    /// Functional behavior of the placed workbench. The station GATING itself
    /// is deliberately NOT here: the prefab carries a CraftingStationMarker
    /// (type Workbench), so CraftingSystem's existing overlap check unlocks
    /// Workbench-tier recipes the moment an initialized workbench stands near
    /// the player — no second station system. What this behavior adds is the
    /// convenience on top: interacting with the bench opens the crafting menu
    /// directly (same menu the B key toggles), which is what the reference
    /// games do.
    ///
    /// REPAIR (durability phase): interacting while the EQUIPPED item is a
    /// damaged tool/weapon repairs it instead of opening the menu — the same
    /// equipped-item-dependent interact pattern the campfire's fueling uses.
    /// Costs come from the item's own crafting recipe via ItemRepair (see the
    /// cost-model rationale there); being a physical interaction with the
    /// placed bench, repair inherits the station gating for free.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class WorkbenchBehavior : MonoBehaviour, IFunctionalPlaceable
    {
        [Header("Repair")]
        [Tooltip("Fraction of the item's crafting-recipe ingredients charged for a FULL repair (scaled down further by how little durability is actually missing; minimum 1 of each ingredient).")]
        [Range(0.05f, 1f)]
        [SerializeField] private float repairCostFraction = 0.5f;

        private BuildingPiece piece;
        private CraftingMenuController craftingMenu;

        /// <summary>The owning placed piece (set by Init; null before placement).</summary>
        public BuildingPiece Piece => piece;

        public string InteractionPrompt => "Use workbench (repairs the equipped damaged item)";

        public void Init(BuildingPiece owner)
        {
            piece = owner;
        }

        public void Interact(GameObject interactor)
        {
            // Repair path: equipped item is a damaged durability item.
            var selector = interactor.GetComponent<HotbarSelector>();
            var inventory = interactor.GetComponent<InventorySystem>();
            if (selector != null && inventory != null)
            {
                InventorySlot equippedSlot = inventory.GetSlot(selector.SelectedIndex);
                ItemDefinition equipped = equippedSlot.Item;

                if (equipped != null && equipped.HasDurability && equippedSlot.Durability01 < 1f)
                {
                    // Success or explained failure (missing materials, no
                    // recipe) — either way this interaction was a repair
                    // attempt, so the menu stays closed.
                    ItemRepair.TryRepairSlot(inventory, selector.SelectedIndex, repairCostFraction, out string message);
                    Debug.Log($"Workbench: {message}", this);
                    return;
                }
            }

            // Menu path — resolved lazily: the menu canvas may be (re)built
            // after this workbench was placed, so a cached-at-Init reference
            // could go stale.
            if (craftingMenu == null)
                craftingMenu = FindFirstObjectByType<CraftingMenuController>();

            if (craftingMenu == null)
            {
                Debug.LogWarning("Workbench: no CraftingMenuController in the scene — run Island Game/UI/Build Crafting Menu UI.", this);
                return;
            }

            if (!craftingMenu.IsOpen)
                craftingMenu.Toggle();
        }
    }
}
