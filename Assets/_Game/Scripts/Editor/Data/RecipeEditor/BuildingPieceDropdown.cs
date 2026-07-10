using System;
using System.Collections.Generic;
using System.Linq;
using IslandGame.Data.Building;
using UnityEditor.IMGUI.Controls;
using UnityEngine;

namespace IslandGame.EditorTools.Data
{
    /// <summary>
    /// Searchable picker over the BuildingPieceDatabase for piece-reference
    /// fields (the Recipe Editor's building output). Same AdvancedDropdown
    /// pattern as ItemDefinitionDropdown: entries show "Display Name (id)"
    /// plus the piece icon; selecting "(None)" clears the reference.
    /// </summary>
    internal sealed class BuildingPieceDropdown : AdvancedDropdown
    {
        private const int NoneId = 0;

        private readonly IReadOnlyList<BuildingPieceDefinition> pieces;
        private readonly Action<BuildingPieceDefinition> onSelected;
        private readonly Dictionary<int, BuildingPieceDefinition> byEntryId = new Dictionary<int, BuildingPieceDefinition>();

        public BuildingPieceDropdown(
            AdvancedDropdownState state, IReadOnlyList<BuildingPieceDefinition> pieces,
            Action<BuildingPieceDefinition> onSelected)
            : base(state)
        {
            this.pieces = pieces;
            this.onSelected = onSelected;
            minimumSize = new Vector2(280f, 320f);
        }

        protected override AdvancedDropdownItem BuildRoot()
        {
            byEntryId.Clear();

            var root = new AdvancedDropdownItem("Building Pieces");
            root.AddChild(new AdvancedDropdownItem("(None)") { id = NoneId });

            int nextEntryId = NoneId + 1;
            IOrderedEnumerable<BuildingPieceDefinition> sorted = pieces
                .Where(piece => piece != null)
                .OrderBy(piece => string.IsNullOrEmpty(piece.DisplayName) ? piece.name : piece.DisplayName,
                    StringComparer.OrdinalIgnoreCase);

            foreach (BuildingPieceDefinition piece in sorted)
            {
                string label = string.IsNullOrEmpty(piece.DisplayName) ? piece.name : piece.DisplayName;
                var entry = new AdvancedDropdownItem($"{label} ({piece.Id})")
                {
                    id = nextEntryId,
                    icon = piece.Icon != null ? piece.Icon.texture : null,
                };
                byEntryId.Add(nextEntryId, piece);
                nextEntryId++;
                root.AddChild(entry);
            }

            return root;
        }

        protected override void ItemSelected(AdvancedDropdownItem item)
        {
            byEntryId.TryGetValue(item.id, out BuildingPieceDefinition definition); // NoneId → null clears
            onSelected(definition);
        }
    }
}
