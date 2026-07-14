using UnityEngine;

namespace IslandGame.Data.Foliage
{
    /// <summary>
    /// Registry of every FoliageDefinition in the project. The asset lives at
    /// Assets/_Game/Resources/Databases/FoliageDatabase.asset and is created
    /// and kept in sync by Island Game/Data/Sync Databases (automatic on
    /// definition import/delete/move) — there is deliberately no
    /// CreateAssetMenu, never create one by hand.
    /// </summary>
    public sealed class FoliageDatabase : DefinitionDatabase<FoliageDefinition>
    {
        /// <summary>Path under Resources/ — must match DefinitionDatabaseSync.DatabaseFolder.</summary>
        public const string ResourcesPath = "Databases/FoliageDatabase";

        private static FoliageDatabase instance;

        /// <summary>Global access for runtime systems (the foliage scatter system).</summary>
        public static FoliageDatabase Instance
        {
            get
            {
                if (instance == null)
                {
                    instance = Resources.Load<FoliageDatabase>(ResourcesPath);
                    if (instance == null)
                        Debug.LogError(
                            $"FoliageDatabase asset not found at Resources/{ResourcesPath}. " +
                            "Run Island Game/Data/Sync Databases in the editor to create it.");
                }

                return instance;
            }
        }
    }
}
