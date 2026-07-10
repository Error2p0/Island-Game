using System.Collections.Generic;
using IslandGame.Data.Blocks;
using IslandGame.Data.Items;
using UnityEditor;
using UnityEngine;

namespace IslandGame.EditorTools.Data
{
    /// <summary>
    /// Block Editor window (Island Game → World → Block Editor): authoring UI
    /// for BlockDefinition assets. Left panel lists/searches the BlockDatabase;
    /// right panel is a live 3D cube preview plus inspector fields editing an
    /// unapplied SerializedObject buffer (BlockEditSession), so Save/Revert are
    /// real operations and the ● unsaved marker is meaningful.
    ///
    /// The window only edits assets on disk — it never touches play-mode game
    /// state. New blocks are created in Assets/_Game/Content/Blocks (project
    /// convention from Phase 1). Deleting warns about inbound references via
    /// BlockReferenceScanner. Pending edits are auto-saved before assembly
    /// reloads/play mode (SerializedObject buffers don't survive domain reload).
    /// </summary>
    internal sealed class BlockEditorWindow : EditorWindow
    {
        private const string BlockContentFolder = "Assets/_Game/Content/Blocks";
        private const float ListWidth = 280f;
        private const float PreviewHeight = 240f;
        private const double RepaintInterval = 1.0 / 30.0;

        private static readonly IReadOnlyList<ItemDefinition> NoItems = new List<ItemDefinition>();

        private BlockDatabase blockDatabase;
        private ItemDatabase itemDatabase;

        private BlockListPanel listPanel;
        private BlockInspectorPanel inspectorPanel;
        private BlockCubePreview preview;
        private BlockEditSession session;

        private double lastRepaintTime;

        [MenuItem("Island Game/World/Block Editor")]
        public static void Open()
        {
            var window = GetWindow<BlockEditorWindow>();
            window.titleContent = new GUIContent("Block Editor");
            window.minSize = new Vector2(720f, 480f);
        }

        private void OnEnable()
        {
            listPanel = new BlockListPanel();
            inspectorPanel = new BlockInspectorPanel(this);
            preview = new BlockCubePreview();

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
            // Databases may have been (re)created by a sync while unfocused.
            if (blockDatabase == null || itemDatabase == null)
                RefreshDatabases();
        }

        private void OnGUI()
        {
            if (session != null && !session.IsValid)
                session = null; // asset deleted out from under us

            hasUnsavedChanges = session != null && session.HasUnsavedChanges;
            saveChangesMessage = session != null && session.IsValid
                ? $"Block '{session.CurrentDisplayName}' has unsaved changes."
                : string.Empty;

            if (blockDatabase == null)
            {
                DrawMissingDatabase();
                return;
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUILayout.VerticalScope(GUILayout.Width(ListWidth)))
                {
                    listPanel.OnGUI(
                        blockDatabase.All,
                        session?.Target,
                        session != null && session.HasUnsavedChanges,
                        SelectBlock,
                        CreateBlock,
                        DeleteBlock);
                }

                DrawVerticalSeparator();

                using (new EditorGUILayout.VerticalScope())
                {
                    if (session == null)
                        EditorGUILayout.LabelField(
                            "Select a block on the left, or create a new one.",
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
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                GUILayout.Label("Preview", EditorStyles.boldLabel);
                GUILayout.FlexibleSpace();
                preview.AutoRotate = GUILayout.Toggle(preview.AutoRotate, "Auto-Rotate", EditorStyles.toolbarButton);
                if (GUILayout.Button("Reset View", EditorStyles.toolbarButton))
                    preview.ResetView();
            }

            Rect previewRect = GUILayoutUtility.GetRect(100f, PreviewHeight, GUILayout.ExpandWidth(true));
            preview.Draw(previewRect, session.GetFaceTexture);

            EditorGUILayout.Space(4f);

            inspectorPanel.OnGUI(
                session,
                blockDatabase.All,
                itemDatabase != null ? itemDatabase.All : NoItems,
                itemDatabase != null);

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
                if (GUILayout.Button("Delete Block…", GUILayout.Height(26f), GUILayout.Width(110f)))
                    DeleteBlock(session.Target);
                GUI.backgroundColor = previous;
            }
        }

        private void DrawMissingDatabase()
        {
            EditorGUILayout.Space(12f);
            EditorGUILayout.HelpBox(
                "BlockDatabase asset not found (expected at Assets/_Game/Resources/Databases). " +
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

        private void SelectBlock(BlockDefinition block)
        {
            if (session?.Target == block)
                return;

            if (!ConfirmLeaveSession())
                return;

            session = block != null ? new BlockEditSession(block) : null;
            GUI.FocusControl(null); // text-field focus must not carry over between blocks
        }

        private void CreateBlock()
        {
            if (!ConfirmLeaveSession())
                return;

            DefinitionDatabaseSync.EnsureFolderExists(BlockContentFolder);
            string path = AssetDatabase.GenerateUniqueAssetPath($"{BlockContentFolder}/NewBlock.asset");

            var block = CreateInstance<BlockDefinition>();
            AssetDatabase.CreateAsset(block, path);
            AssetDatabase.SaveAssets();

            DefinitionDatabaseSync.SyncAll();
            RefreshDatabases();

            session = new BlockEditSession(block);
            EditorGUIUtility.PingObject(block);
        }

        private void DeleteBlock(BlockDefinition block)
        {
            if (block == null)
                return;

            List<string> references = BlockReferenceScanner.FindReferences(block);
            string displayName = string.IsNullOrEmpty(block.DisplayName) ? block.name : block.DisplayName;

            string message = references.Count > 0
                ? $"'{displayName}' is referenced by:\n\n{string.Join("\n", references)}\n\n" +
                  "Those references will become None if you delete it.\n\nDelete anyway?"
                : $"Delete '{displayName}'?\n\nThis cannot be undone.";

            if (!EditorUtility.DisplayDialog(
                    "Delete Block", message, references.Count > 0 ? "Delete Anyway" : "Delete", "Cancel"))
                return;

            if (session?.Target == block)
                session = null;

            AssetDatabase.DeleteAsset(AssetDatabase.GetAssetPath(block));
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
                $"Block '{session.CurrentDisplayName}' has unsaved changes.",
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
                Debug.Log($"Block Editor: auto-saved pending changes to '{session.Target.name}' before assembly reload.");
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
            blockDatabase = Resources.Load<BlockDatabase>(BlockDatabase.ResourcesPath);
            itemDatabase = Resources.Load<ItemDatabase>(ItemDatabase.ResourcesPath);
        }
    }
}
