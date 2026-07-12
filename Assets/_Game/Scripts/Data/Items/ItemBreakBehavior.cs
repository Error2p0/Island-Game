namespace IslandGame.Data.Items
{
    /// <summary>
    /// What happens to a tool/weapon when its durability reaches zero. Both
    /// options are fully implemented by InventorySystem's break handling:
    /// Destroy removes the unit; DowngradeToBrokenVariant swaps it for the
    /// authored Broken Variant item — a normal, weaker ItemDefinition (lower
    /// damage/mining stats, no equip stat modifiers, typically excluded from
    /// recipes), so "still usable but worse" needs no special-case code
    /// anywhere: the variant is just another item.
    /// </summary>
    public enum ItemBreakBehavior
    {
        /// <summary>The broken unit is removed from the inventory entirely.</summary>
        Destroy = 0,

        /// <summary>The broken unit is replaced by the item's Broken Variant reference.</summary>
        DowngradeToBrokenVariant = 1,
    }
}
