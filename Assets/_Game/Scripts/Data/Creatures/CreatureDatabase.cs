using UnityEngine;

namespace IslandGame.Data.Creatures
{
    /// <summary>
    /// Registry of every CreatureDefinition in the project. The asset lives at
    /// Assets/_Game/Resources/Databases/CreatureDatabase.asset and is created
    /// and kept in sync by Island Game/Data/Sync Databases (automatic on
    /// definition import/delete/move) — there is deliberately no
    /// CreateAssetMenu, never create one by hand.
    /// </summary>
    public sealed class CreatureDatabase : DefinitionDatabase<CreatureDefinition>
    {
        /// <summary>Path under Resources/ — must match DefinitionDatabaseSync.DatabaseFolder.</summary>
        public const string ResourcesPath = "Databases/CreatureDatabase";

        private static CreatureDatabase instance;

        /// <summary>Global access for runtime systems (spawners, structure population, save/load later).</summary>
        public static CreatureDatabase Instance
        {
            get
            {
                if (instance == null)
                {
                    instance = Resources.Load<CreatureDatabase>(ResourcesPath);
                    if (instance == null)
                        Debug.LogError(
                            $"CreatureDatabase asset not found at Resources/{ResourcesPath}. " +
                            "Run Island Game/Data/Sync Databases in the editor to create it.");
                }

                return instance;
            }
        }
    }
}
