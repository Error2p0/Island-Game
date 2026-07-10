using UnityEngine;

namespace IslandGame.Data.Building
{
    /// <summary>
    /// Registry of every BuildingPieceDefinition in the project. The asset
    /// lives at Assets/_Game/Resources/Databases/BuildingPieceDatabase.asset
    /// and is created and kept in sync by Island Game/Data/Sync Databases
    /// (automatic on definition import/delete/move) — there is deliberately
    /// no CreateAssetMenu, never create one by hand.
    /// </summary>
    public sealed class BuildingPieceDatabase : DefinitionDatabase<BuildingPieceDefinition>
    {
        /// <summary>Path under Resources/ — must match DefinitionDatabaseSync.DatabaseFolder.</summary>
        public const string ResourcesPath = "Databases/BuildingPieceDatabase";

        private static BuildingPieceDatabase instance;

        /// <summary>Global access for runtime systems (placement, build menu, save/load).</summary>
        public static BuildingPieceDatabase Instance
        {
            get
            {
                if (instance == null)
                {
                    instance = Resources.Load<BuildingPieceDatabase>(ResourcesPath);
                    if (instance == null)
                        Debug.LogError(
                            $"BuildingPieceDatabase asset not found at Resources/{ResourcesPath}. " +
                            "Run Island Game/Data/Sync Databases in the editor to create it.");
                }

                return instance;
            }
        }
    }
}
