using IslandGame.Crafting.UI;
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
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class WorkbenchBehavior : MonoBehaviour, IFunctionalPlaceable
    {
        private BuildingPiece piece;
        private CraftingMenuController craftingMenu;

        /// <summary>The owning placed piece (set by Init; null before placement).</summary>
        public BuildingPiece Piece => piece;

        public string InteractionPrompt => "Use workbench";

        public void Init(BuildingPiece owner)
        {
            piece = owner;
        }

        public void Interact(GameObject interactor)
        {
            // Resolved lazily: the menu canvas may be (re)built after this
            // workbench was placed, so a cached-at-Init reference could go stale.
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
