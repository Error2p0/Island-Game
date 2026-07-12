using UnityEngine;

namespace IslandGame.Data.Stats
{
    /// <summary>
    /// Registry of every StatDefinition in the project. The asset lives at
    /// Assets/_Game/Resources/Databases/StatDatabase.asset and is created and
    /// kept in sync by Island Game/Data/Sync Databases (automatic on definition
    /// import/delete/move) — there is deliberately no CreateAssetMenu, never
    /// create one by hand.
    /// </summary>
    public sealed class StatDatabase : DefinitionDatabase<StatDefinition>
    {
        /// <summary>Path under Resources/ — must match DefinitionDatabaseSync.DatabaseFolder.</summary>
        public const string ResourcesPath = "Databases/StatDatabase";

        private static StatDatabase instance;

        /// <summary>Global access for runtime systems (stat containers, HUD, save/load later).</summary>
        public static StatDatabase Instance
        {
            get
            {
                if (instance == null)
                {
                    instance = Resources.Load<StatDatabase>(ResourcesPath);
                    if (instance == null)
                        Debug.LogError(
                            $"StatDatabase asset not found at Resources/{ResourcesPath}. " +
                            "Run Island Game/Data/Sync Databases in the editor to create it.");
                }

                return instance;
            }
        }
    }
}
