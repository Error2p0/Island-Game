using System;
using System.Collections.Generic;
using IslandGame.Data;
using UnityEditor;
using UnityEngine;

namespace IslandGame.EditorTools.Data
{
    /// <summary>
    /// Generic left-hand list panel shared by definition editor windows — the
    /// same UX the Block Editor established: search box (matches display name,
    /// ID or asset name), a New button, scrollable rows with a ● unsaved marker
    /// on the selected row, and right-click Ping/Delete. Pure view; selection,
    /// creation and deletion are owned by the window and arrive as callbacks.
    ///
    /// (The Block Editor's BlockListPanel predates this class; new editor
    /// windows use this generic version instead of copying it again.)
    /// </summary>
    internal sealed class DefinitionListPanel<TDefinition>
        where TDefinition : ScriptableObject, IDefinition
    {
        private static readonly Color SelectionTint = new Color(0.24f, 0.49f, 0.91f, 0.4f);

        private readonly string createButtonLabel;

        private string search = string.Empty;
        private Vector2 scroll;
        private GUIStyle rowStyle;

        public DefinitionListPanel(string createButtonLabel)
        {
            this.createButtonLabel = createButtonLabel;
        }

        public void OnGUI(
            IReadOnlyList<TDefinition> definitions,
            TDefinition selected,
            bool selectedHasUnsavedChanges,
            Action<TDefinition> select,
            Action create,
            Action<TDefinition> delete)
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

            if (GUILayout.Button(createButtonLabel, GUILayout.Height(24f)))
                create();

            EditorGUILayout.Space(2f);

            using (var scrollScope = new EditorGUILayout.ScrollViewScope(scroll))
            {
                scroll = scrollScope.scrollPosition;

                int shown = 0;
                for (int i = 0; i < definitions.Count; i++)
                {
                    TDefinition definition = definitions[i];
                    if (definition == null || !MatchesSearch(definition))
                        continue;

                    shown++;
                    DrawRow(definition, ReferenceEquals(definition, selected), selectedHasUnsavedChanges, select, delete);
                }

                if (shown == 0)
                {
                    EditorGUILayout.LabelField(
                        definitions.Count == 0 ? "Nothing here yet — create one." : "No entries match the search.",
                        EditorStyles.centeredGreyMiniLabel);
                }
            }
        }

        private void DrawRow(
            TDefinition definition,
            bool isSelected,
            bool selectedHasUnsavedChanges,
            Action<TDefinition> select,
            Action<TDefinition> delete)
        {
            string displayName = string.IsNullOrEmpty(definition.DisplayName) ? definition.name : definition.DisplayName;
            string label = $"{displayName}  ({definition.Id})";
            if (isSelected && selectedHasUnsavedChanges)
                label = "● " + label;

            var content = new GUIContent(label, isSelected && selectedHasUnsavedChanges ? "Unsaved changes" : definition.name);
            Rect rowRect = GUILayoutUtility.GetRect(content, rowStyle, GUILayout.ExpandWidth(true));

            Event current = Event.current;
            if (current.type == EventType.ContextClick && rowRect.Contains(current.mousePosition))
            {
                var menu = new GenericMenu();
                menu.AddItem(new GUIContent("Ping Asset"), false, () => EditorGUIUtility.PingObject(definition));
                menu.AddItem(new GUIContent("Delete…"), false, () => delete(definition));
                menu.ShowAsContext();
                current.Use();
                return;
            }

            if (isSelected)
                EditorGUI.DrawRect(rowRect, SelectionTint);

            if (GUI.Button(rowRect, content, rowStyle))
                select(definition);
        }

        private bool MatchesSearch(TDefinition definition)
        {
            if (string.IsNullOrWhiteSpace(search))
                return true;

            return Contains(definition.DisplayName) || Contains(definition.Id) || Contains(definition.name);
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
