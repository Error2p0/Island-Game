using System;
using System.Collections.Generic;
using System.Linq;
using IslandGame.Data.Items;
using UnityEditor.IMGUI.Controls;
using UnityEngine;

namespace IslandGame.EditorTools.Data
{
    /// <summary>
    /// Searchable picker over the ItemDatabase for item-reference fields (the
    /// block editor's Drop Item now, recipe ingredients later). AdvancedDropdown
    /// provides the built-in search box; entries show "Display Name (id)" plus
    /// the item icon. Selecting "(None)" clears the reference.
    /// </summary>
    internal sealed class ItemDefinitionDropdown : AdvancedDropdown
    {
        private const int NoneId = 0;

        private readonly IReadOnlyList<ItemDefinition> items;
        private readonly Action<ItemDefinition> onSelected;
        private readonly Dictionary<int, ItemDefinition> byEntryId = new Dictionary<int, ItemDefinition>();

        public ItemDefinitionDropdown(
            AdvancedDropdownState state, IReadOnlyList<ItemDefinition> items, Action<ItemDefinition> onSelected)
            : base(state)
        {
            this.items = items;
            this.onSelected = onSelected;
            minimumSize = new Vector2(280f, 320f);
        }

        protected override AdvancedDropdownItem BuildRoot()
        {
            byEntryId.Clear();

            var root = new AdvancedDropdownItem("Items");
            root.AddChild(new AdvancedDropdownItem("(None)") { id = NoneId });

            int nextEntryId = NoneId + 1;
            IOrderedEnumerable<ItemDefinition> sorted = items
                .Where(item => item != null)
                .OrderBy(item => string.IsNullOrEmpty(item.DisplayName) ? item.name : item.DisplayName,
                    StringComparer.OrdinalIgnoreCase);

            foreach (ItemDefinition item in sorted)
            {
                string label = string.IsNullOrEmpty(item.DisplayName) ? item.name : item.DisplayName;
                var entry = new AdvancedDropdownItem($"{label} ({item.Id})")
                {
                    id = nextEntryId,
                    icon = item.Icon != null ? item.Icon.texture : null,
                };
                byEntryId.Add(nextEntryId, item);
                nextEntryId++;
                root.AddChild(entry);
            }

            return root;
        }

        protected override void ItemSelected(AdvancedDropdownItem item)
        {
            byEntryId.TryGetValue(item.id, out ItemDefinition definition); // NoneId → null clears
            onSelected(definition);
        }
    }
}
