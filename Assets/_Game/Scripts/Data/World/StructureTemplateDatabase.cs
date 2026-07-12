using UnityEngine;

namespace IslandGame.Data.World
{
    /// <summary>
    /// Registry of every StructureTemplate in the project. The asset lives at
    /// Assets/_Game/Resources/Databases/StructureTemplateDatabase.asset and is
    /// created and kept in sync by Island Game/Data/Sync Databases (automatic
    /// on definition import/delete/move) — there is deliberately no
    /// CreateAssetMenu, never create one by hand.
    /// </summary>
    public sealed class StructureTemplateDatabase : DefinitionDatabase<StructureTemplate>
    {
        /// <summary>Path under Resources/ — must match DefinitionDatabaseSync.DatabaseFolder.</summary>
        public const string ResourcesPath = "Databases/StructureTemplateDatabase";

        private static StructureTemplateDatabase instance;

        /// <summary>Global access for the placement system (and world save/load later).</summary>
        public static StructureTemplateDatabase Instance
        {
            get
            {
                if (instance == null)
                {
                    instance = Resources.Load<StructureTemplateDatabase>(ResourcesPath);
                    if (instance == null)
                        Debug.LogError(
                            $"StructureTemplateDatabase asset not found at Resources/{ResourcesPath}. " +
                            "Run Island Game/Data/Sync Databases in the editor to create it.");
                }

                return instance;
            }
        }
    }
}
