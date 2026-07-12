using IslandGame.Data.Stats;
using UnityEditor;
using UnityEngine;

namespace IslandGame.EditorTools.Data
{
    /// <summary>
    /// Creates the player's StatDefinition assets (Health, Stamina, Hunger,
    /// Thirst, Warmth, Mining Speed, Carry Capacity) in
    /// Assets/_Game/Content/Stats and syncs the databases. Idempotent, same
    /// contract as every content creator: existing assets are left untouched
    /// so hand-tuned values survive re-running it.
    ///
    /// TUNING NOTES baked into the defaults:
    ///   - stamina regen 20/s + 0.75s delay reproduces the movement phase's
    ///     normalized 0.2/s + 0.75s exactly (bar is 100 units).
    ///   - hunger decays 2.5/min (the old PlayerStats default); thirst is a
    ///     touch faster at 3.5/min so drinking matters first.
    ///   - warmth's +0.5/s day regen is deliberately SMALLER than
    ///     PlayerSurvival's default night reduction (0.85/s), leaving a net
    ///     -0.35/s night drain — a cold night without a fire hurts but is
    ///     survivable (~5 min from full at the default 20-minute day).
    /// </summary>
    public static class StatContentCreator
    {
        private const string StatFolder = "Assets/_Game/Content/Stats";

        [MenuItem("Island Game/Data/Create Player Stat Definitions")]
        public static void Create()
        {
            DefinitionDatabaseSync.EnsureFolderExists(StatFolder);

            CreateStat("Health", StatIds.Health,
                "Life force. Reaches zero, you die. Regenerates only while well fed and warm.",
                StatCategory.Vital, StatKind.Resource,
                baseValue: 100f, minValue: 0f, maxValue: 200f,
                regenPerSecond: 0f, regenDelaySeconds: 5f,
                showOnHud: true, barColor: new Color(0.80f, 0.22f, 0.20f));

            CreateStat("Stamina", StatIds.Stamina,
                "Spent by sprinting and fast swimming. Recovers after a short pause; recovers slowly while starving or dehydrated.",
                StatCategory.Vital, StatKind.Resource,
                baseValue: 100f, minValue: 0f, maxValue: 200f,
                regenPerSecond: 20f, regenDelaySeconds: 0.75f,
                showOnHud: true, barColor: new Color(0.35f, 0.72f, 0.28f));

            CreateStat("Hunger", StatIds.Hunger,
                "Drains slowly over time; restored by eating. Full (with thirst) grants health regen; empty cripples stamina recovery.",
                StatCategory.Survival, StatKind.Resource,
                baseValue: 100f, minValue: 0f, maxValue: 100f,
                regenPerSecond: -2.5f / 60f, regenDelaySeconds: 0f,
                showOnHud: true, barColor: new Color(0.87f, 0.58f, 0.18f));

            CreateStat("Thirst", StatIds.Thirst,
                "Drains a little faster than hunger; restored by drinks and juicy food. Same bonuses/penalties as hunger.",
                StatCategory.Survival, StatKind.Resource,
                baseValue: 100f, minValue: 0f, maxValue: 100f,
                regenPerSecond: -3.5f / 60f, regenDelaySeconds: 0f,
                showOnHud: true, barColor: new Color(0.25f, 0.55f, 0.85f));

            CreateStat("Warmth", StatIds.Warmth,
                "Recovers by day, drains at night, restored near a lit campfire. Low warmth stops health regen; at zero you freeze and lose health.",
                StatCategory.Survival, StatKind.Resource,
                baseValue: 100f, minValue: 0f, maxValue: 100f,
                regenPerSecond: 0.5f, regenDelaySeconds: 0f,
                showOnHud: true, barColor: new Color(0.92f, 0.75f, 0.30f));

            CreateStat("Mining Speed", StatIds.MiningSpeed,
                "Multiplier on terrain mining progress, on top of tool efficiency. Base 1.0; raised by tool equip modifiers and future buffs.",
                StatCategory.Utility, StatKind.Attribute,
                baseValue: 1f, minValue: 0f, maxValue: 10f,
                regenPerSecond: 0f, regenDelaySeconds: 0f,
                showOnHud: false, barColor: new Color(0.55f, 0.55f, 0.58f));

            CreateStat("Carry Capacity", StatIds.CarryCapacity,
                "Kilograms carried at full load (CarryWeight01 = 1). Backs the inventory's weight threshold; buffed by future gear.",
                StatCategory.Utility, StatKind.Attribute,
                baseValue: 60f, minValue: 1f, maxValue: 500f,
                regenPerSecond: 0f, regenDelaySeconds: 0f,
                showOnHud: false, barColor: new Color(0.55f, 0.45f, 0.35f));

            // Creature-centric stats (creatures phase). Shared DEFINITIONS —
            // species author their own base values on CreatureDefinition; the
            // player's container deliberately excludes these (see
            // StatsSystemBuilder's explicit player stat set).
            CreateStat("Move Speed", StatIds.MoveSpeed,
                "Ground speed in m/s. Creatures wander at a fraction of it and flee/chase at full value.",
                StatCategory.Utility, StatKind.Attribute,
                baseValue: 3.5f, minValue: 0f, maxValue: 30f,
                regenPerSecond: 0f, regenDelaySeconds: 0f,
                showOnHud: false, barColor: new Color(0.45f, 0.65f, 0.75f));

            CreateStat("Attack Damage", StatIds.AttackDamage,
                "Damage per attack. Authored per creature species now; the combat phase reads it when attacks land.",
                StatCategory.Combat, StatKind.Attribute,
                baseValue: 5f, minValue: 0f, maxValue: 500f,
                regenPerSecond: 0f, regenDelaySeconds: 0f,
                showOnHud: false, barColor: new Color(0.75f, 0.35f, 0.35f));

            CreateStat("Detection Radius", StatIds.DetectionRadius,
                "How far a creature notices the player, meters (optionally gated by line of sight).",
                StatCategory.Utility, StatKind.Attribute,
                baseValue: 12f, minValue: 0f, maxValue: 100f,
                regenPerSecond: 0f, regenDelaySeconds: 0f,
                showOnHud: false, barColor: new Color(0.6f, 0.6f, 0.4f));

            AssetDatabase.SaveAssets();
            DefinitionDatabaseSync.SyncAll();

            Debug.Log(
                "Player stat definitions ready: Health, Stamina, Hunger, Thirst, Warmth, Mining Speed, " +
                "Carry Capacity in Assets/_Game/Content/Stats. Existing assets were left untouched.");
            EditorGUIUtility.PingObject(AssetDatabase.LoadAssetAtPath<Object>(StatFolder));
        }

        private static void CreateStat(
            string assetName, string id, string description,
            StatCategory category, StatKind kind,
            float baseValue, float minValue, float maxValue,
            float regenPerSecond, float regenDelaySeconds,
            bool showOnHud, Color barColor)
        {
            var stat = ExampleContentCreator.CreateOrLoad<StatDefinition>(
                $"{StatFolder}/{assetName}.asset", out bool created);
            if (!created)
                return; // never overwrite hand-tuned values

            var serialized = new SerializedObject(stat);
            serialized.FindProperty("id").stringValue = id;
            serialized.FindProperty("displayName").stringValue = assetName;
            serialized.FindProperty("description").stringValue = description;
            serialized.FindProperty("category").intValue = (int)category;
            serialized.FindProperty("kind").intValue = (int)kind;
            serialized.FindProperty("baseValue").floatValue = baseValue;
            serialized.FindProperty("minValue").floatValue = minValue;
            serialized.FindProperty("maxValue").floatValue = maxValue;
            serialized.FindProperty("regenPerSecond").floatValue = regenPerSecond;
            serialized.FindProperty("regenDelaySeconds").floatValue = regenDelaySeconds;
            serialized.FindProperty("showOnHud").boolValue = showOnHud;
            serialized.FindProperty("barColor").colorValue = barColor;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
