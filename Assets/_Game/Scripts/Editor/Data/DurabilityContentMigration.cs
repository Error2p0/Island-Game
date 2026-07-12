using IslandGame.Data.Items;
using UnityEditor;
using UnityEngine;

namespace IslandGame.EditorTools.Data
{
    /// <summary>
    /// One-shot content migration for the durability phase, in the guarded
    /// style of the content-set migrations: every Tool/Weapon item whose Max
    /// Durability is still 0 (the pre-durability default) gets a tier-scaled
    /// default so the existing tool ladder wears meaningfully without
    /// re-authoring each asset by hand. GUARDED: items with any authored
    /// Max Durability are never touched, so hand tuning survives re-runs —
    /// and an item deliberately set to 0 AFTER this migration should simply
    /// not be re-migrated (don't re-run the menu blindly).
    ///
    /// Defaults: tools get 60 + 60 × tier uses (tier-0 crude tools die fast,
    /// higher tiers reward the crafting climb); pure weapons get 80 hits.
    /// Wear stays 1 point per use so "uses until broken" reads directly off
    /// Max Durability. Break behavior stays Destroy — Broken Variants are an
    /// authoring choice per item, not something a migration should invent.
    /// </summary>
    public static class DurabilityContentMigration
    {
        [MenuItem("Island Game/Data/Apply Default Durability To Tools And Weapons")]
        public static void Run()
        {
            int migrated = 0;

            foreach (string guid in AssetDatabase.FindAssets("t:" + nameof(ItemDefinition)))
            {
                var item = AssetDatabase.LoadAssetAtPath<ItemDefinition>(AssetDatabase.GUIDToAssetPath(guid));
                if (item == null || (!item.IsTool && !item.IsWeapon) || item.MaxDurability > 0f)
                    continue;

                float defaultDurability = item.IsTool
                    ? 60f + 60f * item.ToolTier
                    : 80f;

                var serialized = new SerializedObject(item);
                serialized.FindProperty("maxDurability").floatValue = defaultDurability;
                serialized.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(item);
                migrated++;

                Debug.Log($"[Durability] '{item.DisplayName}' → Max Durability {defaultDurability:0}.", item);
            }

            if (migrated > 0)
                AssetDatabase.SaveAssets();

            Debug.Log(migrated > 0
                ? $"[Durability] Migrated {migrated} tool/weapon item(s) to default durability. Hand-authored values were left untouched."
                : "[Durability] Nothing to migrate — every tool/weapon already has authored durability.");
        }
    }
}
