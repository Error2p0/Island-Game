using System.Collections.Generic;
using System.Linq;
using IslandGame.Data.Blocks;
using IslandGame.Data.Crafting;
using IslandGame.Data.Items;
using IslandGame.Data.World;
using IslandGame.Texturing;
using UnityEditor;
using UnityEngine;
using static IslandGame.EditorTools.Data.BaseContentSetGenerator;

namespace IslandGame.EditorTools.Data
{
    /// <summary>
    /// The foliage-arc Phase 2 batch pass (Island Game → Data → Create Tree
    /// Variant Content): three new tree varieties through the EXISTING
    /// TreeTemplate/sub-voxel pipeline (authoring and meshing untouched —
    /// this is pure content plus the additive altitude-band fields), and the
    /// crafting ties that make everything the foliage system produces a real
    /// resource:
    ///
    ///   BIRCH  — chalk-white bark, compact high canopy; mid-altitude band.
    ///            Feeds the plank_birch chain the content set already ships.
    ///   WILLOW — squat trunk, drooping leaf curtains; coastal band (max 3
    ///            above sea), so it reads as a waterside tree.
    ///   DEAD   — bare grey snag, no leaves; high ground only (min 8 above
    ///            sea) — harsh-terrain dressing that still drops dry logs.
    ///
    ///   ROPE   — 3 plant fiber, hand-crafted. Chosen over a bandage: rope
    ///            slots straight into what's coming (taming leashes next
    ///            arc phase, boat/building rigging) and into what exists
    ///            (fiber already gates bed/boat/bow), while healing-over-time
    ///            would need a new consume→modifier hook — a bigger change
    ///            than a crafting phase should smuggle in.
    ///
    ///   Roasted Berries (campfire) and the log→plank recipes ALREADY exist
    ///   from the Phase 9 content set — this pass ensure-creates them (no-op
    ///   when present, so a project that skipped the content set still gets
    ///   the campfire-gated berry cook) and adds plank_willow to the family.
    ///
    /// All log blocks drop the GENERIC log item and planks craft per-species
    /// from it — the established wood-family convention. Axes get the new
    /// blocks appended to their efficiency lists (guarded migration, same
    /// style as the content set's ApplyMigrations).
    ///
    /// Idempotent: definitions create-if-missing, textures regenerate every
    /// run (the Phase 8/9 procedural-pipeline contract).
    /// </summary>
    public static class TreeVariantContentCreator
    {
        private const string TreeFolder = "Assets/_Game/Content/Trees";

        [MenuItem("Island Game/Data/Create Tree Variant Content")]
        public static void Create()
        {
            DefinitionDatabaseSync.EnsureFolderExists(TreeFolder);
            DefinitionDatabaseSync.EnsureFolderExists(BlockFolder);
            DefinitionDatabaseSync.EnsureFolderExists(ItemFolder);
            DefinitionDatabaseSync.EnsureFolderExists(RecipeFolder);
            DefinitionDatabaseSync.EnsureFolderExists(GeneratedTextureAssets.BlockTextureFolder);
            DefinitionDatabaseSync.EnsureFolderExists(GeneratedTextureAssets.IconFolder);

            var needsSupportWood = BlockBehaviorFlags.Flammable | BlockBehaviorFlags.NeedsSupport;

            // --- Palette (kept together so the family reads as one) ---------
            Color birchBark = new Color(0.85f, 0.83f, 0.78f);
            Color birchLeaf = new Color(0.52f, 0.72f, 0.32f);
            Color willowBark = new Color(0.44f, 0.41f, 0.35f);
            Color willowLeaf = new Color(0.48f, 0.60f, 0.38f);
            Color deadWood = new Color(0.36f, 0.32f, 0.28f);
            Color ropeTan = new Color(0.76f, 0.66f, 0.42f);

            // --- Blocks (wood family: logs drop the generic log item) -------
            Block("birch_log", "Birch Log", TextureStyle.Wood, birchBark,
                hardness: 1.6f, tier: 0, dropId: "log", flags: needsSupportWood);
            Block("birch_leaves", "Birch Leaves", TextureStyle.Foliage, birchLeaf,
                hardness: 0.25f, tier: 0, dropId: "plant_fiber", dropMin: 0, dropMax: 1,
                transparent: true, flags: needsSupportWood);

            Block("willow_log", "Willow Log", TextureStyle.Wood, willowBark,
                hardness: 1.6f, tier: 0, dropId: "log", flags: needsSupportWood);
            Block("willow_leaves", "Willow Leaves", TextureStyle.Foliage, willowLeaf,
                hardness: 0.25f, tier: 0, dropId: "plant_fiber", dropMin: 0, dropMax: 1,
                transparent: true, flags: needsSupportWood);

            // Dry snag wood: slightly softer than living logs, still a log drop.
            Block("dead_wood", "Dead Wood", TextureStyle.Wood, deadWood,
                hardness: 1.4f, tier: 0, dropId: "log", flags: needsSupportWood);

            Block("plank_willow", "Willow Planks", TextureStyle.Wood, Color.Lerp(willowBark, Color.white, 0.2f),
                hardness: 1.2f, tier: 0, dropId: "plank_willow", flags: BlockBehaviorFlags.Flammable);

            // --- Items -------------------------------------------------------
            PlacerItem("plank_willow", "Willow Planks", Color.Lerp(willowBark, Color.white, 0.2f));
            LinkPlacerItem("plank_willow");

            Resource("rope", "Rope", "Fiber twisted into sturdy cordage. Rigging, lashings — and leads for anything you manage to befriend.",
                IconShape.Circle, ropeTan, maxStack: 20, weightKg: 0.2f);

            // Ensure the cooked berry exists even on projects that skipped the
            // Phase 9 content set (same IDs/paths — no-op otherwise).
            ItemDefinition cookedBerries = Resource("cooked_berries", "Roasted Berries",
                "Campfire-roasted and twice as filling.",
                IconShape.Circle, new Color(0.55f, 0.12f, 0.2f), maxStack: 20, weightKg: 0.1f);
            MakeConsumable(cookedBerries, hungerRestore: 25f);

            // --- Tree templates ----------------------------------------------
            Template("birch", "Birch", "birch_log", "birch_leaves",
                spawnWeight: 0.8f, minAltitude: 2, maxAltitude: 9,
                strokes: new[]
                {
                    new TreeStroke(TreePart.Trunk, new Vector3(0f, 0f, 0f), new Vector3(0f, 4.4f, 0f), 0.17f),
                    new TreeStroke(TreePart.Branch, new Vector3(0f, 3.4f, 0f), new Vector3(0.7f, 4.1f, 0.4f), 0.10f),
                    new TreeStroke(TreePart.Branch, new Vector3(0f, 3.8f, 0f), new Vector3(-0.6f, 4.4f, -0.3f), 0.09f),
                    new TreeStroke(TreePart.Leaves, new Vector3(0f, 4.7f, 0f), new Vector3(0f, 4.7f, 0f), 1.1f),
                    new TreeStroke(TreePart.Leaves, new Vector3(0.6f, 4.2f, 0.35f), new Vector3(0.6f, 4.2f, 0.35f), 0.75f),
                    new TreeStroke(TreePart.Leaves, new Vector3(-0.5f, 4.4f, -0.3f), new Vector3(-0.5f, 4.4f, -0.3f), 0.7f),
                    new TreeStroke(TreePart.Leaves, new Vector3(0f, 5.4f, 0f), new Vector3(0f, 5.4f, 0f), 0.55f),
                });

            Template("willow", "Willow", "willow_log", "willow_leaves",
                spawnWeight: 1.2f, minAltitude: 0, maxAltitude: 3,
                strokes: new[]
                {
                    new TreeStroke(TreePart.Trunk, new Vector3(0f, 0f, 0f), new Vector3(0.2f, 2.9f, 0f), 0.30f),
                    new TreeStroke(TreePart.Branch, new Vector3(0.1f, 2.3f, 0f), new Vector3(1.3f, 3.1f, 0.5f), 0.14f),
                    new TreeStroke(TreePart.Branch, new Vector3(0.1f, 2.5f, 0f), new Vector3(-1.1f, 3.2f, -0.6f), 0.14f),
                    // Crown, then drooping curtains: leaf capsules falling from
                    // the crown rim toward the ground — the willow silhouette.
                    new TreeStroke(TreePart.Leaves, new Vector3(0.1f, 3.5f, 0f), new Vector3(0.1f, 3.5f, 0f), 1.5f),
                    new TreeStroke(TreePart.Leaves, new Vector3(1.5f, 3.2f, 0.4f), new Vector3(1.9f, 1.1f, 0.6f), 0.45f),
                    new TreeStroke(TreePart.Leaves, new Vector3(-1.4f, 3.3f, -0.5f), new Vector3(-1.8f, 1.2f, -0.7f), 0.45f),
                    new TreeStroke(TreePart.Leaves, new Vector3(0.5f, 3.4f, -1.4f), new Vector3(0.7f, 1.3f, -1.8f), 0.45f),
                    new TreeStroke(TreePart.Leaves, new Vector3(-0.4f, 3.3f, 1.4f), new Vector3(-0.6f, 1.2f, 1.8f), 0.45f),
                });

            Template("dead_tree", "Dead Tree", "dead_wood", "dead_wood",
                spawnWeight: 0.5f, minAltitude: 8, maxAltitude: 0, // 0 = open-ended: everything above 8
                strokes: new[]
                {
                    new TreeStroke(TreePart.Trunk, new Vector3(0f, 0f, 0f), new Vector3(0.1f, 3.5f, 0f), 0.20f),
                    new TreeStroke(TreePart.Branch, new Vector3(0.05f, 2.2f, 0f), new Vector3(0.9f, 3.1f, 0.3f), 0.10f),
                    new TreeStroke(TreePart.Branch, new Vector3(0.05f, 2.8f, 0f), new Vector3(-0.8f, 3.6f, -0.2f), 0.09f),
                    new TreeStroke(TreePart.Branch, new Vector3(0.1f, 3.3f, 0f), new Vector3(0.4f, 4.2f, -0.5f), 0.08f),
                    // No Leaves strokes on purpose — a bare snag.
                });

            // --- Recipes (the existing crafting web, extended) ---------------
            Recipe("rope_craft", "Rope", outputItemId: "rope", outputCount: 1,
                CraftingStationType.None, craftSeconds: 1f, ("plant_fiber", 3));

            Recipe("plank_willow_from_log", "Willow Planks", outputItemId: "plank_willow", outputCount: 4,
                CraftingStationType.None, craftSeconds: 0f, ("log", 1));

            // The campfire-gated berry cook (ensure-create — the content set
            // normally ships it; identical ID/path makes this a no-op then).
            Recipe("cooked_berries_craft", "Roasted Berries", outputItemId: "cooked_berries", outputCount: 1,
                CraftingStationType.Campfire, craftSeconds: 2f, ("berries", 2));

            // --- Axe efficiency migration (additive, guarded) ----------------
            AppendToAxeEfficiency("birch_log", "willow_log", "dead_wood", "plank_willow");

            AssetDatabase.SaveAssets();
            DefinitionDatabaseSync.SyncAll();

            Debug.Log(
                "Tree variant content ready: Birch (altitude 2-9), Willow (coastal, ≤3 above sea), Dead Tree " +
                "(≥8 above sea) templates with their log/leaf blocks; Willow Planks placer chain; Rope (3 fiber); " +
                "Roasted Berries ensured at the campfire; axes now efficient against the new wood. Trees appear " +
                "where new chunk data generates — restart play mode (or move to fresh terrain) to see them. " +
                "Existing assets were left untouched.");
        }

        // ==================================================================
        // Helpers (create-if-missing, same shape as the content set's own)
        // ==================================================================

        private static void Block(
            string id, string displayName, TextureStyle style, Color color,
            float hardness, int tier, string dropId = null, int dropMin = 1, int dropMax = 1,
            bool transparent = false, BlockBehaviorFlags flags = BlockBehaviorFlags.None)
        {
            string path = $"{BlockFolder}/{ContentSetItemsAndBlocks.ToAssetName(id)}Block.asset";
            BlockDefinition block = ExampleContentCreator.CreateOrLoad<BlockDefinition>(path, out bool created);

            // Texture regenerates every run (procedural pipeline contract);
            // definition fields only on creation.
            Texture2D texture = BlockTexture(id, style, color);
            if (!created)
                return;

            var serialized = new SerializedObject(block);
            serialized.FindProperty("id").stringValue = id;
            serialized.FindProperty("displayName").stringValue = displayName;
            serialized.FindProperty("useUniformTexture").boolValue = true;
            serialized.FindProperty("uniformTexture").objectReferenceValue = texture;
            serialized.FindProperty("isSolid").boolValue = true;
            serialized.FindProperty("isTransparent").boolValue = transparent;
            serialized.FindProperty("hardness").floatValue = hardness;
            serialized.FindProperty("requiredToolTier").intValue = tier;
            serialized.FindProperty("dropItem").objectReferenceValue = dropId != null ? FindItem(dropId) : null;
            serialized.FindProperty("dropCountMin").intValue = dropId != null ? dropMin : 0;
            serialized.FindProperty("dropCountMax").intValue = dropId != null ? dropMax : 0;
            serialized.FindProperty("behaviorFlags").intValue = (int)flags;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private static ItemDefinition Resource(
            string id, string displayName, string description,
            IconShape shape, Color color, int maxStack, float weightKg)
        {
            string path = $"{ItemFolder}/{ContentSetItemsAndBlocks.ToAssetName(id)}.asset";
            ItemDefinition item = ExampleContentCreator.CreateOrLoad<ItemDefinition>(path, out bool created);
            if (!created)
                return item;

            Sprite icon = Icon(id, shape, color, Color.Lerp(color, Color.black, 0.35f));
            ExampleContentCreator.SetItemFields(item, id, displayName, description,
                icon, ItemCategory.Resource, maxStack, weightKg);
            return item;
        }

        private static void PlacerItem(string id, string displayName, Color color)
        {
            string path = $"{ItemFolder}/{ContentSetItemsAndBlocks.ToAssetName(id)}.asset";
            ItemDefinition item = ExampleContentCreator.CreateOrLoad<ItemDefinition>(path, out bool created);
            if (!created)
                return;

            Sprite icon = Icon(id, IconShape.RoundedSquare, color, Color.Lerp(color, Color.black, 0.35f));
            ExampleContentCreator.SetItemFields(item, id, displayName,
                $"Placeable building material: {displayName.ToLowerInvariant()}.",
                icon, ItemCategory.Block, 100, 0.5f);
        }

        private static void LinkPlacerItem(string id)
        {
            ItemDefinition item = FindItem(id);
            BlockDefinition block = FindBlock(id);
            if (item == null || block == null || item.PlacedBlock != null)
                return;

            ExampleContentCreator.SetObjectReference(item, "placedBlock", block);
        }

        private static void MakeConsumable(ItemDefinition item, float hungerRestore)
        {
            if (item == null)
                return;

            var serialized = new SerializedObject(item);
            if ((ItemCategory)serialized.FindProperty("category").intValue == ItemCategory.Consumable
                && serialized.FindProperty("hungerRestore").floatValue > 0f)
                return; // already configured (pre-existing asset)

            serialized.FindProperty("category").intValue = (int)ItemCategory.Consumable;
            if (serialized.FindProperty("hungerRestore").floatValue <= 0f)
                serialized.FindProperty("hungerRestore").floatValue = hungerRestore;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void Template(
            string id, string displayName, string trunkBlockId, string leavesBlockId,
            float spawnWeight, int minAltitude, int maxAltitude, TreeStroke[] strokes)
        {
            BlockDefinition trunk = FindBlock(trunkBlockId);
            BlockDefinition leaves = FindBlock(leavesBlockId);
            if (trunk == null)
            {
                Debug.LogWarning($"[TreeVariants] Template '{id}' skipped — trunk block '{trunkBlockId}' missing.");
                return;
            }

            // OakTree.asset-style names; ids already ending in "tree"
            // (dead_tree → DeadTree) don't get a second suffix.
            string assetName = ContentSetItemsAndBlocks.ToAssetName(id);
            if (!assetName.EndsWith("Tree"))
                assetName += "Tree";
            string path = $"{TreeFolder}/{assetName}.asset";
            TreeTemplateDefinition template = ExampleContentCreator.CreateOrLoad<TreeTemplateDefinition>(path, out bool created);
            if (!created)
                return; // never overwrite hand-tuned trees

            var serialized = new SerializedObject(template);
            serialized.FindProperty("id").stringValue = id;
            serialized.FindProperty("displayName").stringValue = displayName;
            serialized.FindProperty("trunkBlock").objectReferenceValue = trunk;
            serialized.FindProperty("leavesBlock").objectReferenceValue = leaves;
            serialized.FindProperty("resolution").intValue = 4;
            serialized.FindProperty("spawnWeight").floatValue = spawnWeight;
            serialized.FindProperty("minAltitudeAboveSea").intValue = minAltitude;
            serialized.FindProperty("maxAltitudeAboveSea").intValue = maxAltitude;

            SerializedProperty list = serialized.FindProperty("strokes");
            list.arraySize = strokes.Length;
            for (int i = 0; i < strokes.Length; i++)
            {
                SerializedProperty element = list.GetArrayElementAtIndex(i);
                element.FindPropertyRelative("part").intValue = (int)strokes[i].Part;
                element.FindPropertyRelative("start").vector3Value = strokes[i].Start;
                element.FindPropertyRelative("end").vector3Value = strokes[i].End;
                element.FindPropertyRelative("radius").floatValue = strokes[i].Radius;
            }

            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void Recipe(
            string recipeId, string displayName, string outputItemId, int outputCount,
            CraftingStationType station, float craftSeconds, params (string id, int count)[] ingredients)
        {
            ItemDefinition output = FindItem(outputItemId);
            if (output == null)
            {
                Debug.LogWarning($"[TreeVariants] Recipe '{recipeId}' skipped — output item '{outputItemId}' missing.");
                return;
            }

            string path = $"{RecipeFolder}/{ContentSetItemsAndBlocks.ToAssetName(recipeId)}.asset";
            RecipeDefinition recipe = ExampleContentCreator.CreateOrLoad<RecipeDefinition>(path, out bool created);
            if (!created)
                return;

            var serialized = new SerializedObject(recipe);
            serialized.FindProperty("id").stringValue = recipeId;
            serialized.FindProperty("displayName").stringValue = displayName;
            serialized.FindProperty("output").objectReferenceValue = output;
            serialized.FindProperty("outputPiece").objectReferenceValue = null;
            serialized.FindProperty("outputCount").intValue = outputCount;
            serialized.FindProperty("station").intValue = (int)station;
            serialized.FindProperty("craftSeconds").floatValue = craftSeconds;

            SerializedProperty list = serialized.FindProperty("ingredients");
            list.arraySize = ingredients.Length;
            for (int i = 0; i < ingredients.Length; i++)
            {
                ItemDefinition ingredient = FindItem(ingredients[i].id);
                if (ingredient == null)
                    Debug.LogWarning($"[TreeVariants] Recipe '{recipeId}': ingredient '{ingredients[i].id}' missing — the recipe will flag as dangling.");

                SerializedProperty element = list.GetArrayElementAtIndex(i);
                element.FindPropertyRelative("item").objectReferenceValue = ingredient;
                element.FindPropertyRelative("count").intValue = ingredients[i].count;
            }

            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        /// <summary>
        /// Appends the given blocks to every axe's efficiency list when absent
        /// (guarded migration — never removes or reorders, so hand-tuned lists
        /// only ever gain the new wood). Permission is the tier check; this is
        /// only the speed bonus, but "chops exactly like existing trees" means
        /// axes should be fast against the new logs too.
        /// </summary>
        private static void AppendToAxeEfficiency(params string[] blockIds)
        {
            var blocks = new List<BlockDefinition>();
            foreach (string blockId in blockIds)
            {
                BlockDefinition block = FindBlock(blockId);
                if (block != null)
                    blocks.Add(block);
            }

            if (blocks.Count == 0)
                return;

            var axes = AssetDatabase.FindAssets("t:ItemDefinition")
                .Select(AssetDatabase.GUIDToAssetPath)
                .Select(AssetDatabase.LoadAssetAtPath<ItemDefinition>)
                .Where(item => item != null && item.IsTool && item.ToolType == ToolType.Axe);

            foreach (ItemDefinition axe in axes)
            {
                var missing = blocks.Where(block => !axe.IsEfficientAgainst(block)).ToList();
                if (missing.Count == 0)
                    continue;

                var serialized = new SerializedObject(axe);
                SerializedProperty list = serialized.FindProperty("efficientBlocks");
                int baseIndex = list.arraySize;
                list.arraySize += missing.Count;
                for (int i = 0; i < missing.Count; i++)
                    list.GetArrayElementAtIndex(baseIndex + i).objectReferenceValue = missing[i];

                serialized.ApplyModifiedPropertiesWithoutUndo();
                Debug.Log($"[TreeVariants] Migration: '{axe.Id}' now efficient against {missing.Count} new wood block(s).");
            }
        }
    }
}
