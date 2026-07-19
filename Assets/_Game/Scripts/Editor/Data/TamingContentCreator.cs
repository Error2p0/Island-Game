using System.Linq;
using IslandGame.Data.Creatures;
using IslandGame.Data.Stats;
using IslandGame.Stats;
using UnityEditor;
using UnityEngine;
using static IslandGame.EditorTools.Data.BaseContentSetGenerator;

namespace IslandGame.EditorTools.Data
{
    /// <summary>
    /// Flags the taming-phase example species — a guarded migration in the
    /// content set's ApplyMigrations style (existing creature assets are
    /// upgraded IN PLACE, but only while their Tameable flag is still the
    /// untouched default, so a hand-tuned taming setup is never clobbered):
    ///
    ///   WOLF — the companion archetype: favorite food Raw Meat, 4 feedings,
    ///     ASSIST-capable (it already fights through the pack/combat logic),
    ///     tamed vigor: +25% health, +15% attack damage.
    ///   DEER — the pacifist pet: Berries, 3 feedings, no assist, +20%
    ///     health. Proves the non-combat companion path.
    /// </summary>
    public static class TamingContentCreator
    {
        [MenuItem("Island Game/Data/Create Taming Content")]
        public static void Create()
        {
            MakeTameable("wolf",
                foods: new[] { "raw_meat" }, feedings: 4, canAssist: true,
                modifiers: new[]
                {
                    (StatIds.Health, StatModifierTarget.Value, StatModifierType.PercentMultiplier, 0.25f),
                    (StatIds.AttackDamage, StatModifierTarget.Value, StatModifierType.PercentMultiplier, 0.15f),
                });

            MakeTameable("deer",
                foods: new[] { "berries" }, feedings: 3, canAssist: false,
                modifiers: new[]
                {
                    (StatIds.Health, StatModifierTarget.Value, StatModifierType.PercentMultiplier, 0.2f),
                });

            AssetDatabase.SaveAssets();
            Debug.Log(
                "Taming content ready: Wolf (raw meat ×4, assist-capable) and Deer (berries ×3) are tameable. " +
                "Hold the food, walk up, press E to feed; the interact-cycle commands unlock once tamed.");
        }

        private static void MakeTameable(
            string creatureId, string[] foods, int feedings, bool canAssist,
            (string statId, StatModifierTarget target, StatModifierType type, float value)[] modifiers)
        {
            CreatureDefinition definition = AssetDatabase.FindAssets("t:CreatureDefinition")
                .Select(AssetDatabase.GUIDToAssetPath)
                .Select(AssetDatabase.LoadAssetAtPath<CreatureDefinition>)
                .FirstOrDefault(c => c != null && c.Id == creatureId);

            if (definition == null)
            {
                Debug.LogWarning($"[Taming] Creature '{creatureId}' not found — run Island Game/Data/Create Example Creatures first.");
                return;
            }

            if (definition.Tameable)
                return; // already configured (possibly hand-tuned) — never clobber

            var serialized = new SerializedObject(definition);
            serialized.FindProperty("tameable").boolValue = true;
            serialized.FindProperty("feedingsToTame").intValue = feedings;
            serialized.FindProperty("canAssistInCombat").boolValue = canAssist;

            SerializedProperty foodList = serialized.FindProperty("favoriteFoods");
            foodList.arraySize = foods.Length;
            for (int i = 0; i < foods.Length; i++)
            {
                var item = FindItem(foods[i]);
                if (item == null)
                    Debug.LogWarning($"[Taming] '{creatureId}': favorite food item '{foods[i]}' not found.");
                foodList.GetArrayElementAtIndex(i).objectReferenceValue = item;
            }

            SerializedProperty modifierList = serialized.FindProperty("tamedStatModifiers");
            modifierList.arraySize = modifiers.Length;
            for (int i = 0; i < modifiers.Length; i++)
            {
                SerializedProperty element = modifierList.GetArrayElementAtIndex(i);
                element.FindPropertyRelative("statId").stringValue = modifiers[i].statId;
                element.FindPropertyRelative("target").intValue = (int)modifiers[i].target;
                element.FindPropertyRelative("type").intValue = (int)modifiers[i].type;
                element.FindPropertyRelative("value").floatValue = modifiers[i].value;
            }

            serialized.ApplyModifiedPropertiesWithoutUndo();
            Debug.Log($"[Taming] Migration: '{creatureId}' is now tameable ({feedings} feedings{(canAssist ? ", assist-capable" : string.Empty)}).");
        }
    }
}
