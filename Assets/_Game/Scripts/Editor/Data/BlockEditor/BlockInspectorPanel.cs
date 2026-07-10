using System.Collections.Generic;
using IslandGame.Data.Blocks;
using IslandGame.Data.Items;
using UnityEditor;
using UnityEditor.IMGUI.Controls;
using UnityEngine;

namespace IslandGame.EditorTools.Data
{
    /// <summary>
    /// Right panel of the Block Editor: inspector-style fields for the selected
    /// block's edit session. Pure view over the session's SerializedObject —
    /// everything drawn here edits the unapplied buffer, so nothing hits disk
    /// until the window's Save button applies it.
    /// </summary>
    internal sealed class BlockInspectorPanel
    {
        private const float FaceThumbnailSize = 48f;

        private readonly EditorWindow owner;
        private readonly AdvancedDropdownState dropItemDropdownState = new AdvancedDropdownState();

        private Vector2 scroll;

        public BlockInspectorPanel(EditorWindow owner)
        {
            this.owner = owner;
        }

        public void OnGUI(
            BlockEditSession session,
            IReadOnlyList<BlockDefinition> allBlocks,
            IReadOnlyList<ItemDefinition> allItems,
            bool itemDatabaseAvailable)
        {
            SerializedObject serialized = session.Serialized;

            using (var scrollScope = new EditorGUILayout.ScrollViewScope(scroll))
            {
                scroll = scrollScope.scrollPosition;

                DrawIdentitySection(session, serialized, allBlocks);
                EditorGUILayout.Space(6f);
                DrawTextureSection(session, serialized);
                EditorGUILayout.Space(6f);
                DrawPhysicsSection(serialized);
                EditorGUILayout.Space(6f);
                DrawMiningSection(serialized);
                EditorGUILayout.Space(6f);
                DrawDropsSection(session, serialized, allItems, itemDatabaseAvailable);
                EditorGUILayout.Space(6f);
                DrawBehaviorSection(serialized);
                EditorGUILayout.Space(8f);
            }
        }

        // ------------------------------------------------------------------
        // Sections
        // ------------------------------------------------------------------

        private void DrawIdentitySection(
            BlockEditSession session, SerializedObject serialized, IReadOnlyList<BlockDefinition> allBlocks)
        {
            EditorGUILayout.LabelField("Identity", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(serialized.FindProperty("displayName"));
            EditorGUILayout.PropertyField(serialized.FindProperty("id"));

            string currentId = session.CurrentId;
            if (string.IsNullOrWhiteSpace(currentId))
            {
                EditorGUILayout.HelpBox(
                    "Empty ID — this block cannot be referenced by terrain or items until it has one.",
                    MessageType.Warning);
                return;
            }

            BlockDefinition collision = FindIdCollision(session, allBlocks, currentId.Trim());
            if (collision != null)
            {
                EditorGUILayout.HelpBox(
                    $"ID '{currentId.Trim()}' is already used by '{collision.name}'. Database lookups will " +
                    "resolve to only one of them — change this ID before saving.",
                    MessageType.Error);
            }
        }

        private void DrawTextureSection(BlockEditSession session, SerializedObject serialized)
        {
            EditorGUILayout.LabelField("Textures", EditorStyles.boldLabel);

            SerializedProperty uniformToggle = serialized.FindProperty("useUniformTexture");
            SerializedProperty uniformTexture = serialized.FindProperty("uniformTexture");

            EditorGUILayout.PropertyField(uniformToggle, new GUIContent("Same Texture On All Faces"));

            if (uniformToggle.boolValue)
            {
                EditorGUILayout.PropertyField(uniformTexture, new GUIContent("Texture"));
                DrawThumbnail(session.GetFaceTexture(BlockFace.Top), 72f);
            }
            else
            {
                EditorGUILayout.PropertyField(uniformTexture, new GUIContent(
                    "Fallback Texture", "Used by any face slot left empty below."));

                using (new EditorGUI.DisabledScope(uniformTexture.objectReferenceValue == null))
                {
                    if (GUILayout.Button(new GUIContent(
                            "Copy Fallback To All Face Slots",
                            "Quick-fill: writes the fallback texture into all six face slots so you can then vary individual faces.")))
                    {
                        foreach (BlockFace face in BlockFaces.All)
                        {
                            serialized.FindProperty(BlockEditSession.FacePropertyName(face)).objectReferenceValue =
                                uniformTexture.objectReferenceValue;
                        }
                    }
                }

                EditorGUILayout.Space(2f);
                for (int i = 0; i < BlockFaces.Count; i += 2)
                {
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        DrawFaceCell(session, serialized, BlockFaces.All[i]);
                        DrawFaceCell(session, serialized, BlockFaces.All[i + 1]);
                    }
                }
            }

            EditorGUILayout.HelpBox(
                "Block textures must be imported with Read/Write enabled (runtime atlas packing reads their " +
                "pixels). Missing textures show as magenta/black in the preview and in-game.",
                MessageType.None);
        }

        private void DrawFaceCell(BlockEditSession session, SerializedObject serialized, BlockFace face)
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox, GUILayout.MinWidth(140f)))
            {
                EditorGUILayout.LabelField(face.ToString(), EditorStyles.miniBoldLabel);
                using (new EditorGUILayout.HorizontalScope())
                {
                    DrawThumbnail(session.GetFaceTexture(face), FaceThumbnailSize);
                    using (new EditorGUILayout.VerticalScope())
                    {
                        GUILayout.FlexibleSpace();
                        EditorGUILayout.PropertyField(
                            serialized.FindProperty(BlockEditSession.FacePropertyName(face)), GUIContent.none);
                        GUILayout.FlexibleSpace();
                    }
                }
            }
        }

        private static void DrawThumbnail(Texture2D texture, float size)
        {
            Rect rect = GUILayoutUtility.GetRect(size, size, GUILayout.Width(size), GUILayout.Height(size));

            if (texture != null)
            {
                GUI.DrawTexture(rect, texture, ScaleMode.ScaleToFit);
            }
            else
            {
                EditorGUI.DrawRect(rect, new Color(0.15f, 0.15f, 0.15f));
                GUI.Label(rect, "None", EditorStyles.centeredGreyMiniLabel);
            }
        }

        private static void DrawPhysicsSection(SerializedObject serialized)
        {
            EditorGUILayout.LabelField("Physics & Rendering", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(serialized.FindProperty("isSolid"));
            EditorGUILayout.PropertyField(serialized.FindProperty("isTransparent"));
        }

        private static void DrawMiningSection(SerializedObject serialized)
        {
            EditorGUILayout.LabelField("Mining", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(serialized.FindProperty("hardness"));
            EditorGUILayout.PropertyField(serialized.FindProperty("requiredToolTier"));
        }

        private void DrawDropsSection(
            BlockEditSession session,
            SerializedObject serialized,
            IReadOnlyList<ItemDefinition> allItems,
            bool itemDatabaseAvailable)
        {
            EditorGUILayout.LabelField("Drops", EditorStyles.boldLabel);

            SerializedProperty dropItem = serialized.FindProperty("dropItem");

            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.PrefixLabel(new GUIContent("Drop Item", dropItem.tooltip));

                var current = dropItem.objectReferenceValue as ItemDefinition;
                string buttonLabel = current != null ? $"{current.DisplayName} ({current.Id})" : "(None)";

                Rect buttonRect = GUILayoutUtility.GetRect(
                    new GUIContent(buttonLabel), EditorStyles.popup, GUILayout.MinWidth(120f));

                using (new EditorGUI.DisabledScope(!itemDatabaseAvailable))
                {
                    if (GUI.Button(buttonRect, buttonLabel, EditorStyles.popup))
                    {
                        // The callback fires after this OnGUI pass; write into the
                        // session buffer (still unapplied = still revertable).
                        var dropdown = new ItemDefinitionDropdown(dropItemDropdownState, allItems, picked =>
                        {
                            session.Serialized.FindProperty("dropItem").objectReferenceValue = picked;
                            owner.Repaint();
                        });
                        dropdown.Show(buttonRect);
                    }
                }

                // Drag-and-drop / object-picker alternative to the search dropdown.
                EditorGUILayout.PropertyField(dropItem, GUIContent.none, GUILayout.Width(110f));
            }

            if (!itemDatabaseAvailable)
            {
                EditorGUILayout.HelpBox(
                    "ItemDatabase not found — the search dropdown is disabled. Run Island Game/Data/Sync Databases.",
                    MessageType.Warning);
            }

            EditorGUILayout.PropertyField(serialized.FindProperty("dropCountMin"));
            EditorGUILayout.PropertyField(serialized.FindProperty("dropCountMax"));

            SerializedProperty min = serialized.FindProperty("dropCountMin");
            SerializedProperty max = serialized.FindProperty("dropCountMax");
            if (max.intValue < min.intValue)
                EditorGUILayout.HelpBox("Drop Count Max is below Min — it will be clamped up on save.", MessageType.Info);
        }

        private static void DrawBehaviorSection(SerializedObject serialized)
        {
            EditorGUILayout.LabelField("Behavior", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(serialized.FindProperty("behaviorFlags"));
        }

        // ------------------------------------------------------------------
        // Helpers
        // ------------------------------------------------------------------

        private static BlockDefinition FindIdCollision(
            BlockEditSession session, IReadOnlyList<BlockDefinition> allBlocks, string id)
        {
            for (int i = 0; i < allBlocks.Count; i++)
            {
                BlockDefinition other = allBlocks[i];
                if (other != null && other != session.Target && other.Id == id)
                    return other;
            }

            return null;
        }
    }
}
