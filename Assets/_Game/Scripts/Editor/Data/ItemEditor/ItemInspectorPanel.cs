using System.Collections.Generic;
using IslandGame.Data.Blocks;
using IslandGame.Data.Building;
using IslandGame.Data.Items;
using UnityEditor;
using UnityEditor.IMGUI.Controls;
using UnityEngine;

namespace IslandGame.EditorTools.Data
{
    /// <summary>
    /// Right panel of the Item Editor: inspector-style sections for the
    /// selected item's edit session — General, Weapon (when Is Weapon),
    /// Tool (when Is Tool; both can be active at once, e.g. an axe),
    /// Holding, and a Validation readout. Pure view over the session's
    /// SerializedObject; nothing hits disk until the window's Save applies it.
    /// </summary>
    internal sealed class ItemInspectorPanel
    {
        private readonly EditorWindow owner;
        private readonly EfficientBlocksSelector efficientBlocks = new EfficientBlocksSelector();
        private readonly AdvancedDropdownState placedBlockDropdownState = new AdvancedDropdownState();

        private Vector2 scroll;

        public ItemInspectorPanel(EditorWindow owner)
        {
            this.owner = owner;
        }

        public void OnGUI(
            ItemEditSession session,
            IReadOnlyList<ItemDefinition> allItems,
            IReadOnlyList<BlockDefinition> allBlocks,
            bool blockDatabaseAvailable)
        {
            SerializedObject serialized = session.Serialized;

            using (var scrollScope = new EditorGUILayout.ScrollViewScope(scroll))
            {
                scroll = scrollScope.scrollPosition;

                DrawGeneralSection(session, serialized, allItems, allBlocks, blockDatabaseAvailable);
                EditorGUILayout.Space(6f);
                DrawWeaponSection(serialized);
                EditorGUILayout.Space(6f);
                DrawToolSection(serialized, allBlocks, blockDatabaseAvailable);
                EditorGUILayout.Space(6f);
                DrawHoldSection(session, serialized);
                EditorGUILayout.Space(6f);
                DrawValidationSection(session, serialized);
                EditorGUILayout.Space(8f);
            }
        }

        // ------------------------------------------------------------------
        // General
        // ------------------------------------------------------------------

        private void DrawGeneralSection(
            ItemEditSession session,
            SerializedObject serialized,
            IReadOnlyList<ItemDefinition> allItems,
            IReadOnlyList<BlockDefinition> allBlocks,
            bool blockDatabaseAvailable)
        {
            EditorGUILayout.LabelField("General", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(serialized.FindProperty("displayName"));
            EditorGUILayout.PropertyField(serialized.FindProperty("id"));

            string currentId = session.CurrentId;
            if (string.IsNullOrWhiteSpace(currentId))
            {
                EditorGUILayout.HelpBox(
                    "Empty ID — this item cannot be referenced by inventory or recipes until it has one.",
                    MessageType.Warning);
            }
            else
            {
                ItemDefinition collision = FindIdCollision(session, allItems, currentId.Trim());
                if (collision != null)
                {
                    EditorGUILayout.HelpBox(
                        $"ID '{currentId.Trim()}' is already used by '{collision.name}'. Database lookups will " +
                        "resolve to only one of them — change this ID before saving.",
                        MessageType.Error);
                }
            }

            EditorGUILayout.PropertyField(serialized.FindProperty("description"));
            EditorGUILayout.PropertyField(serialized.FindProperty("icon"));
            EditorGUILayout.PropertyField(serialized.FindProperty("category"));
            EditorGUILayout.PropertyField(serialized.FindProperty("maxStackSize"));
            EditorGUILayout.PropertyField(serialized.FindProperty("weightKg"));
            EditorGUILayout.PropertyField(serialized.FindProperty("worldModelPrefab"));

            var category = (ItemCategory)serialized.FindProperty("category").intValue;
            if (category == ItemCategory.Block || category == ItemCategory.Placeable)
            {
                DrawPlacedBlockField(session, serialized, allBlocks, blockDatabaseAvailable);
                EditorGUILayout.PropertyField(
                    serialized.FindProperty("placedPiece"),
                    new GUIContent("Placed Piece",
                        "Building piece this item lets the player place (ghost preview + snapping while equipped). " +
                        "Set Placed Block OR Placed Piece, never both."));
            }
        }

        private void DrawPlacedBlockField(
            ItemEditSession session,
            SerializedObject serialized,
            IReadOnlyList<BlockDefinition> allBlocks,
            bool blockDatabaseAvailable)
        {
            SerializedProperty placedBlock = serialized.FindProperty("placedBlock");

            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.PrefixLabel(new GUIContent("Placed Block", placedBlock.tooltip));

                var current = placedBlock.objectReferenceValue as BlockDefinition;
                string buttonLabel = current != null ? $"{current.DisplayName} ({current.Id})" : "(None)";

                Rect buttonRect = GUILayoutUtility.GetRect(
                    new GUIContent(buttonLabel), EditorStyles.popup, GUILayout.MinWidth(120f));

                using (new EditorGUI.DisabledScope(!blockDatabaseAvailable))
                {
                    if (GUI.Button(buttonRect, buttonLabel, EditorStyles.popup))
                    {
                        var dropdown = new BlockDefinitionDropdown(placedBlockDropdownState, allBlocks, picked =>
                        {
                            session.Serialized.FindProperty("placedBlock").objectReferenceValue = picked;
                            owner.Repaint();
                        });
                        dropdown.Show(buttonRect);
                    }
                }

                EditorGUILayout.PropertyField(placedBlock, GUIContent.none, GUILayout.Width(110f));
            }
        }

        // ------------------------------------------------------------------
        // Weapon
        // ------------------------------------------------------------------

        private static void DrawWeaponSection(SerializedObject serialized)
        {
            EditorGUILayout.LabelField("Weapon", EditorStyles.boldLabel);
            SerializedProperty isWeapon = serialized.FindProperty("isWeapon");
            EditorGUILayout.PropertyField(isWeapon);

            if (!isWeapon.boolValue)
                return;

            using (new EditorGUI.IndentLevelScope())
            {
                EditorGUILayout.PropertyField(serialized.FindProperty("weaponDamage"));
                EditorGUILayout.PropertyField(serialized.FindProperty("damageType"));
                EditorGUILayout.PropertyField(serialized.FindProperty("attacksPerSecond"));
                EditorGUILayout.PropertyField(serialized.FindProperty("attackRange"));
                EditorGUILayout.PropertyField(serialized.FindProperty("attackClip"));
            }
        }

        // ------------------------------------------------------------------
        // Tool
        // ------------------------------------------------------------------

        private void DrawToolSection(
            SerializedObject serialized, IReadOnlyList<BlockDefinition> allBlocks, bool blockDatabaseAvailable)
        {
            EditorGUILayout.LabelField("Tool", EditorStyles.boldLabel);
            SerializedProperty isTool = serialized.FindProperty("isTool");
            EditorGUILayout.PropertyField(isTool);

            if (!isTool.boolValue)
                return;

            using (new EditorGUI.IndentLevelScope())
            {
                EditorGUILayout.PropertyField(serialized.FindProperty("toolType"));
                EditorGUILayout.PropertyField(serialized.FindProperty("toolTier"));
                EditorGUILayout.PropertyField(serialized.FindProperty("miningSpeedMultiplier"));
                EditorGUILayout.PropertyField(serialized.FindProperty("miningRadius"));

                EditorGUILayout.Space(2f);
                if (blockDatabaseAvailable)
                {
                    efficientBlocks.OnGUI(serialized.FindProperty("efficientBlocks"), allBlocks);
                    EditorGUILayout.HelpBox(
                        "PERMISSION to mine a block is the tier check (Tool Tier vs the block's Required Tool " +
                        "Tier) — it scales automatically as blocks are added. This list only grants the Mining " +
                        "Speed Multiplier; unlisted blocks within tier mine at bare-hand speed.",
                        MessageType.None);
                }
                else
                {
                    EditorGUILayout.HelpBox(
                        "BlockDatabase not found — the Efficient Blocks list needs it. Run Island Game/Data/Sync Databases.",
                        MessageType.Warning);
                }
            }
        }

        // ------------------------------------------------------------------
        // Holding
        // ------------------------------------------------------------------

        private static void DrawHoldSection(ItemEditSession session, SerializedObject serialized)
        {
            EditorGUILayout.LabelField("Holding", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(serialized.FindProperty("holdType"));

            if (session.CurrentHoldType == HoldType.None)
                return;

            using (new EditorGUI.IndentLevelScope())
            {
                EditorGUILayout.PropertyField(serialized.FindProperty("holdSocket"));
                EditorGUILayout.PropertyField(serialized.FindProperty("holdLocalPosition"));
                EditorGUILayout.PropertyField(serialized.FindProperty("holdLocalRotationEuler"));

                Vector3 position = session.CurrentHoldLocalPosition;
                Vector3 rotation = session.CurrentHoldLocalRotationEuler;
                EditorGUILayout.HelpBox(
                    "Attachment (held-item phase reads exactly this):\n" +
                    $"parent  '{HoldSocketConvention.GetSocketTransformName(session.CurrentHoldSocket)}'\n" +
                    $"localPosition  ({position.x:0.###}, {position.y:0.###}, {position.z:0.###})\n" +
                    $"localRotation  ({rotation.x:0.#}°, {rotation.y:0.#}°, {rotation.z:0.#}°)\n" +
                    "Toggle 'Hold Offset' above the preview to see the model under the socket axes (X red, Y green, Z blue).",
                    MessageType.None);
            }
        }

        // ------------------------------------------------------------------
        // Validation
        // ------------------------------------------------------------------

        private static void DrawValidationSection(ItemEditSession session, SerializedObject serialized)
        {
            EditorGUILayout.LabelField("Validation", EditorStyles.boldLabel);

            bool isWeapon = serialized.FindProperty("isWeapon").boolValue;
            bool isTool = serialized.FindProperty("isTool").boolValue;
            HoldType holdType = session.CurrentHoldType;
            var category = (ItemCategory)serialized.FindProperty("category").intValue;
            var placedBlock = serialized.FindProperty("placedBlock").objectReferenceValue as BlockDefinition;
            var placedPiece = serialized.FindProperty("placedPiece").objectReferenceValue as BuildingPieceDefinition;

            bool anyIssue = false;

            if (!isWeapon && !isTool && holdType != HoldType.None)
            {
                anyIssue = true;
                EditorGUILayout.HelpBox(
                    "Held, but neither weapon nor tool. That's fine for placeables, torches and the like — " +
                    "just confirming it's intentional.",
                    MessageType.Info);
            }

            if ((isWeapon || isTool) && holdType == HoldType.None)
            {
                anyIssue = true;
                EditorGUILayout.HelpBox(
                    "This is a weapon/tool but Hold Type is None — it can never be equipped or used. Set a Hold Type.",
                    MessageType.Warning);
            }

            if ((category == ItemCategory.Block || category == ItemCategory.Placeable)
                && placedBlock == null && placedPiece == null)
            {
                anyIssue = true;
                EditorGUILayout.HelpBox(
                    $"Category is {category} but neither Placed Block nor Placed Piece is set — using this item cannot place anything.",
                    MessageType.Warning);
            }

            if (placedBlock != null && placedPiece != null)
            {
                anyIssue = true;
                EditorGUILayout.HelpBox(
                    "Both Placed Block and Placed Piece are set — the place button would fight itself. Clear one.",
                    MessageType.Error);
            }

            if ((placedBlock != null || placedPiece != null)
                && category != ItemCategory.Block && category != ItemCategory.Placeable)
            {
                anyIssue = true;
                EditorGUILayout.HelpBox(
                    $"Placed Block/Piece is set but Category is {category} — placement logic only runs for Block/Placeable items.",
                    MessageType.Warning);
            }

            if (placedBlock != null && placedBlock.DropItem != session.Target)
            {
                anyIssue = true;
                EditorGUILayout.HelpBox(
                    $"One-way link: this item places '{placedBlock.DisplayName}', but that block drops " +
                    $"{(placedBlock.DropItem != null ? $"'{placedBlock.DropItem.DisplayName}'" : "nothing")}. " +
                    "Intentional for processed materials; otherwise set the block's Drop Item to this item.",
                    MessageType.Info);
            }

            if (isTool && (ToolType)serialized.FindProperty("toolType").intValue == ToolType.None)
            {
                anyIssue = true;
                EditorGUILayout.HelpBox("Is Tool is on but Tool Type is None — pick what kind of tool this is.", MessageType.Warning);
            }

            if (isWeapon && serialized.FindProperty("attackClip").objectReferenceValue == null)
            {
                anyIssue = true;
                EditorGUILayout.HelpBox(
                    "No Attack Clip assigned — the held-item phase will need one to play swings.",
                    MessageType.Info);
            }

            if (!anyIssue)
                EditorGUILayout.LabelField("No issues found.", EditorStyles.centeredGreyMiniLabel);
        }

        // ------------------------------------------------------------------
        // Helpers
        // ------------------------------------------------------------------

        private static ItemDefinition FindIdCollision(
            ItemEditSession session, IReadOnlyList<ItemDefinition> allItems, string id)
        {
            for (int i = 0; i < allItems.Count; i++)
            {
                ItemDefinition other = allItems[i];
                if (other != null && other != session.Target && other.Id == id)
                    return other;
            }

            return null;
        }
    }
}
