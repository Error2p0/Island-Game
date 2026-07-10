using System;
using System.Collections.Generic;
using System.Linq;
using IslandGame.Data.Blocks;
using UnityEditor.IMGUI.Controls;
using UnityEngine;

namespace IslandGame.EditorTools.Data
{
    /// <summary>
    /// Searchable picker over the BlockDatabase for block-reference fields
    /// (the Item Editor's Placed Block) — the block-side counterpart of
    /// ItemDefinitionDropdown, with the same UX. Selecting "(None)" clears
    /// the reference.
    /// </summary>
    internal sealed class BlockDefinitionDropdown : AdvancedDropdown
    {
        private const int NoneId = 0;

        private readonly IReadOnlyList<BlockDefinition> blocks;
        private readonly Action<BlockDefinition> onSelected;
        private readonly Dictionary<int, BlockDefinition> byEntryId = new Dictionary<int, BlockDefinition>();

        public BlockDefinitionDropdown(
            AdvancedDropdownState state, IReadOnlyList<BlockDefinition> blocks, Action<BlockDefinition> onSelected)
            : base(state)
        {
            this.blocks = blocks;
            this.onSelected = onSelected;
            minimumSize = new Vector2(280f, 320f);
        }

        protected override AdvancedDropdownItem BuildRoot()
        {
            byEntryId.Clear();

            var root = new AdvancedDropdownItem("Blocks");
            root.AddChild(new AdvancedDropdownItem("(None)") { id = NoneId });

            int nextEntryId = NoneId + 1;
            IOrderedEnumerable<BlockDefinition> sorted = blocks
                .Where(block => block != null)
                .OrderBy(block => string.IsNullOrEmpty(block.DisplayName) ? block.name : block.DisplayName,
                    StringComparer.OrdinalIgnoreCase);

            foreach (BlockDefinition block in sorted)
            {
                string label = string.IsNullOrEmpty(block.DisplayName) ? block.name : block.DisplayName;
                var entry = new AdvancedDropdownItem($"{label} ({block.Id})") { id = nextEntryId };
                byEntryId.Add(nextEntryId, block);
                nextEntryId++;
                root.AddChild(entry);
            }

            return root;
        }

        protected override void ItemSelected(AdvancedDropdownItem item)
        {
            byEntryId.TryGetValue(item.id, out BlockDefinition definition); // NoneId → null clears
            onSelected(definition);
        }
    }
}
