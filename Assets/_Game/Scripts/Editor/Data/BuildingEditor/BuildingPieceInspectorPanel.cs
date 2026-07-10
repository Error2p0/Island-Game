using System.Collections.Generic;
using IslandGame.Building;
using IslandGame.Data.Building;
using IslandGame.Data.Items;
using UnityEditor;
using UnityEditor.IMGUI.Controls;
using UnityEngine;

namespace IslandGame.EditorTools.Data
{
    /// <summary>
    /// Right panel of the Building Piece Editor: inspector-style fields for
    /// the selected piece's edit session. Pure view over the session's
    /// SerializedObject — everything drawn here edits the unapplied buffer,
    /// so nothing hits disk until the window's Save button applies it.
    ///
    /// Owns socket selection (SelectedSocket): the selected socket is the
    /// expanded one in the list AND the highlighted gizmo in the preview.
    /// Also validates the prefab contract (root BuildingPiece component with
    /// a matching piece ID) and can fix it by stamping the prefab asset —
    /// the one deliberate exception to "only Save touches disk", flagged in
    /// its confirmation dialog.
    /// </summary>
    internal sealed class BuildingPieceInspectorPanel
    {
        private readonly EditorWindow owner;
        private readonly AdvancedDropdownState costItemDropdownState = new AdvancedDropdownState();

        private Vector2 scroll;

        public BuildingPieceInspectorPanel(EditorWindow owner)
        {
            this.owner = owner;
        }

        /// <summary>Index into the sockets list, -1 = none. The window feeds this to the preview highlight.</summary>
        public int SelectedSocket { get; private set; } = -1;

        public void OnGUI(
            BuildingPieceEditSession session,
            IReadOnlyList<BuildingPieceDefinition> allPieces,
            IReadOnlyList<ItemDefinition> allItems,
            bool itemDatabaseAvailable)
        {
            SerializedObject serialized = session.Serialized;

            if (SelectedSocket >= session.SocketCount)
                SelectedSocket = session.SocketCount - 1;

            using (var scrollScope = new EditorGUILayout.ScrollViewScope(scroll))
            {
                scroll = scrollScope.scrollPosition;

                DrawIdentitySection(session, serialized, allPieces);
                EditorGUILayout.Space(6f);
                DrawClassificationSection(serialized);
                EditorGUILayout.Space(6f);
                DrawPrefabSection(session, serialized);
                EditorGUILayout.Space(6f);
                DrawCostSection(session, serialized, allItems, itemDatabaseAvailable);
                EditorGUILayout.Space(6f);
                DrawDurabilitySection(serialized);
                EditorGUILayout.Space(6f);
                DrawSocketsSection(serialized);
                EditorGUILayout.Space(8f);
            }
        }

        // ------------------------------------------------------------------
        // Identity / classification
        // ------------------------------------------------------------------

        private static void DrawIdentitySection(
            BuildingPieceEditSession session, SerializedObject serialized, IReadOnlyList<BuildingPieceDefinition> allPieces)
        {
            EditorGUILayout.LabelField("Identity", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(serialized.FindProperty("displayName"));
            EditorGUILayout.PropertyField(serialized.FindProperty("id"));

            string currentId = session.CurrentId;
            if (string.IsNullOrWhiteSpace(currentId))
            {
                EditorGUILayout.HelpBox(
                    "Empty ID — this piece cannot be placed or saved into worlds until it has one.",
                    MessageType.Warning);
                return;
            }

            BuildingPieceDefinition collision = FindIdCollision(session, allPieces, currentId.Trim());
            if (collision != null)
            {
                EditorGUILayout.HelpBox(
                    $"ID '{currentId.Trim()}' is already used by '{collision.name}'. Database lookups will " +
                    "resolve to only one of them — change this ID before saving.",
                    MessageType.Error);
            }
        }

        private static void DrawClassificationSection(SerializedObject serialized)
        {
            EditorGUILayout.LabelField("Classification", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(serialized.FindProperty("category"));
            EditorGUILayout.PropertyField(serialized.FindProperty("description"));
            EditorGUILayout.PropertyField(serialized.FindProperty("icon"));
        }

        // ------------------------------------------------------------------
        // Prefab + contract validation
        // ------------------------------------------------------------------

        private void DrawPrefabSection(BuildingPieceEditSession session, SerializedObject serialized)
        {
            EditorGUILayout.LabelField("Placed Prefab", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(serialized.FindProperty("prefab"));

            GameObject prefab = session.CurrentPrefab;
            if (prefab == null)
            {
                EditorGUILayout.HelpBox(
                    "No prefab — the piece cannot be placed. Assign the prefab that should appear in the world.",
                    MessageType.Warning);
                return;
            }

            if (prefab.GetComponentInChildren<Collider>(true) == null)
            {
                EditorGUILayout.HelpBox(
                    "Prefab has no collider anywhere — placed pieces must block movement and take weapon hits.",
                    MessageType.Warning);
            }

            var buildingPiece = prefab.GetComponent<BuildingPiece>();
            string sessionId = session.CurrentId != null ? session.CurrentId.Trim() : string.Empty;

            if (buildingPiece == null)
            {
                EditorGUILayout.HelpBox(
                    "Prefab root has no BuildingPiece component. Placement, damage and functional init all go " +
                    "through it — the prefab contract requires it on the ROOT.",
                    MessageType.Error);
                DrawStampButton(prefab, sessionId, "Add BuildingPiece Component & Stamp ID");
            }
            else if (buildingPiece.PieceId != sessionId)
            {
                EditorGUILayout.HelpBox(
                    $"Prefab's BuildingPiece has Piece Id '{buildingPiece.PieceId}' but this definition's ID is " +
                    $"'{sessionId}'. Scene-authored instances of the prefab would resolve the wrong definition.",
                    MessageType.Warning);
                DrawStampButton(prefab, sessionId, "Stamp This ID Onto Prefab");
            }
        }

        private void DrawStampButton(GameObject prefab, string id, string label)
        {
            using (new EditorGUI.DisabledScope(string.IsNullOrEmpty(id)))
            {
                if (!GUILayout.Button(label))
                    return;
            }

            if (string.IsNullOrEmpty(id))
                return;

            if (!EditorUtility.DisplayDialog(
                    "Modify Prefab Asset",
                    $"This edits the prefab asset '{prefab.name}' on disk immediately (unlike the piece fields, " +
                    "which wait for Save): it ensures a BuildingPiece component on the root and sets its " +
                    $"Piece Id to '{id}'.\n\nContinue?",
                    "Modify Prefab", "Cancel"))
                return;

            StampPieceId(prefab, id);
            owner.Repaint();
        }

        private static void StampPieceId(GameObject prefab, string id)
        {
            string path = AssetDatabase.GetAssetPath(prefab);
            if (string.IsNullOrEmpty(path))
            {
                Debug.LogError($"'{prefab.name}' is not a prefab asset — assign a prefab from the project, not a scene object.");
                return;
            }

            GameObject root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                var piece = root.GetComponent<BuildingPiece>();
                if (piece == null)
                    piece = root.AddComponent<BuildingPiece>();

                var serializedPiece = new SerializedObject(piece);
                serializedPiece.FindProperty("pieceId").stringValue = id;
                serializedPiece.ApplyModifiedPropertiesWithoutUndo();

                PrefabUtility.SaveAsPrefabAsset(root, path);
                Debug.Log($"Stamped Piece Id '{id}' onto prefab '{prefab.name}'.", prefab);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        // ------------------------------------------------------------------
        // Build cost
        // ------------------------------------------------------------------

        private void DrawCostSection(
            BuildingPieceEditSession session,
            SerializedObject serialized,
            IReadOnlyList<ItemDefinition> allItems,
            bool itemDatabaseAvailable)
        {
            EditorGUILayout.LabelField("Build Cost", EditorStyles.boldLabel);

            SerializedProperty cost = serialized.FindProperty("materialCost");
            int pendingRemove = -1;

            for (int i = 0; i < cost.arraySize; i++)
            {
                SerializedProperty entry = cost.GetArrayElementAtIndex(i);
                SerializedProperty item = entry.FindPropertyRelative("item");
                SerializedProperty count = entry.FindPropertyRelative("count");

                using (new EditorGUILayout.HorizontalScope())
                {
                    var current = item.objectReferenceValue as ItemDefinition;
                    string buttonLabel = current != null ? $"{current.DisplayName} ({current.Id})" : "(None)";

                    Rect buttonRect = GUILayoutUtility.GetRect(
                        new GUIContent(buttonLabel), EditorStyles.popup, GUILayout.MinWidth(120f));

                    using (new EditorGUI.DisabledScope(!itemDatabaseAvailable))
                    {
                        if (GUI.Button(buttonRect, buttonLabel, EditorStyles.popup))
                        {
                            // The callback fires after this OnGUI pass; write into
                            // the session buffer (still unapplied = still revertable).
                            string propertyPath = item.propertyPath;
                            var dropdown = new ItemDefinitionDropdown(costItemDropdownState, allItems, picked =>
                            {
                                session.Serialized.FindProperty(propertyPath).objectReferenceValue = picked;
                                owner.Repaint();
                            });
                            dropdown.Show(buttonRect);
                        }
                    }

                    // Drag-and-drop / object-picker alternative to the search dropdown.
                    EditorGUILayout.PropertyField(item, GUIContent.none, GUILayout.Width(110f));
                    EditorGUILayout.PropertyField(count, GUIContent.none, GUILayout.Width(48f));

                    if (GUILayout.Button("X", GUILayout.Width(22f)))
                        pendingRemove = i;
                }
            }

            if (pendingRemove >= 0)
                cost.DeleteArrayElementAtIndex(pendingRemove);

            if (GUILayout.Button("Add Cost Line", GUILayout.Height(22f)))
            {
                cost.arraySize++;
                SerializedProperty added = cost.GetArrayElementAtIndex(cost.arraySize - 1);
                added.FindPropertyRelative("item").objectReferenceValue = null;
                added.FindPropertyRelative("count").intValue = 1;
            }

            if (!itemDatabaseAvailable)
            {
                EditorGUILayout.HelpBox(
                    "ItemDatabase not found — the search dropdown is disabled. Run Island Game/Data/Sync Databases.",
                    MessageType.Warning);
            }

            EditorGUILayout.HelpBox(
                "Placeholder until Phase 3 links pieces to real recipes — these counts seed those recipes and are " +
                "what the placement UI charges in the meantime.",
                MessageType.None);
        }

        private static void DrawDurabilitySection(SerializedObject serialized)
        {
            EditorGUILayout.LabelField("Durability", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(serialized.FindProperty("maxHealth"));
        }

        // ------------------------------------------------------------------
        // Sockets
        // ------------------------------------------------------------------

        private void DrawSocketsSection(SerializedObject serialized)
        {
            EditorGUILayout.LabelField("Snap Sockets", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "Socket +Z must point OUT of the piece, +Y up. Tag = what the socket IS; Accepted Tags = what may " +
                "snap onto it (either side accepting is enough to mate). Click a socket to select it — the preview " +
                "highlights it. Full convention: SnapSocket.cs / DataConventions.md.",
                MessageType.None);

            SerializedProperty sockets = serialized.FindProperty("sockets");
            int pendingRemove = -1;
            int pendingDuplicate = -1;

            for (int i = 0; i < sockets.arraySize; i++)
            {
                SerializedProperty socket = sockets.GetArrayElementAtIndex(i);
                bool selected = i == SelectedSocket;

                using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                {
                    if (DrawSocketHeader(socket, i, selected, out bool duplicate, out bool remove))
                        SelectedSocket = selected ? -1 : i;

                    if (duplicate)
                        pendingDuplicate = i;
                    if (remove)
                        pendingRemove = i;

                    if (selected)
                        DrawSocketFields(socket);
                }
            }

            if (sockets.arraySize == 0)
            {
                EditorGUILayout.LabelField(
                    "No sockets — the piece will only free-place (no snapping).", EditorStyles.centeredGreyMiniLabel);
            }

            if (pendingDuplicate >= 0)
            {
                sockets.InsertArrayElementAtIndex(pendingDuplicate); // inserts a copy of [i] at [i+1]
                SerializedProperty copy = sockets.GetArrayElementAtIndex(pendingDuplicate + 1);
                SerializedProperty copyName = copy.FindPropertyRelative("socketName");
                copyName.stringValue = copyName.stringValue + "_copy";
                SelectedSocket = pendingDuplicate + 1;
            }
            else if (pendingRemove >= 0)
            {
                sockets.DeleteArrayElementAtIndex(pendingRemove);
                if (SelectedSocket >= sockets.arraySize)
                    SelectedSocket = sockets.arraySize - 1;
            }

            if (GUILayout.Button("Add Socket", GUILayout.Height(22f)))
            {
                sockets.arraySize++;
                SerializedProperty added = sockets.GetArrayElementAtIndex(sockets.arraySize - 1);
                added.FindPropertyRelative("socketName").stringValue = $"socket_{sockets.arraySize - 1}";
                added.FindPropertyRelative("localPosition").vector3Value = Vector3.zero;
                added.FindPropertyRelative("localRotationEuler").vector3Value = Vector3.zero;
                added.FindPropertyRelative("tag").stringValue = string.Empty;
                added.FindPropertyRelative("acceptedTags").arraySize = 0;
                SelectedSocket = sockets.arraySize - 1;
            }
        }

        /// <summary>Header row: swatch + name/tag button (returns true on click), Dup and X buttons.</summary>
        private static bool DrawSocketHeader(
            SerializedProperty socket, int index, bool selected, out bool duplicate, out bool remove)
        {
            duplicate = false;
            remove = false;
            bool clicked = false;

            string socketName = socket.FindPropertyRelative("socketName").stringValue;
            string tag = socket.FindPropertyRelative("tag").stringValue;

            using (new EditorGUILayout.HorizontalScope())
            {
                Rect swatch = GUILayoutUtility.GetRect(14f, 14f, GUILayout.Width(14f));
                swatch.y += 2f;
                swatch.height = 12f;
                EditorGUI.DrawRect(swatch, SnapTagColors.Get(tag));

                string label = string.IsNullOrEmpty(socketName) ? $"socket {index}" : socketName;
                string tagLabel = string.IsNullOrEmpty(tag) ? "(no tag)" : tag;
                var style = selected ? EditorStyles.boldLabel : EditorStyles.label;

                if (GUILayout.Button($"{label}   —   {tagLabel}", style, GUILayout.ExpandWidth(true)))
                    clicked = true;

                if (GUILayout.Button("Dup", EditorStyles.miniButton, GUILayout.Width(34f)))
                    duplicate = true;
                if (GUILayout.Button("X", EditorStyles.miniButton, GUILayout.Width(22f)))
                    remove = true;
            }

            return clicked;
        }

        private static void DrawSocketFields(SerializedProperty socket)
        {
            EditorGUILayout.PropertyField(socket.FindPropertyRelative("socketName"));
            EditorGUILayout.PropertyField(socket.FindPropertyRelative("localPosition"));
            EditorGUILayout.PropertyField(
                socket.FindPropertyRelative("localRotationEuler"), new GUIContent("Local Rotation (Euler)"));

            DrawTagField(socket.FindPropertyRelative("tag"), new GUIContent("Tag", "What this socket IS."));

            EditorGUILayout.LabelField("Accepted Tags", EditorStyles.miniBoldLabel);
            SerializedProperty accepted = socket.FindPropertyRelative("acceptedTags");
            int pendingRemove = -1;

            for (int i = 0; i < accepted.arraySize; i++)
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    DrawTagField(accepted.GetArrayElementAtIndex(i), GUIContent.none);
                    if (GUILayout.Button("X", GUILayout.Width(22f)))
                        pendingRemove = i;
                }
            }

            if (pendingRemove >= 0)
                accepted.DeleteArrayElementAtIndex(pendingRemove);

            if (GUILayout.Button("Add Accepted Tag", EditorStyles.miniButton))
            {
                accepted.arraySize++;
                accepted.GetArrayElementAtIndex(accepted.arraySize - 1).stringValue = string.Empty;
            }
        }

        /// <summary>Tag string field plus a ▾ picker of the standard SnapTags (custom text stays legal).</summary>
        private static void DrawTagField(SerializedProperty tagProperty, GUIContent label)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.PropertyField(tagProperty, label);

                if (GUILayout.Button("▾", EditorStyles.miniButton, GUILayout.Width(20f)))
                {
                    // GenericMenu callbacks fire after this OnGUI pass — capture
                    // the path, not the SerializedProperty (iterator state moves on).
                    var serialized = tagProperty.serializedObject;
                    string path = tagProperty.propertyPath;

                    var menu = new GenericMenu();
                    foreach (string standardTag in SnapTags.All)
                    {
                        string value = standardTag;
                        menu.AddItem(new GUIContent(value), tagProperty.stringValue == value, () =>
                        {
                            serialized.FindProperty(path).stringValue = value;
                        });
                    }

                    menu.ShowAsContext();
                }
            }
        }

        // ------------------------------------------------------------------
        // Helpers
        // ------------------------------------------------------------------

        private static BuildingPieceDefinition FindIdCollision(
            BuildingPieceEditSession session, IReadOnlyList<BuildingPieceDefinition> allPieces, string id)
        {
            for (int i = 0; i < allPieces.Count; i++)
            {
                BuildingPieceDefinition other = allPieces[i];
                if (other != null && other != session.Target && other.Id == id)
                    return other;
            }

            return null;
        }
    }
}
