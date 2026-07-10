using System.Collections.Generic;
using IslandGame.Data.Building;
using IslandGame.Data.Crafting;
using IslandGame.Data.Items;
using UnityEditor;
using UnityEngine;

namespace IslandGame.EditorTools.Data
{
    /// <summary>
    /// Recipe Editor window (Island Game → Crafting → Recipe Editor): the same
    /// list+detail structure and buffered Save/Revert editing model as the
    /// Block and Item editors (generic DefinitionListPanel on the left,
    /// unapplied-SerializedObject session on the right, ● unsaved markers,
    /// auto-save before domain reload, close-prompt integration). New recipes
    /// are created in Assets/_Game/Content/Recipes. Nothing references
    /// recipes yet, so deletion is a plain confirm.
    /// </summary>
    internal sealed class RecipeEditorWindow : EditorWindow
    {
        private const string RecipeContentFolder = "Assets/_Game/Content/Recipes";
        private const float ListWidth = 280f;

        private static readonly IReadOnlyList<ItemDefinition> NoItems = new List<ItemDefinition>();
        private static readonly IReadOnlyList<BuildingPieceDefinition> NoPieces = new List<BuildingPieceDefinition>();

        private RecipeDatabase recipeDatabase;
        private ItemDatabase itemDatabase;
        private BuildingPieceDatabase pieceDatabase;

        private DefinitionListPanel<RecipeDefinition> listPanel;
        private RecipeInspectorPanel inspectorPanel;
        private RecipeEditSession session;

        [MenuItem("Island Game/Crafting/Recipe Editor")]
        public static void Open()
        {
            var window = GetWindow<RecipeEditorWindow>();
            window.titleContent = new GUIContent("Recipe Editor");
            window.minSize = new Vector2(680f, 440f);
        }

        private void OnEnable()
        {
            listPanel = new DefinitionListPanel<RecipeDefinition>("New Recipe");
            inspectorPanel = new RecipeInspectorPanel(this);
            AssemblyReloadEvents.beforeAssemblyReload += AutoSaveBeforeReload;
            RefreshDatabases();
        }

        private void OnDisable()
        {
            AssemblyReloadEvents.beforeAssemblyReload -= AutoSaveBeforeReload;
        }

        private void OnFocus()
        {
            if (recipeDatabase == null || itemDatabase == null || pieceDatabase == null)
                RefreshDatabases();
        }

        private void OnGUI()
        {
            if (session != null && !session.IsValid)
                session = null; // asset deleted out from under us

            hasUnsavedChanges = session != null && session.HasUnsavedChanges;
            saveChangesMessage = session != null && session.IsValid
                ? $"Recipe '{session.CurrentDisplayName}' has unsaved changes."
                : string.Empty;

            if (recipeDatabase == null)
            {
                DrawMissingDatabase();
                return;
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUILayout.VerticalScope(GUILayout.Width(ListWidth)))
                {
                    listPanel.OnGUI(
                        recipeDatabase.All,
                        session?.Target,
                        session != null && session.HasUnsavedChanges,
                        SelectRecipe,
                        CreateRecipe,
                        DeleteRecipe);
                }

                DrawVerticalSeparator();

                using (new EditorGUILayout.VerticalScope())
                {
                    if (session == null)
                    {
                        EditorGUILayout.LabelField(
                            "Select a recipe on the left, or create a new one.",
                            EditorStyles.centeredGreyMiniLabel, GUILayout.ExpandHeight(true));
                    }
                    else
                    {
                        inspectorPanel.OnGUI(
                            session,
                            recipeDatabase.All,
                            itemDatabase != null ? itemDatabase.All : NoItems,
                            itemDatabase != null,
                            pieceDatabase != null ? pieceDatabase.All : NoPieces,
                            pieceDatabase != null);
                        DrawActionRow();
                    }
                }
            }
        }

        // ------------------------------------------------------------------
        // Actions
        // ------------------------------------------------------------------

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
                        GUI.FocusControl(null);
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
                if (GUILayout.Button("Delete Recipe…", GUILayout.Height(26f), GUILayout.Width(120f)))
                    DeleteRecipe(session.Target);
                GUI.backgroundColor = previous;
            }
        }

        private void DrawMissingDatabase()
        {
            EditorGUILayout.Space(12f);
            EditorGUILayout.HelpBox(
                "RecipeDatabase asset not found (expected at Assets/_Game/Resources/Databases). " +
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

        private void SelectRecipe(RecipeDefinition recipe)
        {
            if (session?.Target == recipe)
                return;

            if (!ConfirmLeaveSession())
                return;

            session = recipe != null ? new RecipeEditSession(recipe) : null;
            GUI.FocusControl(null);
        }

        private void CreateRecipe()
        {
            if (!ConfirmLeaveSession())
                return;

            DefinitionDatabaseSync.EnsureFolderExists(RecipeContentFolder);
            string path = AssetDatabase.GenerateUniqueAssetPath($"{RecipeContentFolder}/NewRecipe.asset");

            var recipe = CreateInstance<RecipeDefinition>();
            AssetDatabase.CreateAsset(recipe, path);
            AssetDatabase.SaveAssets();

            DefinitionDatabaseSync.SyncAll();
            RefreshDatabases();

            session = new RecipeEditSession(recipe);
            EditorGUIUtility.PingObject(recipe);
        }

        private void DeleteRecipe(RecipeDefinition recipe)
        {
            if (recipe == null)
                return;

            string displayName = string.IsNullOrEmpty(recipe.DisplayName) ? recipe.name : recipe.DisplayName;
            if (!EditorUtility.DisplayDialog(
                    "Delete Recipe", $"Delete '{displayName}'?\n\nThis cannot be undone.", "Delete", "Cancel"))
                return;

            if (session?.Target == recipe)
                session = null;

            AssetDatabase.DeleteAsset(AssetDatabase.GetAssetPath(recipe));
            DefinitionDatabaseSync.SyncAll();
            RefreshDatabases();
        }

        private bool ConfirmLeaveSession()
        {
            if (session == null || !session.IsValid || !session.HasUnsavedChanges)
                return true;

            int choice = EditorUtility.DisplayDialogComplex(
                "Unsaved Changes",
                $"Recipe '{session.CurrentDisplayName}' has unsaved changes.",
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
        // Plumbing
        // ------------------------------------------------------------------

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
            if (session != null && session.IsValid && session.HasUnsavedChanges)
            {
                session.Save();
                Debug.Log($"Recipe Editor: auto-saved pending changes to '{session.Target.name}' before assembly reload.");
            }
        }

        private void RefreshDatabases()
        {
            recipeDatabase = Resources.Load<RecipeDatabase>(RecipeDatabase.ResourcesPath);
            itemDatabase = Resources.Load<ItemDatabase>(ItemDatabase.ResourcesPath);
            pieceDatabase = Resources.Load<BuildingPieceDatabase>(BuildingPieceDatabase.ResourcesPath);
        }
    }
}
