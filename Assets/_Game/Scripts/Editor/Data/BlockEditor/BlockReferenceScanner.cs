using System.Collections.Generic;
using IslandGame.Data.Blocks;
using IslandGame.Data.Items;
using UnityEngine;

namespace IslandGame.EditorTools.Data
{
    /// <summary>
    /// Inbound-reference check run before deleting a BlockDefinition, so the
    /// user is warned about references that would go null.
    ///
    /// Phase 2 scope: the only asset type that can point AT a block today is
    /// ItemDefinition (its PlacedBlock field). Block drop-items point the other
    /// way (block→item), so deleting a block cannot dangle those. Later phases
    /// APPEND further checks here (recipes, terrain palettes, ...) — extend
    /// FindReferences, never replace the pattern.
    /// </summary>
    internal static class BlockReferenceScanner
    {
        /// <summary>One human-readable line per asset referencing the block; empty list = safe to delete.</summary>
        public static List<string> FindReferences(BlockDefinition block)
        {
            var references = new List<string>();
            if (block == null)
                return references;

            var itemDatabase = Resources.Load<ItemDatabase>(ItemDatabase.ResourcesPath);
            if (itemDatabase == null)
            {
                references.Add("(ItemDatabase not found — item Placed Block references could NOT be checked. Run Island Game/Data/Sync Databases.)");
                return references;
            }

            foreach (ItemDefinition item in itemDatabase.All)
            {
                if (item != null && item.PlacedBlock == block)
                    references.Add($"Item '{item.DisplayName}' ({item.Id}) — Placed Block");
            }

            return references;
        }
    }
}
