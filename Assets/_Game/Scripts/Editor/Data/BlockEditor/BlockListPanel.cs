using System;
using System.Collections.Generic;
using IslandGame.Data.Blocks;
using UnityEditor;
using UnityEngine;

namespace IslandGame.EditorTools.Data
{
    /// <summary>
    /// Left panel of the Block Editor: search box, New Block button, and the
    /// scrollable list of every BlockDefinition in the BlockDatabase. Pure
    /// view — selection, creation and deletion are owned by the window and
    /// arrive as callbacks. The selected block shows a ● marker while its edit
    /// session has unsaved changes; right-click offers Ping/Delete.
    /// </summary>
    internal sealed class BlockListPanel
    {
        private static readonly Color SelectionTint = new Color(0.24f, 0.49f, 0.91f, 0.4f);

        private string search = string.Empty;
        private Vector2 scroll;
        private GUIStyle rowStyle;

        public void OnGUI(
            IReadOnlyList<BlockDefinition> blocks,
            BlockDefinition selected,
            bool selectedHasUnsavedChanges,
            Action<BlockDefinition> selectBlock,
            Action createBlock,
            Action<BlockDefinition> deleteBlock)
        {
            EnsureStyles();

            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                search = GUILayout.TextField(search, EditorStyles.toolbarSearchField);
                if (GUILayout.Button("x", EditorStyles.toolbarButton, GUILayout.Width(20f)))
                {
                    search = string.Empty;
                    GUI.FocusControl(null);
                }
            }

            if (GUILayout.Button("New Block", GUILayout.Height(24f)))
                createBlock();

            EditorGUILayout.Space(2f);

            using (var scrollScope = new EditorGUILayout.ScrollViewScope(scroll))
            {
                scroll = scrollScope.scrollPosition;

                int shown = 0;
                for (int i = 0; i < blocks.Count; i++)
                {
                    BlockDefinition block = blocks[i];
                    if (block == null || !MatchesSearch(block))
                        continue;

                    shown++;
                    DrawRow(block, block == selected, selectedHasUnsavedChanges, selectBlock, deleteBlock);
                }

                if (shown == 0)
                {
                    EditorGUILayout.LabelField(
                        blocks.Count == 0 ? "No blocks yet — create one." : "No blocks match the search.",
                        EditorStyles.centeredGreyMiniLabel);
                }
            }
        }

        private void DrawRow(
            BlockDefinition block,
            bool isSelected,
            bool selectedHasUnsavedChanges,
            Action<BlockDefinition> selectBlock,
            Action<BlockDefinition> deleteBlock)
        {
            string displayName = string.IsNullOrEmpty(block.DisplayName) ? block.name : block.DisplayName;
            string label = $"{displayName}  ({block.Id})";
            if (isSelected && selectedHasUnsavedChanges)
                label = "● " + label;

            var content = new GUIContent(label, isSelected && selectedHasUnsavedChanges ? "Unsaved changes" : block.name);
            Rect rowRect = GUILayoutUtility.GetRect(content, rowStyle, GUILayout.ExpandWidth(true));

            Event current = Event.current;
            if (current.type == EventType.ContextClick && rowRect.Contains(current.mousePosition))
            {
                var menu = new GenericMenu();
                menu.AddItem(new GUIContent("Ping Asset"), false, () => EditorGUIUtility.PingObject(block));
                menu.AddItem(new GUIContent("Delete…"), false, () => deleteBlock(block));
                menu.ShowAsContext();
                current.Use();
                return;
            }

            if (isSelected)
                EditorGUI.DrawRect(rowRect, SelectionTint);

            if (GUI.Button(rowRect, content, rowStyle))
                selectBlock(block);
        }

        private bool MatchesSearch(BlockDefinition block)
        {
            if (string.IsNullOrWhiteSpace(search))
                return true;

            return Contains(block.DisplayName) || Contains(block.Id) || Contains(block.name);
        }

        private bool Contains(string text)
        {
            return !string.IsNullOrEmpty(text)
                   && text.IndexOf(search.Trim(), StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private void EnsureStyles()
        {
            if (rowStyle != null)
                return;

            rowStyle = new GUIStyle(EditorStyles.label)
            {
                padding = new RectOffset(8, 4, 3, 3),
                alignment = TextAnchor.MiddleLeft,
            };
        }
    }
}
