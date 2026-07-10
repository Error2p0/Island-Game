using IslandGame.Data.Blocks;
using IslandGame.Data.Items;
using UnityEditor;
using UnityEngine;

namespace IslandGame.EditorTools.Data
{
    /// <summary>
    /// Creates the Phase 7 world-generation block set — Sand, Dirt, Grass
    /// (per-face textures: green top, dirt bottom, blended sides) and Water
    /// (non-solid, transparent, Liquid flag) — plus their item forms and
    /// procedural textures, through the same idempotent pipeline as the
    /// Phase 1 example content. Everything it makes is a normal asset,
    /// editable afterwards in the Block/Item Editor windows.
    /// </summary>
    public static class WorldGenContentCreator
    {
        private const string ItemFolder = "Assets/_Game/Content/Items";
        private const string BlockFolder = "Assets/_Game/Content/Blocks";
        private const string BlockTextureFolder = "Assets/_Game/Content/Textures/Blocks";
        private const string IconFolder = "Assets/_Game/Content/Textures/Icons";

        [MenuItem("Island Game/Data/Create World Gen Content")]
        public static void Create()
        {
            DefinitionDatabaseSync.EnsureFolderExists(ItemFolder);
            DefinitionDatabaseSync.EnsureFolderExists(BlockFolder);
            DefinitionDatabaseSync.EnsureFolderExists(BlockTextureFolder);
            DefinitionDatabaseSync.EnsureFolderExists(IconFolder);

            // --- Textures ------------------------------------------------
            Texture2D sandTexture = ExampleContentCreator.CreateTexture($"{BlockTextureFolder}/sand.png", 16, SandPixel, asSprite: false);
            Texture2D dirtTexture = ExampleContentCreator.CreateTexture($"{BlockTextureFolder}/dirt.png", 16, DirtPixel, asSprite: false);
            Texture2D grassTopTexture = ExampleContentCreator.CreateTexture($"{BlockTextureFolder}/grass_top.png", 16, GrassTopPixel, asSprite: false);
            Texture2D grassSideTexture = ExampleContentCreator.CreateTexture($"{BlockTextureFolder}/grass_side.png", 16, GrassSidePixel, asSprite: false);
            Texture2D waterTexture = ExampleContentCreator.CreateTexture($"{BlockTextureFolder}/water.png", 16, WaterPixel, asSprite: false);

            Sprite sandIcon = ExampleContentCreator.LoadSprite(
                ExampleContentCreator.CreateTexture($"{IconFolder}/icon_sand.png", 64, SandPixel, asSprite: true));
            Sprite dirtIcon = ExampleContentCreator.LoadSprite(
                ExampleContentCreator.CreateTexture($"{IconFolder}/icon_dirt.png", 64, DirtPixel, asSprite: true));
            Sprite grassIcon = ExampleContentCreator.LoadSprite(
                ExampleContentCreator.CreateTexture($"{IconFolder}/icon_grass.png", 64, GrassTopPixel, asSprite: true));

            // --- Items (pass 1) -------------------------------------------
            ItemDefinition sandItem = ExampleContentCreator.CreateOrLoad<ItemDefinition>($"{ItemFolder}/Sand.asset", out bool sandItemCreated);
            if (sandItemCreated)
            {
                ExampleContentCreator.SetItemFields(sandItem,
                    id: "sand", displayName: "Sand", description: "Loose coastal sand. Builds soft, sinks hearts.",
                    icon: sandIcon, category: ItemCategory.Block, maxStackSize: 100, weightKg: 1.2f);
            }

            ItemDefinition dirtItem = ExampleContentCreator.CreateOrLoad<ItemDefinition>($"{ItemFolder}/Dirt.asset", out bool dirtItemCreated);
            if (dirtItemCreated)
            {
                ExampleContentCreator.SetItemFields(dirtItem,
                    id: "dirt", displayName: "Dirt", description: "Plain earth from under the grass.",
                    icon: dirtIcon, category: ItemCategory.Block, maxStackSize: 100, weightKg: 1f);
            }

            ItemDefinition grassItem = ExampleContentCreator.CreateOrLoad<ItemDefinition>($"{ItemFolder}/GrassBlock.asset", out bool grassItemCreated);
            if (grassItemCreated)
            {
                ExampleContentCreator.SetItemFields(grassItem,
                    id: "grass_block", displayName: "Grass Block", description: "A turf-topped chunk of earth.",
                    icon: grassIcon, category: ItemCategory.Block, maxStackSize: 100, weightKg: 1f);
            }

            // --- Blocks ---------------------------------------------------
            BlockDefinition sandBlock = ExampleContentCreator.CreateOrLoad<BlockDefinition>($"{BlockFolder}/SandBlock.asset", out bool sandBlockCreated);
            if (sandBlockCreated)
            {
                SetBlockFieldsExtended(sandBlock,
                    id: "sand", displayName: "Sand",
                    uniformTexture: sandTexture, topTexture: null, bottomTexture: null, sideTexture: null,
                    isSolid: true, isTransparent: false, hardness: 0.8f, requiredToolTier: 0,
                    dropItem: sandItem, dropCountMin: 1, dropCountMax: 1, flags: BlockBehaviorFlags.None);
            }

            BlockDefinition dirtBlock = ExampleContentCreator.CreateOrLoad<BlockDefinition>($"{BlockFolder}/DirtBlock.asset", out bool dirtBlockCreated);
            if (dirtBlockCreated)
            {
                SetBlockFieldsExtended(dirtBlock,
                    id: "dirt", displayName: "Dirt",
                    uniformTexture: dirtTexture, topTexture: null, bottomTexture: null, sideTexture: null,
                    isSolid: true, isTransparent: false, hardness: 0.9f, requiredToolTier: 0,
                    dropItem: dirtItem, dropCountMin: 1, dropCountMax: 1, flags: BlockBehaviorFlags.None);
            }

            BlockDefinition grassBlock = ExampleContentCreator.CreateOrLoad<BlockDefinition>($"{BlockFolder}/GrassBlock.asset", out bool grassBlockCreated);
            if (grassBlockCreated)
            {
                // Per-face showcase: green top, dirt bottom (via fallback), blended sides.
                SetBlockFieldsExtended(grassBlock,
                    id: "grass", displayName: "Grass",
                    uniformTexture: dirtTexture, topTexture: grassTopTexture, bottomTexture: null, sideTexture: grassSideTexture,
                    isSolid: true, isTransparent: false, hardness: 1f, requiredToolTier: 0,
                    dropItem: dirtItem, dropCountMin: 1, dropCountMax: 1, flags: BlockBehaviorFlags.None);
            }

            BlockDefinition waterBlock = ExampleContentCreator.CreateOrLoad<BlockDefinition>($"{BlockFolder}/WaterBlock.asset", out bool waterBlockCreated);
            if (waterBlockCreated)
            {
                SetBlockFieldsExtended(waterBlock,
                    id: "water", displayName: "Water",
                    uniformTexture: waterTexture, topTexture: null, bottomTexture: null, sideTexture: null,
                    isSolid: false, isTransparent: true, hardness: 0f, requiredToolTier: 0,
                    dropItem: null, dropCountMin: 0, dropCountMax: 0, flags: BlockBehaviorFlags.Liquid);
            }

            // --- Items (pass 2: placement links) --------------------------
            if (sandItemCreated)
                ExampleContentCreator.SetObjectReference(sandItem, "placedBlock", sandBlock);
            if (dirtItemCreated)
                ExampleContentCreator.SetObjectReference(dirtItem, "placedBlock", dirtBlock);
            if (grassItemCreated)
                ExampleContentCreator.SetObjectReference(grassItem, "placedBlock", grassBlock);

            AssetDatabase.SaveAssets();
            DefinitionDatabaseSync.SyncAll();

            Debug.Log(
                "World-gen content ready: blocks Sand/Dirt/Grass/Water (+ textures), items Sand/Dirt/Grass Block, " +
                "links wired, databases synced. Existing assets were left untouched. " +
                "Water has no item form on purpose — the creative menu lists it as a non-giveable entry.");
        }

        /// <summary>Block field setter covering per-face textures and behavior flags (superset of the Phase 1 uniform-only helper).</summary>
        private static void SetBlockFieldsExtended(
            BlockDefinition block, string id, string displayName,
            Texture2D uniformTexture, Texture2D topTexture, Texture2D bottomTexture, Texture2D sideTexture,
            bool isSolid, bool isTransparent, float hardness, int requiredToolTier,
            ItemDefinition dropItem, int dropCountMin, int dropCountMax, BlockBehaviorFlags flags)
        {
            bool anyPerFace = topTexture != null || bottomTexture != null || sideTexture != null;

            var serialized = new SerializedObject(block);
            serialized.FindProperty("id").stringValue = id;
            serialized.FindProperty("displayName").stringValue = displayName;
            serialized.FindProperty("useUniformTexture").boolValue = !anyPerFace;
            serialized.FindProperty("uniformTexture").objectReferenceValue = uniformTexture;
            serialized.FindProperty("topTexture").objectReferenceValue = topTexture;
            serialized.FindProperty("bottomTexture").objectReferenceValue = bottomTexture;
            serialized.FindProperty("northTexture").objectReferenceValue = sideTexture;
            serialized.FindProperty("southTexture").objectReferenceValue = sideTexture;
            serialized.FindProperty("eastTexture").objectReferenceValue = sideTexture;
            serialized.FindProperty("westTexture").objectReferenceValue = sideTexture;
            serialized.FindProperty("isSolid").boolValue = isSolid;
            serialized.FindProperty("isTransparent").boolValue = isTransparent;
            serialized.FindProperty("hardness").floatValue = hardness;
            serialized.FindProperty("requiredToolTier").intValue = requiredToolTier;
            serialized.FindProperty("dropItem").objectReferenceValue = dropItem;
            serialized.FindProperty("dropCountMin").intValue = dropCountMin;
            serialized.FindProperty("dropCountMax").intValue = dropCountMax;
            serialized.FindProperty("behaviorFlags").intValue = (int)flags;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        // ------------------------------------------------------------------
        // Procedural pixels
        // ------------------------------------------------------------------

        private static Color32 SandPixel(int x, int y, int size, System.Random random)
        {
            float noise = (float)(random.NextDouble() - 0.5) * 0.12f;
            if (random.NextDouble() < 0.05)
                noise -= 0.12f; // darker grains

            return ToColor(0.87f + noise, 0.8f + noise, 0.55f + noise, 1f);
        }

        private static Color32 DirtPixel(int x, int y, int size, System.Random random)
        {
            float noise = (float)(random.NextDouble() - 0.5) * 0.14f;
            return ToColor(0.45f + noise, 0.32f + noise, 0.2f + noise * 0.7f, 1f);
        }

        private static Color32 GrassTopPixel(int x, int y, int size, System.Random random)
        {
            float noise = (float)(random.NextDouble() - 0.5) * 0.16f;
            return ToColor(0.3f + noise * 0.6f, 0.55f + noise, 0.24f + noise * 0.5f, 1f);
        }

        private static Color32 GrassSidePixel(int x, int y, int size, System.Random random)
        {
            // Texture v=1 is the TOP of the face (sides map texture-up = +Y),
            // so the top quarter of rows is turf and the rest is dirt.
            int turfRows = Mathf.Max(2, size / 4);
            bool turf = y >= size - turfRows;
            return turf ? GrassTopPixel(x, y, size, random) : DirtPixel(x, y, size, random);
        }

        private static Color32 WaterPixel(int x, int y, int size, System.Random random)
        {
            float noise = (float)(random.NextDouble() - 0.5) * 0.08f;
            return ToColor(0.2f + noise * 0.5f, 0.45f + noise, 0.75f + noise, 0.72f);
        }

        private static Color32 ToColor(float r, float g, float b, float a)
        {
            return new Color32(
                (byte)Mathf.Clamp(Mathf.RoundToInt(r * 255f), 0, 255),
                (byte)Mathf.Clamp(Mathf.RoundToInt(g * 255f), 0, 255),
                (byte)Mathf.Clamp(Mathf.RoundToInt(b * 255f), 0, 255),
                (byte)Mathf.Clamp(Mathf.RoundToInt(a * 255f), 0, 255));
        }
    }
}
