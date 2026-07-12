using System;
using System.Collections.Generic;
using System.Linq;
using IslandGame.Data;
using IslandGame.Data.Blocks;
using IslandGame.Data.Building;
using IslandGame.Data.Crafting;
using IslandGame.Data.Creatures;
using IslandGame.Data.Items;
using IslandGame.Data.Stats;
using IslandGame.Data.World;
using UnityEditor;
using UnityEngine;

namespace IslandGame.EditorTools.Data
{
    /// <summary>
    /// Keeps ItemDatabase/BlockDatabase in sync with every definition asset in
    /// the project, so designers never maintain registry lists by hand.
    /// Definitions can live anywhere under Assets/; only the two small database
    /// assets live in Resources (at DatabaseFolder) so runtime can load them.
    /// Runs from the menu and automatically whenever a definition asset is
    /// imported, deleted or moved (see DefinitionDatabasePostprocessor below).
    /// </summary>
    public static class DefinitionDatabaseSync
    {
        /// <summary>Must stay consistent with ItemDatabase.ResourcesPath / BlockDatabase.ResourcesPath.</summary>
        public const string DatabaseFolder = "Assets/_Game/Resources/Databases";

        [MenuItem("Island Game/Data/Sync Databases")]
        public static void SyncAll()
        {
            SyncDatabase<ItemDefinition, ItemDatabase>("ItemDatabase");
            SyncDatabase<BlockDefinition, BlockDatabase>("BlockDatabase");
            SyncDatabase<RecipeDefinition, RecipeDatabase>("RecipeDatabase");
            SyncDatabase<BuildingPieceDefinition, BuildingPieceDatabase>("BuildingPieceDatabase");
            SyncDatabase<TreeTemplateDefinition, TreeTemplateDatabase>("TreeTemplateDatabase");
            SyncDatabase<StatDefinition, StatDatabase>("StatDatabase");
            SyncDatabase<CreatureDefinition, CreatureDatabase>("CreatureDatabase");
            AssetDatabase.SaveAssets();
        }

        private static void SyncDatabase<TDefinition, TDatabase>(string assetName)
            where TDefinition : ScriptableObject, IDefinition
            where TDatabase : DefinitionDatabase<TDefinition>
        {
            TDatabase database = LoadOrCreateDatabase<TDatabase>(assetName);

            List<TDefinition> definitions = AssetDatabase.FindAssets("t:" + typeof(TDefinition).Name)
                .Select(AssetDatabase.GUIDToAssetPath)
                .Select(AssetDatabase.LoadAssetAtPath<TDefinition>)
                .Where(definition => definition != null)
                .OrderBy(definition => definition.Id, StringComparer.Ordinal)
                .ToList();

            database.EditorSetDefinitions(definitions);
            database.RebuildLookup();
            EditorUtility.SetDirty(database);

            List<string> errors = database.Validate();
            foreach (string error in errors)
                Debug.LogError($"[{assetName}] {error}", database);

            Debug.Log(
                errors.Count == 0
                    ? $"[{assetName}] Synced {definitions.Count} definition(s), no problems."
                    : $"[{assetName}] Synced {definitions.Count} definition(s) with {errors.Count} problem(s) — see errors above.",
                database);
        }

        private static TDatabase LoadOrCreateDatabase<TDatabase>(string assetName)
            where TDatabase : ScriptableObject
        {
            string assetPath = $"{DatabaseFolder}/{assetName}.asset";
            var database = AssetDatabase.LoadAssetAtPath<TDatabase>(assetPath);
            if (database != null)
                return database;

            EnsureFolderExists(DatabaseFolder);
            database = ScriptableObject.CreateInstance<TDatabase>();
            AssetDatabase.CreateAsset(database, assetPath);
            Debug.Log($"Created {assetPath}.", database);
            return database;
        }

        internal static void EnsureFolderExists(string folder)
        {
            string[] parts = folder.Split('/');
            string current = parts[0];
            for (int i = 1; i < parts.Length; i++)
            {
                string next = current + "/" + parts[i];
                if (!AssetDatabase.IsValidFolder(next))
                    AssetDatabase.CreateFolder(current, parts[i]);
                current = next;
            }
        }
    }

    /// <summary>
    /// Auto-resyncs the databases when definition assets change, so the
    /// registries can never silently drift from the project's content. Runs
    /// once per import batch via delayCall.
    /// </summary>
    internal sealed class DefinitionDatabasePostprocessor : AssetPostprocessor
    {
        private static bool syncQueued;

        private static void OnPostprocessAllAssets(
            string[] importedAssets, string[] deletedAssets, string[] movedAssets, string[] movedFromAssetPaths)
        {
            if (syncQueued || !AnyDefinitionChanged(importedAssets, deletedAssets, movedAssets))
                return;

            syncQueued = true;
            EditorApplication.delayCall += () =>
            {
                syncQueued = false;
                DefinitionDatabaseSync.SyncAll();
            };
        }

        private static bool AnyDefinitionChanged(string[] imported, string[] deleted, string[] moved)
        {
            foreach (string path in imported)
            {
                if (IsDefinitionAsset(path))
                    return true;
            }

            foreach (string path in moved)
            {
                if (IsDefinitionAsset(path))
                    return true;
            }

            // A deleted asset's type is no longer queryable, so any deleted
            // .asset triggers a (cheap) resync. The sync's own database saves
            // arrive as IMPORTED database assets, which IsDefinitionAsset
            // rejects — so syncing cannot re-trigger itself.
            foreach (string path in deleted)
            {
                if (path.EndsWith(".asset", StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            return false;
        }

        private static bool IsDefinitionAsset(string path)
        {
            if (!path.EndsWith(".asset", StringComparison.OrdinalIgnoreCase))
                return false;

            Type type = AssetDatabase.GetMainAssetTypeAtPath(path);
            return type == typeof(ItemDefinition) || type == typeof(BlockDefinition)
                || type == typeof(RecipeDefinition) || type == typeof(BuildingPieceDefinition)
                || type == typeof(TreeTemplateDefinition) || type == typeof(StatDefinition)
                || type == typeof(CreatureDefinition);
        }
    }
}
