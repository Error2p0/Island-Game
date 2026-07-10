using System.Collections.Generic;
using IslandGame.Data.Building;
using IslandGame.Data.Crafting;
using IslandGame.Data.Items;
using UnityEditor;
using UnityEditor.IMGUI.Controls;
using UnityEngine;

namespace IslandGame.EditorTools.Data
{
    /// <summary>
    /// Right panel of the Recipe Editor: output-type selector (Item recipes
    /// land in the inventory, Building recipes arm the placement ghost) with
    /// the matching searchable picker (ItemDatabase / BuildingPieceDatabase),
    /// add/remove ingredient rows (each with its own searchable picker +
    /// quantity), station dropdown, craft time, and a live validation section
    /// — dangling references from deleted assets are called out per row, plus
    /// duplicate/self-referencing ingredients, exactly-one-output problems,
    /// duplicate building recipes for the same piece, and ID problems. Pure
    /// view over the session's unapplied SerializedObject.
    /// </summary>
    internal sealed class RecipeInspectorPanel
    {
        private static readonly GUIContent[] OutputTypeLabels =
        {
            new GUIContent("Item", "Output lands in the inventory when crafted."),
            new GUIContent("Building Piece", "The Build button arms the placement ghost; ingredients are consumed on placement."),
        };

        private readonly EditorWindow owner;
        private readonly AdvancedDropdownState outputDropdownState = new AdvancedDropdownState();
        private readonly AdvancedDropdownState outputPieceDropdownState = new AdvancedDropdownState();
        private readonly AdvancedDropdownState ingredientDropdownState = new AdvancedDropdownState();

        private Vector2 scroll;
        private int outputTypeChoice = -1; // remembers the toolbar while both output fields are empty

        public RecipeInspectorPanel(EditorWindow owner)
        {
            this.owner = owner;
        }

        public void OnGUI(
            RecipeEditSession session,
            IReadOnlyList<RecipeDefinition> allRecipes,
            IReadOnlyList<ItemDefinition> allItems,
            bool itemDatabaseAvailable,
            IReadOnlyList<BuildingPieceDefinition> allPieces,
            bool pieceDatabaseAvailable)
        {
            SerializedObject serialized = session.Serialized;

            using (var scrollScope = new EditorGUILayout.ScrollViewScope(scroll))
            {
                scroll = scrollScope.scrollPosition;

                DrawIdentitySection(session, serialized, allRecipes);
                EditorGUILayout.Space(6f);
                DrawOutputSection(session, serialized, allItems, itemDatabaseAvailable, allPieces, pieceDatabaseAvailable);
                EditorGUILayout.Space(6f);
                DrawIngredientsSection(session, serialized, allItems, itemDatabaseAvailable);
                EditorGUILayout.Space(6f);
                DrawRequirementsSection(serialized);
                EditorGUILayout.Space(6f);
                DrawValidationSection(session, serialized, allRecipes);
                EditorGUILayout.Space(8f);
            }
        }

        // ------------------------------------------------------------------
        // Sections
        // ------------------------------------------------------------------

        private static void DrawIdentitySection(
            RecipeEditSession session, SerializedObject serialized, IReadOnlyList<RecipeDefinition> allRecipes)
        {
            EditorGUILayout.LabelField("Identity", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(serialized.FindProperty("displayName"));
            EditorGUILayout.PropertyField(serialized.FindProperty("id"));

            string currentId = session.CurrentId;
            if (string.IsNullOrWhiteSpace(currentId))
            {
                EditorGUILayout.HelpBox("Empty ID — future save data (recipe unlocks) needs one.", MessageType.Warning);
                return;
            }

            for (int i = 0; i < allRecipes.Count; i++)
            {
                RecipeDefinition other = allRecipes[i];
                if (other != null && other != session.Target && other.Id == currentId.Trim())
                {
                    EditorGUILayout.HelpBox(
                        $"ID '{currentId.Trim()}' is already used by '{other.name}' — change one before saving.",
                        MessageType.Error);
                    return;
                }
            }
        }

        private void DrawOutputSection(
            RecipeEditSession session, SerializedObject serialized,
            IReadOnlyList<ItemDefinition> allItems, bool itemDatabaseAvailable,
            IReadOnlyList<BuildingPieceDefinition> allPieces, bool pieceDatabaseAvailable)
        {
            EditorGUILayout.LabelField("Output", EditorStyles.boldLabel);

            SerializedProperty output = serialized.FindProperty("output");
            SerializedProperty outputPiece = serialized.FindProperty("outputPiece");

            int currentType = outputPiece.objectReferenceValue != null ? 1
                : output.objectReferenceValue != null ? 0
                : Mathf.Max(0, outputTypeChoice);

            int newType = GUILayout.Toolbar(currentType, OutputTypeLabels, GUILayout.Height(22f));
            if (newType != currentType)
            {
                // Switching type clears the other output so the exactly-one
                // rule holds without hidden leftover references.
                outputTypeChoice = newType;
                if (newType == 0)
                    outputPiece.objectReferenceValue = null;
                else
                    output.objectReferenceValue = null;
            }

            if (newType == 0)
            {
                DrawItemPickerRow(session, "Output Item", output, allItems, itemDatabaseAvailable, outputDropdownState);
                EditorGUILayout.PropertyField(serialized.FindProperty("outputCount"));
            }
            else
            {
                DrawPiecePickerRow(session, "Output Piece", outputPiece, allPieces, pieceDatabaseAvailable);
                EditorGUILayout.HelpBox(
                    "Building recipe: the crafting menu's Build button arms the placement ghost with this piece; " +
                    "ingredients are consumed on successful placement. Output Count is ignored.",
                    MessageType.None);

                if (!pieceDatabaseAvailable)
                {
                    EditorGUILayout.HelpBox(
                        "BuildingPieceDatabase not found — the search dropdown is disabled. Run Island Game/Data/Sync Databases.",
                        MessageType.Warning);
                }
            }
        }

        private void DrawPiecePickerRow(
            RecipeEditSession session, string label, SerializedProperty property,
            IReadOnlyList<BuildingPieceDefinition> allPieces, bool pieceDatabaseAvailable)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.PrefixLabel(label);

                var current = property.objectReferenceValue as BuildingPieceDefinition;
                string buttonLabel = current != null ? $"{current.DisplayName} ({current.Id})" : "(None)";
                string propertyPath = property.propertyPath;

                Rect buttonRect = GUILayoutUtility.GetRect(
                    new GUIContent(buttonLabel), EditorStyles.popup, GUILayout.MinWidth(110f));

                using (new EditorGUI.DisabledScope(!pieceDatabaseAvailable))
                {
                    if (GUI.Button(buttonRect, buttonLabel, EditorStyles.popup))
                    {
                        var dropdown = new BuildingPieceDropdown(outputPieceDropdownState, allPieces, picked =>
                        {
                            SerializedProperty target = session.Serialized.FindProperty(propertyPath);
                            if (target != null)
                                target.objectReferenceValue = picked;
                            owner.Repaint();
                        });
                        dropdown.Show(buttonRect);
                    }
                }

                EditorGUILayout.PropertyField(property, GUIContent.none, GUILayout.Width(100f));
            }
        }

        private void DrawIngredientsSection(
            RecipeEditSession session, SerializedObject serialized,
            IReadOnlyList<ItemDefinition> allItems, bool itemDatabaseAvailable)
        {
            SerializedProperty list = serialized.FindProperty("ingredients");
            EditorGUILayout.LabelField($"Ingredients ({list.arraySize})", EditorStyles.boldLabel);

            int removeIndex = -1;
            for (int i = 0; i < list.arraySize; i++)
            {
                SerializedProperty element = list.GetArrayElementAtIndex(i);
                SerializedProperty item = element.FindPropertyRelative("item");
                SerializedProperty count = element.FindPropertyRelative("count");

                using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                {
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        DrawItemPickerRow(session, $"Ingredient {i + 1}", item, allItems, itemDatabaseAvailable, ingredientDropdownState);
                        if (GUILayout.Button("✕", GUILayout.Width(22f)))
                            removeIndex = i;
                    }

                    EditorGUILayout.PropertyField(count, new GUIContent("Count"));

                    if (item.objectReferenceValue == null)
                        EditorGUILayout.HelpBox("Missing item (deleted asset?) — dangling reference.", MessageType.Error);
                }
            }

            if (removeIndex >= 0)
                list.DeleteArrayElementAtIndex(removeIndex);

            if (GUILayout.Button("Add Ingredient", GUILayout.Height(22f)))
            {
                list.arraySize++;
                SerializedProperty added = list.GetArrayElementAtIndex(list.arraySize - 1);
                added.FindPropertyRelative("item").objectReferenceValue = null;
                added.FindPropertyRelative("count").intValue = 1;
            }
        }

        private static void DrawRequirementsSection(SerializedObject serialized)
        {
            EditorGUILayout.LabelField("Requirements", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(serialized.FindProperty("station"));
            EditorGUILayout.PropertyField(serialized.FindProperty("craftSeconds"));
        }

        private static void DrawValidationSection(
            RecipeEditSession session, SerializedObject serialized, IReadOnlyList<RecipeDefinition> allRecipes)
        {
            EditorGUILayout.LabelField("Validation", EditorStyles.boldLabel);
            bool anyIssue = false;

            var output = serialized.FindProperty("output").objectReferenceValue as ItemDefinition;
            var outputPiece = serialized.FindProperty("outputPiece").objectReferenceValue as BuildingPieceDefinition;

            if (output == null && outputPiece == null)
            {
                anyIssue = true;
                EditorGUILayout.HelpBox(
                    "No output — set an Output Item or an Output Piece (dangling or unset reference).", MessageType.Error);
            }

            if (output != null && outputPiece != null)
            {
                anyIssue = true;
                EditorGUILayout.HelpBox(
                    "Both Output Item AND Output Piece are set — a recipe is one or the other. Crafting and placement both refuse it; clear one.",
                    MessageType.Error);
            }

            if (outputPiece != null)
            {
                if (outputPiece.Prefab == null)
                {
                    anyIssue = true;
                    EditorGUILayout.HelpBox(
                        $"Output piece '{outputPiece.DisplayName}' has no prefab — it cannot be placed. Fix it in the Building Piece Editor.",
                        MessageType.Error);
                }

                for (int i = 0; i < allRecipes.Count; i++)
                {
                    RecipeDefinition other = allRecipes[i];
                    if (other != null && other != session.Target && other.OutputPiece == outputPiece)
                    {
                        anyIssue = true;
                        EditorGUILayout.HelpBox(
                            $"'{other.name}' also outputs this piece — placement charges the FIRST recipe found. Keep one recipe per piece.",
                            MessageType.Warning);
                        break;
                    }
                }
            }

            SerializedProperty list = serialized.FindProperty("ingredients");
            if (list.arraySize == 0)
            {
                anyIssue = true;
                EditorGUILayout.HelpBox("No ingredients — this crafts something from nothing.", MessageType.Warning);
            }

            var seen = new HashSet<ItemDefinition>();
            for (int i = 0; i < list.arraySize; i++)
            {
                var item = list.GetArrayElementAtIndex(i).FindPropertyRelative("item").objectReferenceValue as ItemDefinition;
                if (item == null)
                    continue; // per-row error already shown above

                if (!seen.Add(item))
                {
                    anyIssue = true;
                    EditorGUILayout.HelpBox($"'{item.DisplayName}' appears in multiple ingredient rows — merge them into one row's count.", MessageType.Warning);
                }

                if (output != null && item == output)
                {
                    anyIssue = true;
                    EditorGUILayout.HelpBox("The output is also an ingredient — legal (e.g. refining), just confirming it's intentional.", MessageType.Info);
                }
            }

            if (output != null && output.MaxStackSize < serialized.FindProperty("outputCount").intValue)
            {
                anyIssue = true;
                EditorGUILayout.HelpBox(
                    "Output Count exceeds the item's Max Stack Size — it will fill multiple slots per craft.",
                    MessageType.Info);
            }

            if (!anyIssue)
                EditorGUILayout.LabelField("No issues found.", EditorStyles.centeredGreyMiniLabel);
        }

        // ------------------------------------------------------------------
        // Shared item picker row (searchable dropdown + drag-drop field)
        // ------------------------------------------------------------------

        private void DrawItemPickerRow(
            RecipeEditSession session, string label, SerializedProperty property,
            IReadOnlyList<ItemDefinition> allItems, bool itemDatabaseAvailable, AdvancedDropdownState state)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.PrefixLabel(label);

                var current = property.objectReferenceValue as ItemDefinition;
                string buttonLabel = current != null ? $"{current.DisplayName} ({current.Id})" : "(None)";
                string propertyPath = property.propertyPath;

                Rect buttonRect = GUILayoutUtility.GetRect(
                    new GUIContent(buttonLabel), EditorStyles.popup, GUILayout.MinWidth(110f));

                using (new EditorGUI.DisabledScope(!itemDatabaseAvailable))
                {
                    if (GUI.Button(buttonRect, buttonLabel, EditorStyles.popup))
                    {
                        // Resolve by path inside the callback: the row's
                        // SerializedProperty may have been iterated past (or the
                        // array resized) by the time the dropdown returns.
                        var dropdown = new ItemDefinitionDropdown(state, allItems, picked =>
                        {
                            SerializedProperty target = session.Serialized.FindProperty(propertyPath);
                            if (target != null)
                                target.objectReferenceValue = picked;
                            owner.Repaint();
                        });
                        dropdown.Show(buttonRect);
                    }
                }

                EditorGUILayout.PropertyField(property, GUIContent.none, GUILayout.Width(100f));
            }
        }
    }
}
