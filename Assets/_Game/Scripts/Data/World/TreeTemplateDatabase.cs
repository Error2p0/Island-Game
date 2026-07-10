using UnityEngine;

namespace IslandGame.Data.World
{
    /// <summary>
    /// Registry of every TreeTemplateDefinition in the project. The asset
    /// lives at Assets/_Game/Resources/Databases/TreeTemplateDatabase.asset
    /// and is created and kept in sync by Island Game/Data/Sync Databases
    /// (automatic on definition import/delete/move) — there is deliberately
    /// no CreateAssetMenu, never create one by hand.
    /// </summary>
    public sealed class TreeTemplateDatabase : DefinitionDatabase<TreeTemplateDefinition>
    {
        /// <summary>Path under Resources/ — must match DefinitionDatabaseSync.DatabaseFolder.</summary>
        public const string ResourcesPath = "Databases/TreeTemplateDatabase";

        private static TreeTemplateDatabase instance;

        /// <summary>Global access for runtime systems (the world generator's tree scattering).</summary>
        public static TreeTemplateDatabase Instance
        {
            get
            {
                if (instance == null)
                {
                    instance = Resources.Load<TreeTemplateDatabase>(ResourcesPath);
                    if (instance == null)
                        Debug.LogError(
                            $"TreeTemplateDatabase asset not found at Resources/{ResourcesPath}. " +
                            "Run Island Game/Data/Sync Databases in the editor to create it.");
                }

                return instance;
            }
        }
    }
}
