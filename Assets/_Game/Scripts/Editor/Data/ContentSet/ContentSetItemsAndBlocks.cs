using System.Collections.Generic;
using IslandGame.Data.Blocks;
using IslandGame.Data.Items;
using IslandGame.Texturing;
using UnityEditor;
using UnityEngine;
using static IslandGame.EditorTools.Data.BaseContentSetGenerator;

namespace IslandGame.EditorTools.Data
{
    /// <summary>
    /// Blocks and items of the base content set. Tier ladder (matches the
    /// tool permissions below, requirement: wood pick can't mine ore, stone
    /// pick can):
    ///   tier 0  bare hands — dirt/sand/grass/clay/snow/logs/leaves/planks
    ///   tier 1  wood tools — stone, cobblestone, cut stone, brick, ice
    ///   tier 2  stone tools — copper + tin ore
    ///   tier 3  copper (metal) tools — silver ore
    /// Every visual comes from the Phase 8 generator (per-ID seeds; ores are
    /// stone + a nugget overlay pass).
    /// </summary>
    internal static class ContentSetItemsAndBlocks
    {
        // --------------------------------------------------------------
        // 1. Resource / stackable items (exist before blocks link drops)
        // --------------------------------------------------------------

        public static void CreateResourceItems()
        {
            Resource("flint", "Flint", "Knapped cutting stone — spearheads and arrowheads.",
                IconShape.Circle, new Color(0.35f, 0.35f, 0.38f), 30, 0.3f);
            Resource("plant_fiber", "Plant Fiber", "Tough strands from pine needles and brush. Cordage for tools.",
                IconShape.Circle, new Color(0.45f, 0.62f, 0.3f), 50, 0.05f);
            Resource("clay_lump", "Clay", "Wet moldable clay from the shoreline.",
                IconShape.Circle, new Color(0.66f, 0.55f, 0.47f), 30, 0.5f);

            Resource("raw_copper", "Raw Copper", "Copper-bearing ore. Smelt it at a furnace.",
                IconShape.Circle, Preset(TextureStyle.Metal, 1), 30, 1.2f);
            Resource("raw_tin", "Raw Tin", "Tin-bearing ore. Smelt it at a furnace.",
                IconShape.Circle, new Color(0.72f, 0.75f, 0.78f), 30, 1.2f);
            Resource("raw_silver", "Raw Silver", "Precious ore glinting in the rock. Smelt it at a furnace.",
                IconShape.Circle, new Color(0.85f, 0.88f, 0.95f), 30, 1.4f);

            Resource("copper_bar", "Copper Bar", "Smelted copper — the first metal tier.",
                IconShape.Ingot, Preset(TextureStyle.Metal, 1), 20, 1.5f);
            Resource("tin_bar", "Tin Bar", "Smelted tin. Soft alone; alloys later.",
                IconShape.Ingot, new Color(0.72f, 0.75f, 0.78f), 20, 1.5f);
            Resource("silver_bar", "Silver Bar", "Smelted silver. Valuable; future smithing tier.",
                IconShape.Ingot, new Color(0.85f, 0.88f, 0.95f), 20, 1.6f);

            // Berries exist before oak_leaves links them as its drop.
            ItemDefinition berries = Resource("berries", "Berries", "Foraged from oak canopies. Eat with the use button.",
                IconShape.Circle, new Color(0.75f, 0.18f, 0.28f), 20, 0.1f);
            MakeConsumable(berries, 12f);

            // Placer items for the processed blocks (PlacedBlock linked after
            // the blocks exist — see LinkPlacerItems).
            PlacerItem("plank_oak", "Oak Planks", Color.Lerp(Preset(TextureStyle.Wood, 0), Color.white, 0.12f));
            PlacerItem("plank_birch", "Birch Planks", Preset(TextureStyle.Wood, 1));
            PlacerItem("plank_pine", "Pine Planks", Preset(TextureStyle.Wood, 2));
            PlacerItem("cut_stone", "Cut Stone", Preset(TextureStyle.Stone, 3), weightKg: 1.4f);
            PlacerItem("stone_brick", "Stone Brick", new Color(0.5f, 0.5f, 0.53f), weightKg: 1.5f);
            PlacerItem("cobblestone", "Cobblestone", Preset(TextureStyle.Stone, 1), weightKg: 1.4f);
        }

        // --------------------------------------------------------------
        // 2. Blocks
        // --------------------------------------------------------------

        public static void CreateBlocks()
        {
            var needsSupportWood = BlockBehaviorFlags.Flammable | BlockBehaviorFlags.NeedsSupport;

            // Ores: stone base + nugget overlay, gated by the tier ladder.
            Block("copper_ore", "Copper Ore", TextureStyle.Stone, Preset(TextureStyle.Stone, 0),
                hardness: 4f, tier: 2, dropId: "raw_copper", dropMin: 1, dropMax: 2,
                post: NuggetOverlay(Preset(TextureStyle.Metal, 1)));
            Block("tin_ore", "Tin Ore", TextureStyle.Stone, Preset(TextureStyle.Stone, 0),
                hardness: 4f, tier: 2, dropId: "raw_tin", dropMin: 1, dropMax: 2,
                post: NuggetOverlay(new Color(0.72f, 0.75f, 0.78f)));
            Block("silver_ore", "Silver Ore", TextureStyle.Stone, Preset(TextureStyle.Stone, 1),
                hardness: 5f, tier: 3, dropId: "raw_silver", dropMin: 1, dropMax: 2,
                post: NuggetOverlay(new Color(0.85f, 0.88f, 0.95f)));

            Block("clay", "Clay", TextureStyle.Sand, new Color(0.66f, 0.55f, 0.47f),
                hardness: 0.8f, tier: 0, dropId: "clay_lump", dropMin: 1, dropMax: 2);
            Block("snow", "Snow", TextureStyle.Sand, new Color(0.92f, 0.94f, 0.97f),
                hardness: 0.3f, tier: 0);
            Block("ice", "Ice", TextureStyle.Liquid, new Color(0.65f, 0.8f, 0.95f),
                hardness: 0.6f, tier: 1, transparent: true);

            // Tree woods (the templates migrate onto these — see generator).
            Block("oak_log", "Oak Log", TextureStyle.Wood, Preset(TextureStyle.Wood, 0),
                hardness: 1.6f, tier: 0, dropId: "log", flags: needsSupportWood);
            Block("pine_log", "Pine Log", TextureStyle.Wood, Preset(TextureStyle.Wood, 2),
                hardness: 1.6f, tier: 0, dropId: "log", flags: needsSupportWood);
            Block("oak_leaves", "Oak Leaves", TextureStyle.Foliage, Preset(TextureStyle.Foliage, 0),
                hardness: 0.25f, tier: 0, dropId: "berries", dropMin: 0, dropMax: 1,
                transparent: true, flags: needsSupportWood);
            Block("pine_leaves", "Pine Leaves", TextureStyle.Foliage, Preset(TextureStyle.Foliage, 2),
                hardness: 0.25f, tier: 0, dropId: "plant_fiber", dropMin: 0, dropMax: 1,
                transparent: true, flags: needsSupportWood);

            // Processed building blocks — each drops its placer item back.
            Block("plank_oak", "Oak Planks", TextureStyle.Wood, Color.Lerp(Preset(TextureStyle.Wood, 0), Color.white, 0.12f),
                hardness: 1.2f, tier: 0, dropId: "plank_oak", flags: BlockBehaviorFlags.Flammable);
            Block("plank_birch", "Birch Planks", TextureStyle.Wood, Preset(TextureStyle.Wood, 1),
                hardness: 1.2f, tier: 0, dropId: "plank_birch", flags: BlockBehaviorFlags.Flammable);
            Block("plank_pine", "Pine Planks", TextureStyle.Wood, Preset(TextureStyle.Wood, 2),
                hardness: 1.2f, tier: 0, dropId: "plank_pine", flags: BlockBehaviorFlags.Flammable);

            Block("cut_stone", "Cut Stone", TextureStyle.Stone, Preset(TextureStyle.Stone, 3),
                hardness: 2.5f, tier: 1, dropId: "cut_stone");
            Block("stone_brick", "Stone Brick", TextureStyle.Fabric, new Color(0.5f, 0.5f, 0.53f),
                hardness: 3f, tier: 1, dropId: "stone_brick");
            Block("cobblestone", "Cobblestone", TextureStyle.Stone, Preset(TextureStyle.Stone, 1),
                hardness: 2f, tier: 1, dropId: "cobblestone");
        }

        // --------------------------------------------------------------
        // 3. Placer-item ↔ block cross-links (fill-if-null, rerun-safe)
        // --------------------------------------------------------------

        public static void CreateBlockPlacerItems()
        {
            LinkPlacerItem("plank_oak");
            LinkPlacerItem("plank_birch");
            LinkPlacerItem("plank_pine");
            LinkPlacerItem("cut_stone");
            LinkPlacerItem("stone_brick");
            LinkPlacerItem("cobblestone");
        }

        // --------------------------------------------------------------
        // 4. Tools & weapons
        // --------------------------------------------------------------

        public static void CreateToolsAndWeapons()
        {
            // Efficiency lists per tool type (permission is the tier).
            string[] axeBlocks = { "oak_log", "pine_log", "wood", "wood_plank", "plank_oak", "plank_birch", "plank_pine" };
            string[] pickBlocks = { "stone", "cobblestone", "cut_stone", "stone_brick", "copper_ore", "tin_ore", "silver_ore", "ice" };
            string[] shovelBlocks = { "dirt", "sand", "grass", "clay", "snow" };
            string[] hoeBlocks = { "grass", "dirt" };

            Color wood = Preset(TextureStyle.Wood, 0);
            Color stone = Preset(TextureStyle.Stone, 0);
            Color copper = Preset(TextureStyle.Metal, 1);

            ToolSet("wooden", "Wooden", wood, wood, tier: 1, speed: 2.5f, radius: 0.45f, weightKg: 2f,
                axeBlocks, pickBlocks, shovelBlocks, hoeBlocks);
            ToolSet("stone", "Stone", stone, wood, tier: 2, speed: 4f, radius: 0.55f, weightKg: 3f,
                axeBlocks, pickBlocks, shovelBlocks, hoeBlocks, skipPickaxe: true); // stone_pickaxe already exists (migrated to tier 2)
            ToolSet("copper", "Copper", copper, wood, tier: 3, speed: 6f, radius: 0.65f, weightKg: 3.5f,
                axeBlocks, pickBlocks, shovelBlocks, hoeBlocks);

            // Melee weapons.
            ItemDefinition spear = Weapon("wooden_spear", "Wooden Spear", "A fire-hardened point on a long haft. Keeps trouble at range.",
                IconShape.Sword, new Color(0.6f, 0.6f, 0.62f), wood, HoldType.TwoHanded,
                damage: 12f, type: DamageType.Pierce, attacksPerSecond: 1.1f, range: 2.8f, weightKg: 2.5f);
            _ = spear;

            Weapon("wooden_club", "Wooden Club", "A heavy knot of hardwood. Simple arguments.",
                IconShape.Sword, wood, wood, HoldType.OneHanded,
                damage: 9f, type: DamageType.Blunt, attacksPerSecond: 1.3f, range: 2f, weightKg: 2f);

            Weapon("copper_sword", "Copper Sword", "The first forged blade.",
                IconShape.Sword, copper, wood, HoldType.OneHanded,
                damage: 18f, type: DamageType.Slash, attacksPerSecond: 1.4f, range: 2.2f, weightKg: 2.5f);

            // Ammo before the bow that consumes it.
            Resource("arrow", "Arrow", "Fletched shafts tipped with flint.",
                IconShape.Sword, new Color(0.5f, 0.5f, 0.52f), 30, 0.05f);

            ItemDefinition bow = Weapon("bow", "Bow", "Plank belly, fiber string. Consumes arrows.",
                IconShape.Sword, wood, new Color(0.45f, 0.62f, 0.3f), HoldType.TwoHanded,
                damage: 15f, type: DamageType.Pierce, attacksPerSecond: 0.9f, range: 40f, weightKg: 1.5f);
            if (bow != null)
            {
                var serialized = new SerializedObject(bow);
                if (!serialized.FindProperty("isRangedWeapon").boolValue)
                {
                    serialized.FindProperty("isRangedWeapon").boolValue = true;
                    serialized.FindProperty("projectileSpeed").floatValue = 32f;
                    serialized.FindProperty("ammoItem").objectReferenceValue = FindItem("arrow");
                    serialized.ApplyModifiedPropertiesWithoutUndo();
                }
            }
        }

        // --------------------------------------------------------------
        // 5. Food & misc (cooked food, torch with real light, bucket)
        // --------------------------------------------------------------

        public static void CreateFoodAndMisc()
        {
            ItemDefinition cooked = Resource("cooked_berries", "Roasted Berries", "Campfire-roasted and twice as filling.",
                IconShape.Circle, new Color(0.55f, 0.12f, 0.2f), 20, 0.1f);
            MakeConsumable(cooked, 25f);

            // Torch: one-handed held light — the world model carries a real
            // point light, which the hold controller keeps (it only strips
            // colliders/rigidbodies).
            ItemDefinition torch = Resource("torch", "Torch", "Fiber-wrapped brand. Carry fire into the night.",
                IconShape.Droplet, new Color(1f, 0.62f, 0.2f), 10, 0.4f);
            if (torch != null && torch.WorldModelPrefab == null)
            {
                GameObject prefab = CreateTorchPrefab($"{PrefabFolder}/TorchHand.prefab");
                var serialized = new SerializedObject(torch);
                serialized.FindProperty("worldModelPrefab").objectReferenceValue = prefab;
                serialized.FindProperty("holdType").intValue = (int)HoldType.OneHanded;
                serialized.FindProperty("holdSocket").intValue = (int)HoldSocket.RightHand;
                serialized.ApplyModifiedPropertiesWithoutUndo();
            }

            Resource("bucket", "Bucket", "A riveted copper pail. Liquids someday; storage today.",
                IconShape.Droplet, Preset(TextureStyle.Metal, 1), 5, 1.2f);
        }

        // ==================================================================
        // Helpers
        // ==================================================================

        private static ItemDefinition Resource(
            string id, string displayName, string description,
            IconShape shape, Color color, int maxStack, float weightKg)
        {
            string path = $"{ItemFolder}/{ToAssetName(id)}.asset";
            ItemDefinition item = ExampleContentCreator.CreateOrLoad<ItemDefinition>(path, out bool created);
            if (!created)
                return item;

            Sprite icon = Icon(id, shape, color, Color.Lerp(color, Color.black, 0.35f));
            ExampleContentCreator.SetItemFields(item, id, displayName, description,
                icon, ItemCategory.Resource, maxStack, weightKg);
            ItemsCreated++;
            return item;
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

        private static void PlacerItem(string id, string displayName, Color color, float weightKg = 0.5f)
        {
            string path = $"{ItemFolder}/{ToAssetName(id)}.asset";
            ItemDefinition item = ExampleContentCreator.CreateOrLoad<ItemDefinition>(path, out bool created);
            if (!created)
                return;

            Sprite icon = Icon(id, IconShape.RoundedSquare, color, Color.Lerp(color, Color.black, 0.35f));
            ExampleContentCreator.SetItemFields(item, id, displayName, $"Placeable building material: {displayName.ToLowerInvariant()}.",
                icon, ItemCategory.Block, 100, weightKg);
            ItemsCreated++;
        }

        private static void LinkPlacerItem(string id)
        {
            ItemDefinition item = FindItem(id);
            BlockDefinition block = FindBlock(id);
            if (item == null || block == null || item.PlacedBlock != null)
                return;

            ExampleContentCreator.SetObjectReference(item, "placedBlock", block);
        }

        private static void Block(
            string id, string displayName, TextureStyle style, Color color,
            float hardness, int tier, string dropId = null, int dropMin = 1, int dropMax = 1,
            bool transparent = false, BlockBehaviorFlags flags = BlockBehaviorFlags.None,
            System.Action<Color32[], int, int> post = null)
        {
            string path = $"{BlockFolder}/{ToAssetName(id)}Block.asset";
            BlockDefinition block = ExampleContentCreator.CreateOrLoad<BlockDefinition>(path, out bool created);

            // Texture regenerates every run (procedural pipeline contract);
            // definition fields only on creation.
            Texture2D texture = BlockTexture(id, style, color, 0f, post);
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
            BlocksCreated++;
        }

        private static void ToolSet(
            string idPrefix, string namePrefix, Color headColor, Color handleColor,
            int tier, float speed, float radius, float weightKg,
            string[] axeBlocks, string[] pickBlocks, string[] shovelBlocks, string[] hoeBlocks,
            bool skipPickaxe = false)
        {
            Tool($"{idPrefix}_axe", $"{namePrefix} Axe", ToolType.Axe, IconShape.Axe,
                headColor, handleColor, tier, speed, radius, weightKg, axeBlocks);
            if (!skipPickaxe)
            {
                Tool($"{idPrefix}_pickaxe", $"{namePrefix} Pickaxe", ToolType.Pickaxe, IconShape.Pickaxe,
                    headColor, handleColor, tier, speed, radius, weightKg, pickBlocks);
            }

            Tool($"{idPrefix}_shovel", $"{namePrefix} Shovel", ToolType.Shovel, IconShape.Pickaxe,
                headColor, handleColor, tier, speed, radius, weightKg, shovelBlocks);
            Tool($"{idPrefix}_hoe", $"{namePrefix} Hoe", ToolType.Hoe, IconShape.Axe,
                headColor, handleColor, tier, speed, radius, weightKg, hoeBlocks);
        }

        private static void Tool(
            string id, string displayName, ToolType toolType, IconShape shape,
            Color headColor, Color handleColor, int tier, float speed, float radius, float weightKg,
            string[] efficientBlockIds)
        {
            string path = $"{ItemFolder}/{ToAssetName(id)}.asset";
            ItemDefinition item = ExampleContentCreator.CreateOrLoad<ItemDefinition>(path, out bool created);
            if (!created)
                return;

            Sprite icon = Icon(id, shape, headColor, handleColor);
            ExampleContentCreator.SetItemFields(item, id, displayName,
                $"Tier {tier} {toolType.ToString().ToLowerInvariant()}.",
                icon, ItemCategory.Tool, 1, weightKg);

            var serialized = new SerializedObject(item);
            serialized.FindProperty("isTool").boolValue = true;
            serialized.FindProperty("toolType").intValue = (int)toolType;
            serialized.FindProperty("toolTier").intValue = tier;
            serialized.FindProperty("miningSpeedMultiplier").floatValue = speed;
            serialized.FindProperty("miningRadius").floatValue = radius;
            serialized.FindProperty("holdType").intValue = (int)HoldType.OneHanded;
            serialized.FindProperty("holdSocket").intValue = (int)HoldSocket.RightHand;

            var efficient = new List<BlockDefinition>();
            foreach (string blockId in efficientBlockIds)
            {
                BlockDefinition block = FindBlock(blockId);
                if (block != null)
                    efficient.Add(block);
            }

            SerializedProperty list = serialized.FindProperty("efficientBlocks");
            list.arraySize = efficient.Count;
            for (int i = 0; i < efficient.Count; i++)
                list.GetArrayElementAtIndex(i).objectReferenceValue = efficient[i];

            serialized.ApplyModifiedPropertiesWithoutUndo();
            ItemsCreated++;
        }

        private static ItemDefinition Weapon(
            string id, string displayName, string description, IconShape shape,
            Color primary, Color secondary, HoldType holdType,
            float damage, DamageType type, float attacksPerSecond, float range, float weightKg)
        {
            string path = $"{ItemFolder}/{ToAssetName(id)}.asset";
            ItemDefinition item = ExampleContentCreator.CreateOrLoad<ItemDefinition>(path, out bool created);
            if (!created)
                return item;

            Sprite icon = Icon(id, shape, primary, secondary);
            ExampleContentCreator.SetItemFields(item, id, displayName, description,
                icon, ItemCategory.Weapon, 1, weightKg);

            var serialized = new SerializedObject(item);
            serialized.FindProperty("isWeapon").boolValue = true;
            serialized.FindProperty("weaponDamage").floatValue = damage;
            serialized.FindProperty("damageType").intValue = (int)type;
            serialized.FindProperty("attacksPerSecond").floatValue = attacksPerSecond;
            serialized.FindProperty("attackRange").floatValue = range;
            serialized.FindProperty("holdType").intValue = (int)holdType;
            serialized.FindProperty("holdSocket").intValue = (int)HoldSocket.RightHand;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            ItemsCreated++;
            return item;
        }

        private static GameObject CreateTorchPrefab(string assetPath)
        {
            var existing = AssetDatabase.LoadAssetAtPath<GameObject>(assetPath);
            if (existing != null)
                return existing;

            var root = new GameObject("TorchHand");
            try
            {
                GameObject stick = GameObject.CreatePrimitive(PrimitiveType.Cube);
                stick.name = "Stick";
                stick.transform.SetParent(root.transform, false);
                stick.transform.localScale = new Vector3(0.04f, 0.4f, 0.04f);
                stick.transform.localPosition = new Vector3(0f, 0.2f, 0f);
                Object.DestroyImmediate(stick.GetComponent<Collider>()); // held models never collide

                GameObject ember = GameObject.CreatePrimitive(PrimitiveType.Cube);
                ember.name = "Ember";
                ember.transform.SetParent(root.transform, false);
                ember.transform.localScale = new Vector3(0.08f, 0.1f, 0.08f);
                ember.transform.localPosition = new Vector3(0f, 0.44f, 0f);
                Object.DestroyImmediate(ember.GetComponent<Collider>());

                var lightObject = new GameObject("TorchLight");
                lightObject.transform.SetParent(root.transform, false);
                lightObject.transform.localPosition = new Vector3(0f, 0.5f, 0f);
                var light = lightObject.AddComponent<Light>();
                light.type = LightType.Point;
                light.color = new Color(1f, 0.62f, 0.25f);
                light.range = 7f;
                light.intensity = 1.6f;
                light.shadows = LightShadows.None;

                GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, assetPath);
                PrefabsCreated++;
                return prefab;
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        /// <summary>lowercase_underscore id → PascalCase asset filename (plank_oak → PlankOak).</summary>
        internal static string ToAssetName(string id)
        {
            string[] parts = id.Split('_');
            var builder = new System.Text.StringBuilder();
            foreach (string part in parts)
            {
                if (part.Length == 0)
                    continue;

                builder.Append(char.ToUpperInvariant(part[0]));
                if (part.Length > 1)
                    builder.Append(part.Substring(1));
            }

            return builder.ToString();
        }
    }
}
