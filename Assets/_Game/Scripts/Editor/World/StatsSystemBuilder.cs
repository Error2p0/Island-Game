using System.Collections.Generic;
using IslandGame.Data.Stats;
using IslandGame.EditorTools.Data;
using IslandGame.Player;
using IslandGame.Stats;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace IslandGame.EditorTools
{
    /// <summary>
    /// Wires the stats runtime onto the player (same builder workflow as the
    /// building-system builder): ensures StatContainer / PlayerHealth /
    /// PlayerSurvival / EquippedItemStatModifiers exist, and fills the
    /// container's stat list with every player StatDefinition (running the
    /// stat content creator first if the assets are missing). Idempotent —
    /// existing components, inspector tweaks and extra stats already on the
    /// container are left untouched; only MISSING stats are appended.
    /// </summary>
    public static class StatsSystemBuilder
    {
        /// <summary>
        /// The stats the PLAYER carries. Explicit since the creatures phase:
        /// the StatDatabase also holds creature-centric stats (move_speed,
        /// attack_damage, detection_radius) that must not land on the player.
        /// </summary>
        private static readonly string[] PlayerStatIds =
        {
            StatIds.Health, StatIds.Stamina, StatIds.Hunger, StatIds.Thirst,
            StatIds.Warmth, StatIds.MiningSpeed, StatIds.CarryCapacity,
        };

        [MenuItem("Island Game/World/Add Stats System To Player")]
        public static void Create()
        {
            var player = Object.FindFirstObjectByType<PlayerReferences>();
            if (player == null)
            {
                Debug.LogError(
                    "Stats System: no player in the scene (PlayerReferences not found) — " +
                    "run Tools/Island Game/Build Player Rig first.");
                return;
            }

            // Definitions first: the container is useless without them.
            var database = AssetDatabase.LoadAssetAtPath<StatDatabase>(
                $"{DefinitionDatabaseSync.DatabaseFolder}/StatDatabase.asset");
            if (database == null || database.Count == 0)
            {
                StatContentCreator.Create();
                database = AssetDatabase.LoadAssetAtPath<StatDatabase>(
                    $"{DefinitionDatabaseSync.DatabaseFolder}/StatDatabase.asset");
            }

            if (database == null)
            {
                Debug.LogError("Stats System: StatDatabase still missing after content creation — check earlier errors.");
                return;
            }

            bool changed = false;

            var container = player.GetComponent<StatContainer>();
            if (container == null)
            {
                container = Undo.AddComponent<StatContainer>(player.gameObject);
                changed = true;
                Debug.Log("Stats System: added StatContainer to the player.");
            }

            changed |= AppendMissingPlayerStats(container, database);

            changed |= EnsureComponent<PlayerHealth>(player.gameObject, "PlayerHealth (IDamageable → health stat, death/respawn)");
            changed |= EnsureComponent<PlayerSurvival>(player.gameObject, "PlayerSurvival (well-fed/starving/warmth rules)");
            changed |= EnsureComponent<EquippedItemStatModifiers>(player.gameObject, "EquippedItemStatModifiers (item equip buffs)");

            if (changed)
                EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
            else
                Debug.Log("Stats System: already set up — nothing to do.");
        }

        /// <summary>Appends every PLAYER stat missing from the container's serialized list (explicit id set — never the whole database).</summary>
        private static bool AppendMissingPlayerStats(StatContainer container, StatDatabase database)
        {
            var serialized = new SerializedObject(container);
            SerializedProperty list = serialized.FindProperty("stats");

            var present = new HashSet<StatDefinition>();
            for (int i = 0; i < list.arraySize; i++)
            {
                var existing = list.GetArrayElementAtIndex(i).objectReferenceValue as StatDefinition;
                if (existing != null)
                    present.Add(existing);
            }

            bool changed = false;
            foreach (string statId in PlayerStatIds)
            {
                if (!database.TryGet(statId, out StatDefinition stat) || present.Contains(stat))
                    continue;

                list.InsertArrayElementAtIndex(list.arraySize);
                list.GetArrayElementAtIndex(list.arraySize - 1).objectReferenceValue = stat;
                changed = true;
                Debug.Log($"Stats System: added stat '{stat.Id}' to the player's StatContainer.");
            }

            if (changed)
                serialized.ApplyModifiedPropertiesWithoutUndo();
            return changed;
        }

        private static bool EnsureComponent<T>(GameObject player, string logName) where T : Component
        {
            if (player.GetComponent<T>() != null)
                return false;

            Undo.AddComponent<T>(player);
            Debug.Log($"Stats System: added {logName} to the player.");
            return true;
        }
    }
}
