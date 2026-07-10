using System.Collections.Generic;
using IslandGame.Data.Blocks;
using IslandGame.Data.Items;
using UnityEditor;
using UnityEngine;

namespace IslandGame.EditorTools.Data
{
    /// <summary>
    /// Item Editor window (Island Game → Items → Item Editor): authoring UI
    /// for ItemDefinition assets, deliberately mirroring the Block Editor's
    /// structure — searchable list on the left; live rotatable preview (the
    /// world-model prefab, with an optional hold-offset view against the
    /// socket axes), inspector sections and a Save/Revert/Delete action row on
    /// the right. Editing goes through an unapplied SerializedObject buffer
    /// (ItemEditSession) so Save/Revert are real operations.
    ///
    /// The window only edits assets on disk — never play-mode state. New items
    /// are created in Assets/_Game/Content/Items (project convention).
    /// Deleting warns about inbound references (blocks whose Drop Item is this
    /// item) via ItemReferenceScanner. Pending edits auto-save before assembly
    /// reloads/play mode.
    /// </summary>
    internal sealed class ItemEditorWindow : EditorWindow
    {
        private const string ItemContentFolder = "Assets/_Game/Content/Items";
        private const float ListWidth = 280f;
        private const float PreviewHeight = 240f;
        private const double RepaintInterval = 1.0 / 30.0;

        private static readonly IReadOnlyList<BlockDefinition> NoBlocks = new List<BlockDefinition>();

        private ItemDatabase itemDatabase;
        private BlockDatabase blockDatabase;

        private DefinitionListPanel<ItemDefinition> listPanel;
        private ItemInspectorPanel inspectorPanel;
        private ItemModelPreview preview;
        private ItemEditSession session;

        private bool showHoldOffset;
        private double lastRepaintTime;

        [MenuItem("Island Game/Items/Item Editor")]
        public static void Open()
        {
            var window = GetWindow<ItemEditorWindow>();
            window.titleContent = new GUIContent("Item Editor");
            window.minSize = new Vector2(720f, 520f);
        }

        private void OnEnable()
        {
            listPanel = new DefinitionListPanel<ItemDefinition>("New Item");
            inspectorPanel = new ItemInspectorPanel(this);
            preview = new ItemModelPreview();

            EditorApplication.update += DriveRepaint;
            AssemblyReloadEvents.beforeAssemblyReload += AutoSaveBeforeReload;

            RefreshDatabases();
        }

        private void OnDisable()
        {
            EditorApplication.update -= DriveRepaint;
            AssemblyReloadEvents.beforeAssemblyReload -= AutoSaveBeforeReload;
            preview.Dispose();
        }

        private void OnFocus()
        {
            if (itemDatabase == null || blockDatabase == null)
                RefreshDatabases();
        }

        private void OnGUI()
        {
            if (session != null && !session.IsValid)
                session = null; // asset deleted out from under us

            hasUnsavedChanges = session != null && session.HasUnsavedChanges;
            saveChangesMessage = session != null && session.IsValid
                ? $"Item '{session.CurrentDisplayName}' has unsaved changes."
                : string.Empty;

            if (itemDatabase == null)
            {
                DrawMissingDatabase();
                return;
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUILayout.VerticalScope(GUILayout.Width(ListWidth)))
                {
                    listPanel.OnGUI(
                        itemDatabase.All,
                        session?.Target,
                        session != null && session.HasUnsavedChanges,
                        SelectItem,
                        CreateItem,
                        DeleteItem);
                }

                DrawVerticalSeparator();

                using (new EditorGUILayout.VerticalScope())
                {
                    if (session == null)
                        EditorGUILayout.LabelField(
                            "Select an item on the left, or create a new one.",
                            EditorStyles.centeredGreyMiniLabel, GUILayout.ExpandHeight(true));
                    else
                        DrawEditor();
                }
            }
        }

        // ------------------------------------------------------------------
        // Right side
        // ------------------------------------------------------------------

        private void DrawEditor()
        {
            bool canShowHoldOffset = session.CurrentHoldType != HoldType.None;
            if (!canShowHoldOffset)
                showHoldOffset = false;

            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                GUILayout.Label("Preview", EditorStyles.boldLabel);
                GUILayout.FlexibleSpace();

                using (new EditorGUI.DisabledScope(!canShowHoldOffset))
                {
                    showHoldOffset = GUILayout.Toggle(
                        showHoldOffset,
                        new GUIContent("Hold Offset", "Show the model under the hold socket axes (X red, Y green, Z blue) using the authored local offset."),
                        EditorStyles.toolbarButton);
                }

                preview.AutoRotate = GUILayout.Toggle(preview.AutoRotate, "Auto-Rotate", EditorStyles.toolbarButton);
                if (GUILayout.Button("Reset View", EditorStyles.toolbarButton))
                    preview.ResetView();
            }

            Rect previewRect = GUILayoutUtility.GetRect(100f, PreviewHeight, GUILayout.ExpandWidth(true));
            preview.Draw(
                previewRect,
                session.CurrentWorldModelPrefab,
                showHoldOffset,
                session.CurrentHoldLocalPosition,
                session.CurrentHoldLocalRotationEuler);

            EditorGUILayout.Space(4f);

            inspectorPanel.OnGUI(
                session,
                itemDatabase.All,
                blockDatabase != null ? blockDatabase.All : NoBlocks,
                blockDatabase != null);

            DrawActionRow();
        }

        private void DrawActionRow()
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.helpBox))
            {
                bool dirty = session.HasUnsavedChanges;

                using (new EditorGUI.DisabledScope(!dirty))
                {
                    if (GUILayout.Button("Save", GUILayout.Height(26f), GUILayout.Width(90f)))
                    {
                        session.Save();
                        GUI.FocusControl(null); // drop text-field focus so stale edits can't reappear
                    }

                    if (GUILayout.Button("Revert", GUILayout.Height(26f), GUILayout.Width(90f)))
                    {
                        session.Revert();
                        GUI.FocusControl(null);
                    }
                }

                GUILayout.Label(dirty ? "● Unsaved changes" : "Saved", EditorStyles.miniLabel, GUILayout.Height(26f));
                GUILayout.FlexibleSpace();

                Color previous = GUI.backgroundColor;
                GUI.backgroundColor = new Color(1f, 0.55f, 0.5f);
                if (GUILayout.Button("Delete Item…", GUILayout.Height(26f), GUILayout.Width(110f)))
                    DeleteItem(session.Target);
                GUI.backgroundColor = previous;
            }
        }

        private void DrawMissingDatabase()
        {
            EditorGUILayout.Space(12f);
            EditorGUILayout.HelpBox(
                "ItemDatabase asset not found (expected at Assets/_Game/Resources/Databases). " +
                "Sync the databases to create it.",
                MessageType.Warning);

            if (GUILayout.Button("Sync Databases Now", GUILayout.Height(28f)))
            {
                DefinitionDatabaseSync.SyncAll();
                RefreshDatabases();
            }
        }

        private static void DrawVerticalSeparator()
        {
            Rect rect = GUILayoutUtility.GetRect(1f, 1f, GUILayout.Width(1f), GUILayout.ExpandHeight(true));
            EditorGUI.DrawRect(rect, new Color(0f, 0f, 0f, 0.35f));
        }

        // ------------------------------------------------------------------
        // Selection / create / delete
        // ------------------------------------------------------------------

        private void SelectItem(ItemDefinition item)
        {
            if (session?.Target == item)
                return;

            if (!ConfirmLeaveSession())
                return;

            session = item != null ? new ItemEditSession(item) : null;
            GUI.FocusControl(null); // text-field focus must not carry over between items
        }

        private void CreateItem()
        {
            if (!ConfirmLeaveSession())
                return;

            DefinitionDatabaseSync.EnsureFolderExists(ItemContentFolder);
            string path = AssetDatabase.GenerateUniqueAssetPath($"{ItemContentFolder}/NewItem.asset");

            var item = CreateInstance<ItemDefinition>();
            AssetDatabase.CreateAsset(item, path);
            AssetDatabase.SaveAssets();

            DefinitionDatabaseSync.SyncAll();
            RefreshDatabases();

            session = new ItemEditSession(item);
            EditorGUIUtility.PingObject(item);
        }

        private void DeleteItem(ItemDefinition item)
        {
            if (item == null)
                return;

            List<string> references = ItemReferenceScanner.FindReferences(item);
            string displayName = string.IsNullOrEmpty(item.DisplayName) ? item.name : item.DisplayName;

            string message = references.Count > 0
                ? $"'{displayName}' is referenced by:\n\n{string.Join("\n", references)}\n\n" +
                  "Those references will become None if you delete it.\n\nDelete anyway?"
                : $"Delete '{displayName}'?\n\nThis cannot be undone.";

            if (!EditorUtility.DisplayDialog(
                    "Delete Item", message, references.Count > 0 ? "Delete Anyway" : "Delete", "Cancel"))
                return;

            if (session?.Target == item)
                session = null;

            AssetDatabase.DeleteAsset(AssetDatabase.GetAssetPath(item));
            DefinitionDatabaseSync.SyncAll();
            RefreshDatabases();
        }

        /// <summary>Save/Discard/Cancel prompt guarding selection changes away from a dirty session.</summary>
        private bool ConfirmLeaveSession()
        {
            if (session == null || !session.IsValid || !session.HasUnsavedChanges)
                return true;

            int choice = EditorUtility.DisplayDialogComplex(
                "Unsaved Changes",
                $"Item '{session.CurrentDisplayName}' has unsaved changes.",
                "Save", "Cancel", "Discard");

            switch (choice)
            {
                case 0:
                    session.Save();
                    return true;
                case 2:
                    session.Revert();
                    return true;
                default:
                    return false;
            }
        }

        // ------------------------------------------------------------------
        // Window plumbing
        // ------------------------------------------------------------------

        /// <summary>Unity calls this from the close-with-unsaved-changes dialog (hasUnsavedChanges).</summary>
        public override void SaveChanges()
        {
            session?.Save();
            base.SaveChanges();
        }

        public override void DiscardChanges()
        {
            session?.Revert();
            base.DiscardChanges();
        }

        private void AutoSaveBeforeReload()
        {
            // SerializedObject buffers don't survive domain reloads (recompile,
            // play mode). Losing edits silently is worse than saving them.
            if (session != null && session.IsValid && session.HasUnsavedChanges)
            {
                session.Save();
                Debug.Log($"Item Editor: auto-saved pending changes to '{session.Target.name}' before assembly reload.");
            }
        }

        private void DriveRepaint()
        {
            if (session == null || !preview.AutoRotate)
                return;

            double now = EditorApplication.timeSinceStartup;
            if (now - lastRepaintTime < RepaintInterval)
                return;

            lastRepaintTime = now;
            Repaint();
        }

        private void RefreshDatabases()
        {
            // Deliberately not the Instance properties: those log a load error
            // per call, and a missing database is a state this window explains
            // in its own UI instead.
            itemDatabase = Resources.Load<ItemDatabase>(ItemDatabase.ResourcesPath);
            blockDatabase = Resources.Load<BlockDatabase>(BlockDatabase.ResourcesPath);
        }
    }
}
