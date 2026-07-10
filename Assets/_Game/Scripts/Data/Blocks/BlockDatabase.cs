using UnityEngine;

namespace IslandGame.Data.Blocks
{
    /// <summary>
    /// Registry of every BlockDefinition in the project. The asset lives at
    /// Assets/_Game/Resources/Databases/BlockDatabase.asset and is created and
    /// kept in sync by Island Game/Data/Sync Databases (automatic on definition
    /// import/delete/move) — there is deliberately no CreateAssetMenu, never
    /// create one by hand.
    /// </summary>
    public sealed class BlockDatabase : DefinitionDatabase<BlockDefinition>
    {
        /// <summary>Path under Resources/ — must match DefinitionDatabaseSync.DatabaseFolder.</summary>
        public const string ResourcesPath = "Databases/BlockDatabase";

        private static BlockDatabase instance;

        /// <summary>Global access for runtime systems (terrain, mining, creative menu).</summary>
        public static BlockDatabase Instance
        {
            get
            {
                if (instance == null)
                {
                    instance = Resources.Load<BlockDatabase>(ResourcesPath);
                    if (instance == null)
                        Debug.LogError(
                            $"BlockDatabase asset not found at Resources/{ResourcesPath}. " +
                            "Run Island Game/Data/Sync Databases in the editor to create it.");
                }

                return instance;
            }
        }
    }
}
