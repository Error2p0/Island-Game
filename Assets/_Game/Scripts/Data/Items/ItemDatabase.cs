using UnityEngine;

namespace IslandGame.Data.Items
{
    /// <summary>
    /// Registry of every ItemDefinition in the project. The asset lives at
    /// Assets/_Game/Resources/Databases/ItemDatabase.asset and is created and
    /// kept in sync by Island Game/Data/Sync Databases (automatic on definition
    /// import/delete/move) — there is deliberately no CreateAssetMenu, never
    /// create one by hand.
    /// </summary>
    public sealed class ItemDatabase : DefinitionDatabase<ItemDefinition>
    {
        /// <summary>Path under Resources/ — must match DefinitionDatabaseSync.DatabaseFolder.</summary>
        public const string ResourcesPath = "Databases/ItemDatabase";

        private static ItemDatabase instance;

        /// <summary>Global access for runtime systems (inventory, crafting, creative menu).</summary>
        public static ItemDatabase Instance
        {
            get
            {
                if (instance == null)
                {
                    instance = Resources.Load<ItemDatabase>(ResourcesPath);
                    if (instance == null)
                        Debug.LogError(
                            $"ItemDatabase asset not found at Resources/{ResourcesPath}. " +
                            "Run Island Game/Data/Sync Databases in the editor to create it.");
                }

                return instance;
            }
        }
    }
}
