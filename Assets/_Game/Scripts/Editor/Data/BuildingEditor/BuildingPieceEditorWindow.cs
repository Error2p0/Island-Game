using System.Collections.Generic;
using IslandGame.Data.Building;
using IslandGame.Data.Items;
using UnityEditor;
using UnityEngine;

namespace IslandGame.EditorTools.Data
{
    /// <summary>
    /// Building Piece Editor window (Island Game → World → Building Piece
    /// Editor): authoring UI for BuildingPieceDefinition assets. Left panel
    /// lists/searches the BuildingPieceDatabase (shared DefinitionListPanel);
    /// right panel is a live 3D preview of the prefab with its snap-socket
    /// gizmos (colored by tag, selected socket highlighted) plus inspector
    /// fields editing an unapplied SerializedObject buffer
    /// (BuildingPieceEditSession), so Save/Revert are real operations and the
    /// ● unsaved marker is meaningful.
    ///
    /// The window only edits assets on disk — it never touches play-mode game
    /// state. New pieces are created in Assets/_Game/Content/Building/Pieces.
    /// Pending edits are auto-saved before assembly reloads/play mode
    /// (SerializedObject buffers don't survive domain reload).
    /// </summary>
    internal sealed class BuildingPieceEditorWindow : EditorWindow
    {
        private const string PieceContentFolder = "Assets/_Game/Content/Building/Pieces";
        private const float ListWidth = 280f;
        private const float PreviewHeight = 280f;
        private const double RepaintInterval = 1.0 / 30.0;

        private static readonly IReadOnlyList<ItemDefinition> NoItems = new List<ItemDefinition>();

        private BuildingPieceDatabase pieceDatabase;
        private ItemDatabase itemDatabase;

        private DefinitionListPanel<BuildingPieceDefinition> listPanel;
        private BuildingPieceInspectorPanel inspectorPanel;
        private BuildingPiecePreview preview;
        private BuildingPieceEditSession session;

        private readonly List<SocketPreviewData> socketBuffer = new List<SocketPreviewData>();
        private bool showSockets = true;
        private double lastRepaintTime;

        [MenuItem("Island Game/World/Building Piece Editor")]
        public static void Open()
        {
            var window = GetWindow<BuildingPieceEditorWindow>();
            window.titleContent = new GUIContent("Building Piece Editor");
            window.minSize = new Vector2(760f, 520f);
        }

        private void OnEnable()
        {
            listPanel = new DefinitionListPanel<BuildingPieceDefinition>("New Building Piece");
            inspectorPanel = new BuildingPieceInspectorPanel(this);
            preview = new BuildingPiecePreview();

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
            if (pieceDatabase == null || itemDatabase == null)
                RefreshDatabases();
        }

        private void OnGUI()
        {
            if (session != null && !session.IsValid)
                session = null; // asset deleted out from under us

            hasUnsavedChanges = session != null && session.HasUnsavedChanges;
            saveChangesMessage = session != null && session.IsValid
                ? $"Building piece '{session.CurrentDisplayName}' has unsaved changes."
                : string.Empty;

            if (pieceDatabase == null)
            {
                DrawMissingDatabase();
                return;
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUILayout.VerticalScope(GUILayout.Width(ListWidth)))
                {
                    listPanel.OnGUI(
                        pieceDatabase.All,
                        session?.Target,
                        session != null && session.HasUnsavedChanges,
                        SelectPiece,
                        CreatePiece,
                        DeletePiece);
                }

                DrawVerticalSeparator();

                using (new EditorGUILayout.VerticalScope())
                {
                    if (session == null)
                        EditorGUILayout.LabelField(
                            "Select a building piece on the left, or create a new one.",
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
                showSockets = GUILayout.Toggle(showSockets, "Show Sockets", EditorStyles.toolbarButton);
                preview.AutoRotate = GUILayout.Toggle(preview.AutoRotate, "Auto-Rotate", EditorStyles.toolbarButton);
                if (GUILayout.Button("Reset View", EditorStyles.toolbarButton))
                    preview.ResetView();
            }

            session.GetSockets(socketBuffer);
            Rect previewRect = GUILayoutUtility.GetRect(100f, PreviewHeight, GUILayout.ExpandWidth(true));
            preview.Draw(previewRect, session.CurrentPrefab, socketBuffer, inspectorPanel.SelectedSocket, showSockets);

            EditorGUILayout.Space(4f);

            inspectorPanel.OnGUI(
                session,
                pieceDatabase.All,
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
                if (GUILayout.Button("Delete Piece…", GUILayout.Height(26f), GUILayout.Width(110f)))
                    DeletePiece(session.Target);
                GUI.backgroundColor = previous;
            }
        }

        private void DrawMissingDatabase()
        {
            EditorGUILayout.Space(12f);
            EditorGUILayout.HelpBox(
                "BuildingPieceDatabase asset not found (expected at Assets/_Game/Resources/Databases). " +
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

        private void SelectPiece(BuildingPieceDefinition piece)
        {
            if (session?.Target == piece)
                return;

            if (!ConfirmLeaveSession())
                return;

            session = piece != null ? new BuildingPieceEditSession(piece) : null;
            GUI.FocusControl(null); // text-field focus must not carry over between pieces
        }

        private void CreatePiece()
        {
            if (!ConfirmLeaveSession())
                return;

            DefinitionDatabaseSync.EnsureFolderExists(PieceContentFolder);
            string path = AssetDatabase.GenerateUniqueAssetPath($"{PieceContentFolder}/NewBuildingPiece.asset");

            var piece = CreateInstance<BuildingPieceDefinition>();
            AssetDatabase.CreateAsset(piece, path);
            AssetDatabase.SaveAssets();

            DefinitionDatabaseSync.SyncAll();
            RefreshDatabases();

            session = new BuildingPieceEditSession(piece);
            EditorGUIUtility.PingObject(piece);
        }

        private void DeletePiece(BuildingPieceDefinition piece)
        {
            if (piece == null)
                return;

            string displayName = string.IsNullOrEmpty(piece.DisplayName) ? piece.name : piece.DisplayName;

            // No reference scan yet: nothing references pieces until Phase 3
            // links recipes — the scan gets added alongside that link.
            if (!EditorUtility.DisplayDialog(
                    "Delete Building Piece",
                    $"Delete '{displayName}'?\n\nPlaced instances in saved worlds will fail to resolve this ID. " +
                    "This cannot be undone.",
                    "Delete", "Cancel"))
                return;

            if (session?.Target == piece)
                session = null;

            AssetDatabase.DeleteAsset(AssetDatabase.GetAssetPath(piece));
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
                $"Building piece '{session.CurrentDisplayName}' has unsaved changes.",
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
                Debug.Log($"Building Piece Editor: auto-saved pending changes to '{session.Target.name}' before assembly reload.");
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
            pieceDatabase = Resources.Load<BuildingPieceDatabase>(BuildingPieceDatabase.ResourcesPath);
            itemDatabase = Resources.Load<ItemDatabase>(ItemDatabase.ResourcesPath);
        }
    }
}
