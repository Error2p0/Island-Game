using System;
using System.Collections.Generic;
using IslandGame.Data.Blocks;
using IslandGame.Data.Items;
using UnityEngine;

namespace IslandGame.Creative.UI
{
    /// <summary>One row of the creative menu: an item, or a block presented through its item form.</summary>
    public sealed class CreativeEntry
    {
        public string DisplayName;
        public string Id;
        public Sprite Icon;

        /// <summary>What clicking gives, via the normal inventory AddItem path. Null = not giveable (block with no item form yet).</summary>
        public ItemDefinition GiveItem;

        public bool IsBlock;
        public ItemCategory Category;
    }

    /// <summary>
    /// Builds the creative menu's entry list from the databases: every
    /// ItemDefinition under its category, and every BlockDefinition on the
    /// Blocks tab through its item form — Drop Item first, else any item whose
    /// PlacedBlock points back at it, else a synthetic non-giveable entry so
    /// the block is still visible instead of silently missing (or crashing).
    /// </summary>
    public static class CreativeCatalog
    {
        /// <summary>Sorted entry list. Empty (with a console warning) when the databases are missing.</summary>
        public static List<CreativeEntry> Build()
        {
            var entries = new List<CreativeEntry>();

            var itemDatabase = Resources.Load<ItemDatabase>(ItemDatabase.ResourcesPath);
            var blockDatabase = Resources.Load<BlockDatabase>(BlockDatabase.ResourcesPath);

            if (itemDatabase == null || blockDatabase == null)
            {
                Debug.LogWarning(
                    "CreativeCatalog: databases missing — run Island Game/Data/Sync Databases. The creative menu will be empty.");
                return entries;
            }

            foreach (ItemDefinition item in itemDatabase.All)
            {
                if (item == null)
                    continue;

                entries.Add(new CreativeEntry
                {
                    DisplayName = string.IsNullOrEmpty(item.DisplayName) ? item.name : item.DisplayName,
                    Id = item.Id,
                    Icon = item.Icon,
                    GiveItem = item,
                    IsBlock = false,
                    Category = item.Category,
                });
            }

            foreach (BlockDefinition block in blockDatabase.All)
            {
                if (block == null)
                    continue;

                ItemDefinition itemForm = ResolveItemForm(block, itemDatabase);
                entries.Add(new CreativeEntry
                {
                    DisplayName = string.IsNullOrEmpty(block.DisplayName) ? block.name : block.DisplayName,
                    Id = block.Id,
                    Icon = itemForm != null ? itemForm.Icon : null,
                    GiveItem = itemForm,
                    IsBlock = true,
                    Category = ItemCategory.Block,
                });
            }

            entries.Sort((a, b) => string.Compare(a.DisplayName, b.DisplayName, StringComparison.OrdinalIgnoreCase));
            return entries;
        }

        private static ItemDefinition ResolveItemForm(BlockDefinition block, ItemDatabase itemDatabase)
        {
            if (block.DropItem != null)
                return block.DropItem;

            // No drop authored yet — an item that places this block is just as valid a hand-out.
            foreach (ItemDefinition item in itemDatabase.All)
            {
                if (item != null && item.PlacedBlock == block)
                    return item;
            }

            return null;
        }
    }
}
