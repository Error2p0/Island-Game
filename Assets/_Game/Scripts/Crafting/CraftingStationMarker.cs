using IslandGame.Data.Crafting;
using UnityEngine;

namespace IslandGame.Crafting
{
    /// <summary>
    /// STATION CONVENTION (stub until a dedicated station-object phase): a
    /// crafting station is any GameObject with a collider (trigger or solid)
    /// carrying this component. CraftingSystem overlap-checks a small radius
    /// around the player and matches StationType against the recipe's
    /// requirement. When placed station blocks/furniture arrive later, they
    /// attach this same component — recipes and the crafting system won't
    /// change.
    ///
    /// Manual test setup: empty GameObject + BoxCollider + this component,
    /// type Workbench, dropped near the spawn beach.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CraftingStationMarker : MonoBehaviour
    {
        [SerializeField] private CraftingStationType stationType = CraftingStationType.Workbench;

        public CraftingStationType StationType => stationType;
    }
}
