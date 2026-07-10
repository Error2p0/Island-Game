using System.Collections.Generic;
using IslandGame.Data.Blocks;
using UnityEditor;
using UnityEngine;

namespace IslandGame.EditorTools.Data
{
    /// <summary>
    /// Multi-select checklist over the BlockDatabase for a tool's Efficient
    /// Blocks list: search/filter plus one toggle per block, editing the
    /// SerializedProperty array in the (unapplied) edit session buffer. Also
    /// surfaces and prunes missing entries left behind by deleted blocks.
    /// </summary>
    internal sealed class EfficientBlocksSelector
    {
        private const float ListHeight = 150f;

        private string search = string.Empty;
        private Vector2 scroll;

        public void OnGUI(SerializedProperty listProperty, IReadOnlyList<BlockDefinition> allBlocks)
        {
            var selected = new HashSet<BlockDefinition>();
            int missingEntries = 0;
            for (int i = 0; i < listProperty.arraySize; i++)
            {
                var entry = listProperty.GetArrayElementAtIndex(i).objectReferenceValue as BlockDefinition;
                if (entry != null)
                    selected.Add(entry);
                else
                    missingEntries++;
            }

            EditorGUILayout.LabelField(
                $"Efficient Blocks ({selected.Count} selected)",
                EditorStyles.miniBoldLabel);

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
                {
                    search = GUILayout.TextField(search, EditorStyles.toolbarSearchField);
                    if (GUILayout.Button("x", EditorStyles.toolbarButton, GUILayout.Width(20f)))
                    {
                        search = string.Empty;
                        GUI.FocusControl(null);
                    }
                }

                using (var scrollScope = new EditorGUILayout.ScrollViewScope(scroll, GUILayout.Height(ListHeight)))
                {
                    scroll = scrollScope.scrollPosition;

                    int shown = 0;
                    for (int i = 0; i < allBlocks.Count; i++)
                    {
                        BlockDefinition block = allBlocks[i];
                        if (block == null || !MatchesSearch(block))
                            continue;

                        shown++;
                        string label = string.IsNullOrEmpty(block.DisplayName) ? block.name : block.DisplayName;
                        bool wasSelected = selected.Contains(block);
                        bool isSelected = EditorGUILayout.ToggleLeft($"{label}  ({block.Id})", wasSelected);

                        if (isSelected && !wasSelected)
                            AddBlock(listProperty, block);
                        else if (!isSelected && wasSelected)
                            RemoveBlock(listProperty, block);
                    }

                    if (shown == 0)
                    {
                        EditorGUILayout.LabelField(
                            allBlocks.Count == 0 ? "No blocks in the BlockDatabase." : "No blocks match the search.",
                            EditorStyles.centeredGreyMiniLabel);
                    }
                }
            }

            if (missingEntries > 0)
            {
                EditorGUILayout.HelpBox(
                    $"{missingEntries} entr{(missingEntries == 1 ? "y" : "ies")} point at deleted blocks.",
                    MessageType.Warning);
                if (GUILayout.Button("Remove Missing Entries"))
                    RemoveMissing(listProperty);
            }
        }

        private static void AddBlock(SerializedProperty listProperty, BlockDefinition block)
        {
            listProperty.arraySize++;
            listProperty.GetArrayElementAtIndex(listProperty.arraySize - 1).objectReferenceValue = block;
        }

        private static void RemoveBlock(SerializedProperty listProperty, BlockDefinition block)
        {
            for (int i = listProperty.arraySize - 1; i >= 0; i--)
            {
                if (listProperty.GetArrayElementAtIndex(i).objectReferenceValue != block)
                    continue;

                // Null first, then delete — removes outright on every Unity
                // version regardless of the old clear-then-delete behavior.
                listProperty.GetArrayElementAtIndex(i).objectReferenceValue = null;
                listProperty.DeleteArrayElementAtIndex(i);
            }
        }

        private static void RemoveMissing(SerializedProperty listProperty)
        {
            for (int i = listProperty.arraySize - 1; i >= 0; i--)
            {
                if (listProperty.GetArrayElementAtIndex(i).objectReferenceValue == null)
                    listProperty.DeleteArrayElementAtIndex(i);
            }
        }

        private bool MatchesSearch(BlockDefinition block)
        {
            if (string.IsNullOrWhiteSpace(search))
                return true;

            string trimmed = search.Trim();
            return Contains(block.DisplayName, trimmed) || Contains(block.Id, trimmed) || Contains(block.name, trimmed);
        }

        private static bool Contains(string text, string needle)
        {
            return !string.IsNullOrEmpty(text)
                   && text.IndexOf(needle, System.StringComparison.OrdinalIgnoreCase) >= 0;
        }
    }
}
