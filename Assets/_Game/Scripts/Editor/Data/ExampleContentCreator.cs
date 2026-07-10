using System.IO;
using IslandGame.Data.Blocks;
using IslandGame.Data.Items;
using UnityEditor;
using UnityEngine;

namespace IslandGame.EditorTools.Data
{
    /// <summary>
    /// Creates a small, fully-linked set of example content proving the Phase 1
    /// data model end-to-end: procedural block textures and icons, items
    /// (Log, Stone, Wood Plank), blocks (Stone, Wood Plank), the block→drop-item
    /// links, the item→placed-block links, and a database sync at the end.
    ///
    /// Idempotent: assets that already exist are left untouched (so hand edits
    /// survive re-running it), only missing pieces are created. Real gameplay
    /// content is authored with the Phase 2/3 editor windows, not here.
    /// </summary>
    public static class ExampleContentCreator
    {
        private const string ItemFolder = "Assets/_Game/Content/Items";
        private const string BlockFolder = "Assets/_Game/Content/Blocks";
        private const string BlockTextureFolder = "Assets/_Game/Content/Textures/Blocks";
        private const string IconFolder = "Assets/_Game/Content/Textures/Icons";

        [MenuItem("Island Game/Data/Create Example Content")]
        public static void Create()
        {
            DefinitionDatabaseSync.EnsureFolderExists(ItemFolder);
            DefinitionDatabaseSync.EnsureFolderExists(BlockFolder);
            DefinitionDatabaseSync.EnsureFolderExists(BlockTextureFolder);
            DefinitionDatabaseSync.EnsureFolderExists(IconFolder);

            // --- Textures ------------------------------------------------
            Texture2D stoneTexture = CreateTexture($"{BlockTextureFolder}/stone.png", 16, StonePixel, asSprite: false);
            Texture2D plankTexture = CreateTexture($"{BlockTextureFolder}/plank.png", 16, PlankPixel, asSprite: false);

            Sprite stoneIcon = LoadSprite(CreateTexture($"{IconFolder}/icon_stone.png", 64, StonePixel, asSprite: true));
            Sprite plankIcon = LoadSprite(CreateTexture($"{IconFolder}/icon_plank.png", 64, PlankPixel, asSprite: true));
            Sprite logIcon = LoadSprite(CreateTexture($"{IconFolder}/icon_log.png", 64, LogIconPixel, asSprite: true));

            // --- Items (pass 1: exist before blocks can reference them) ---
            ItemDefinition logItem = CreateOrLoad<ItemDefinition>($"{ItemFolder}/Log.asset", out bool logCreated);
            if (logCreated)
            {
                SetItemFields(logItem,
                    id: "log", displayName: "Log", description: "A freshly felled tree trunk. Heavy — sprinting with a few of these is not happening.",
                    icon: logIcon, category: ItemCategory.Resource, maxStackSize: 4, weightKg: 10f);
            }

            ItemDefinition stoneItem = CreateOrLoad<ItemDefinition>($"{ItemFolder}/Stone.asset", out bool stoneCreated);
            if (stoneCreated)
            {
                SetItemFields(stoneItem,
                    id: "stone", displayName: "Stone", description: "A chunk of solid rock. Builds sturdy walls and blunt arguments.",
                    icon: stoneIcon, category: ItemCategory.Block, maxStackSize: 50, weightKg: 1.5f);
            }

            ItemDefinition plankItem = CreateOrLoad<ItemDefinition>($"{ItemFolder}/WoodPlank.asset", out bool plankCreated);
            if (plankCreated)
            {
                SetItemFields(plankItem,
                    id: "wood_plank", displayName: "Wood Plank", description: "Sawn lumber, ready for building.",
                    icon: plankIcon, category: ItemCategory.Block, maxStackSize: 100, weightKg: 0.5f);
            }

            // --- Blocks (reference the drop items) ------------------------
            BlockDefinition stoneBlock = CreateOrLoad<BlockDefinition>($"{BlockFolder}/StoneBlock.asset", out bool stoneBlockCreated);
            if (stoneBlockCreated)
            {
                SetBlockFields(stoneBlock,
                    id: "stone", displayName: "Stone", texture: stoneTexture,
                    isSolid: true, isTransparent: false, hardness: 3f, requiredToolTier: 1,
                    dropItem: stoneItem, dropCountMin: 1, dropCountMax: 1);
            }

            BlockDefinition plankBlock = CreateOrLoad<BlockDefinition>($"{BlockFolder}/WoodPlankBlock.asset", out bool plankBlockCreated);
            if (plankBlockCreated)
            {
                SetBlockFields(plankBlock,
                    id: "wood_plank", displayName: "Wood Plank", texture: plankTexture,
                    isSolid: true, isTransparent: false, hardness: 1.5f, requiredToolTier: 0,
                    dropItem: plankItem, dropCountMin: 1, dropCountMax: 1);
            }

            // --- Items (pass 2: close the item→block loop) ----------------
            if (stoneCreated)
                SetObjectReference(stoneItem, "placedBlock", stoneBlock);
            if (plankCreated)
                SetObjectReference(plankItem, "placedBlock", plankBlock);

            AssetDatabase.SaveAssets();
            DefinitionDatabaseSync.SyncAll();

            Debug.Log(
                "Example content ready: items Log/Stone/Wood Plank, blocks Stone/Wood Plank, " +
                "textures + icons, drop and placement links, databases synced. " +
                "Existing assets were left untouched.");
            EditorGUIUtility.PingObject(AssetDatabase.LoadAssetAtPath<Object>("Assets/_Game/Content"));
        }

        // ------------------------------------------------------------------
        // Definition assets
        // ------------------------------------------------------------------

        // The helpers below are internal so other content creators (the Phase 7
        // world-gen content menu) reuse the exact same asset/texture pipeline.
        internal static T CreateOrLoad<T>(string assetPath, out bool created) where T : ScriptableObject
        {
            var existing = AssetDatabase.LoadAssetAtPath<T>(assetPath);
            if (existing != null)
            {
                created = false;
                return existing;
            }

            var asset = ScriptableObject.CreateInstance<T>();
            AssetDatabase.CreateAsset(asset, assetPath);
            created = true;
            return asset;
        }

        internal static void SetItemFields(
            ItemDefinition item, string id, string displayName, string description,
            Sprite icon, ItemCategory category, int maxStackSize, float weightKg)
        {
            var serialized = new SerializedObject(item);
            serialized.FindProperty("id").stringValue = id;
            serialized.FindProperty("displayName").stringValue = displayName;
            serialized.FindProperty("description").stringValue = description;
            serialized.FindProperty("icon").objectReferenceValue = icon;
            serialized.FindProperty("category").intValue = (int)category;
            serialized.FindProperty("maxStackSize").intValue = maxStackSize;
            serialized.FindProperty("weightKg").floatValue = weightKg;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void SetBlockFields(
            BlockDefinition block, string id, string displayName, Texture2D texture,
            bool isSolid, bool isTransparent, float hardness, int requiredToolTier,
            ItemDefinition dropItem, int dropCountMin, int dropCountMax)
        {
            var serialized = new SerializedObject(block);
            serialized.FindProperty("id").stringValue = id;
            serialized.FindProperty("displayName").stringValue = displayName;
            serialized.FindProperty("useUniformTexture").boolValue = true;
            serialized.FindProperty("uniformTexture").objectReferenceValue = texture;
            serialized.FindProperty("isSolid").boolValue = isSolid;
            serialized.FindProperty("isTransparent").boolValue = isTransparent;
            serialized.FindProperty("hardness").floatValue = hardness;
            serialized.FindProperty("requiredToolTier").intValue = requiredToolTier;
            serialized.FindProperty("dropItem").objectReferenceValue = dropItem;
            serialized.FindProperty("dropCountMin").intValue = dropCountMin;
            serialized.FindProperty("dropCountMax").intValue = dropCountMax;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        internal static void SetObjectReference(ScriptableObject asset, string propertyName, Object value)
        {
            var serialized = new SerializedObject(asset);
            serialized.FindProperty(propertyName).objectReferenceValue = value;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        // ------------------------------------------------------------------
        // Procedural textures
        // ------------------------------------------------------------------

        internal delegate Color32 PixelFunc(int x, int y, int size, System.Random random);

        internal static Texture2D CreateTexture(string assetPath, int size, PixelFunc pixel, bool asSprite)
        {
            var existing = AssetDatabase.LoadAssetAtPath<Texture2D>(assetPath);
            if (existing != null)
                return existing;

            // Deterministic pattern: same seed every run, so re-created files
            // are byte-identical and diffs stay quiet.
            var random = new System.Random(12345);
            var pixels = new Color32[size * size];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                    pixels[y * size + x] = pixel(x, y, size, random);
            }

            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
            texture.SetPixels32(pixels);
            texture.Apply(false, false);
            byte[] png = texture.EncodeToPNG();
            Object.DestroyImmediate(texture);

            string fullPath = Path.GetFullPath(assetPath);
            File.WriteAllBytes(fullPath, png);
            AssetDatabase.ImportAsset(assetPath);

            var importer = (TextureImporter)AssetImporter.GetAtPath(assetPath);
            if (asSprite)
            {
                importer.textureType = TextureImporterType.Sprite;
                importer.spriteImportMode = SpriteImportMode.Single;
            }
            else
            {
                // Block textures follow the BlockTextureAtlas convention:
                // readable (packing reads pixels), point-filtered, uncompressed.
                importer.textureType = TextureImporterType.Default;
                importer.isReadable = true;
            }

            importer.filterMode = FilterMode.Point;
            importer.mipmapEnabled = false;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.SaveAndReimport();

            return AssetDatabase.LoadAssetAtPath<Texture2D>(assetPath);
        }

        internal static Sprite LoadSprite(Texture2D texture)
        {
            if (texture == null)
                return null;

            return AssetDatabase.LoadAssetAtPath<Sprite>(AssetDatabase.GetAssetPath(texture));
        }

        private static Color32 StonePixel(int x, int y, int size, System.Random random)
        {
            float value = 0.52f + (float)(random.NextDouble() - 0.5) * 0.16f;
            if (random.NextDouble() < 0.06)
                value -= 0.18f; // dark speckles

            byte gray = (byte)Mathf.Clamp(Mathf.RoundToInt(value * 255f), 0, 255);
            return new Color32(gray, gray, (byte)Mathf.Clamp(gray + 6, 0, 255), 255);
        }

        private static Color32 PlankPixel(int x, int y, int size, System.Random random)
        {
            int plankHeight = Mathf.Max(4, size / 4);
            bool seam = y % plankHeight == 0;

            float r = 0.72f, g = 0.55f, b = 0.35f;
            float grain = (float)(random.NextDouble() - 0.5) * 0.08f;

            if (seam)
            {
                r *= 0.55f;
                g *= 0.55f;
                b *= 0.55f;
            }

            return new Color32(
                (byte)Mathf.Clamp(Mathf.RoundToInt((r + grain) * 255f), 0, 255),
                (byte)Mathf.Clamp(Mathf.RoundToInt((g + grain) * 255f), 0, 255),
                (byte)Mathf.Clamp(Mathf.RoundToInt((b + grain) * 255f), 0, 255),
                255);
        }

        private static Color32 LogIconPixel(int x, int y, int size, System.Random random)
        {
            // End-grain look: concentric rings on a transparent background.
            float half = size * 0.5f;
            float dx = x - half + 0.5f;
            float dy = y - half + 0.5f;
            float distance = Mathf.Sqrt(dx * dx + dy * dy);

            if (distance > half - 1f)
                return new Color32(0, 0, 0, 0);

            bool ring = Mathf.RoundToInt(distance) % Mathf.Max(2, size / 10) == 0;
            float shade = ring ? 0.42f : 0.62f;
            shade += (float)(random.NextDouble() - 0.5) * 0.05f;

            return new Color32(
                (byte)Mathf.Clamp(Mathf.RoundToInt(shade * 255f), 0, 255),
                (byte)Mathf.Clamp(Mathf.RoundToInt(shade * 0.68f * 255f), 0, 255),
                (byte)Mathf.Clamp(Mathf.RoundToInt(shade * 0.42f * 255f), 0, 255),
                255);
        }
    }
}
