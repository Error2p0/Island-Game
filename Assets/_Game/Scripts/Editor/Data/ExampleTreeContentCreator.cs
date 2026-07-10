using System.Linq;
using IslandGame.Data.Blocks;
using IslandGame.Data.Items;
using IslandGame.Data.World;
using UnityEditor;
using UnityEngine;

namespace IslandGame.EditorTools.Data
{
    /// <summary>
    /// Creates the small-voxel tree content: bark/leaf textures, the Wood and
    /// Leaves BlockDefinitions (real blocks — drops, hardness, transparency
    /// and the NeedsSupport collapse flag all live on them, no special-cased
    /// tree renderer or mining code), and two TreeTemplateDefinitions (a
    /// broad-crowned oak and a tall conical pine) that the island generator
    /// scatters on grassy land. Wood drops the existing Log item, closing the
    /// loop into planks → building.
    ///
    /// Same idempotent pipeline as the other content creators: existing
    /// assets are left untouched, only missing pieces are created.
    /// </summary>
    public static class ExampleTreeContentCreator
    {
        private const string BlockFolder = "Assets/_Game/Content/Blocks";
        private const string BlockTextureFolder = "Assets/_Game/Content/Textures/Blocks";
        private const string TreeFolder = "Assets/_Game/Content/Trees";

        [MenuItem("Island Game/Data/Create Tree Content")]
        public static void Create()
        {
            DefinitionDatabaseSync.EnsureFolderExists(BlockFolder);
            DefinitionDatabaseSync.EnsureFolderExists(BlockTextureFolder);
            DefinitionDatabaseSync.EnsureFolderExists(TreeFolder);

            ItemDefinition logItem = FindItem("log");
            if (logItem == null)
                Debug.LogWarning("ExampleTreeContentCreator: item 'log' not found — wood will drop nothing. Run Island Game/Data/Create Example Content first.");

            // --- Textures ------------------------------------------------
            Texture2D barkTexture = ExampleContentCreator.CreateTexture(
                $"{BlockTextureFolder}/bark.png", 16, BarkPixel, asSprite: false);
            Texture2D leavesTexture = ExampleContentCreator.CreateTexture(
                $"{BlockTextureFolder}/leaves.png", 16, LeavesPixel, asSprite: false);

            // --- Blocks ----------------------------------------------------
            BlockDefinition woodBlock = ExampleContentCreator.CreateOrLoad<BlockDefinition>(
                $"{BlockFolder}/WoodBlock.asset", out bool woodCreated);
            if (woodCreated)
            {
                SetBlockFields(woodBlock,
                    id: "wood", displayName: "Wood", texture: barkTexture,
                    isSolid: true, isTransparent: false, hardness: 1.5f, requiredToolTier: 0,
                    dropItem: logItem, dropCountMin: 1, dropCountMax: 1,
                    flags: BlockBehaviorFlags.Flammable | BlockBehaviorFlags.NeedsSupport);
            }

            BlockDefinition leavesBlock = ExampleContentCreator.CreateOrLoad<BlockDefinition>(
                $"{BlockFolder}/LeavesBlock.asset", out bool leavesCreated);
            if (leavesCreated)
            {
                SetBlockFields(leavesBlock,
                    id: "leaves", displayName: "Leaves", texture: leavesTexture,
                    isSolid: true, isTransparent: true, hardness: 0.25f, requiredToolTier: 0,
                    dropItem: null, dropCountMin: 0, dropCountMax: 0,
                    flags: BlockBehaviorFlags.Flammable | BlockBehaviorFlags.NeedsSupport);
            }

            // --- Tree templates ---------------------------------------------
            TreeTemplateDefinition oak = ExampleContentCreator.CreateOrLoad<TreeTemplateDefinition>(
                $"{TreeFolder}/OakTree.asset", out bool oakCreated);
            if (oakCreated)
            {
                SetTemplateFields(oak, "oak", "Oak Tree", woodBlock, leavesBlock,
                    resolution: 4, spawnWeight: 1f,
                    strokes: new[]
                    {
                        new TreeStroke(TreePart.Trunk, new Vector3(0f, 0f, 0f), new Vector3(0f, 3.0f, 0f), 0.30f),
                        new TreeStroke(TreePart.Branch, new Vector3(0f, 2.4f, 0f), new Vector3(1.1f, 3.3f, 0.6f), 0.16f),
                        new TreeStroke(TreePart.Branch, new Vector3(0f, 2.7f, 0f), new Vector3(-0.9f, 3.5f, -0.7f), 0.16f),
                        new TreeStroke(TreePart.Leaves, new Vector3(0f, 3.9f, 0f), new Vector3(0f, 3.9f, 0f), 1.7f),
                        new TreeStroke(TreePart.Leaves, new Vector3(1.1f, 3.6f, 0.7f), new Vector3(1.1f, 3.6f, 0.7f), 1.0f),
                        new TreeStroke(TreePart.Leaves, new Vector3(-0.9f, 3.8f, -0.8f), new Vector3(-0.9f, 3.8f, -0.8f), 1.0f),
                    });
            }

            TreeTemplateDefinition pine = ExampleContentCreator.CreateOrLoad<TreeTemplateDefinition>(
                $"{TreeFolder}/PineTree.asset", out bool pineCreated);
            if (pineCreated)
            {
                SetTemplateFields(pine, "pine", "Pine Tree", woodBlock, leavesBlock,
                    resolution: 4, spawnWeight: 0.7f,
                    strokes: new[]
                    {
                        new TreeStroke(TreePart.Trunk, new Vector3(0f, 0f, 0f), new Vector3(0f, 5.2f, 0f), 0.24f),
                        new TreeStroke(TreePart.Leaves, new Vector3(0f, 2.4f, 0f), new Vector3(0f, 2.4f, 0f), 1.6f),
                        new TreeStroke(TreePart.Leaves, new Vector3(0f, 3.6f, 0f), new Vector3(0f, 3.6f, 0f), 1.25f),
                        new TreeStroke(TreePart.Leaves, new Vector3(0f, 4.6f, 0f), new Vector3(0f, 4.6f, 0f), 0.9f),
                        new TreeStroke(TreePart.Leaves, new Vector3(0f, 5.5f, 0f), new Vector3(0f, 5.5f, 0f), 0.5f),
                    });
            }

            AssetDatabase.SaveAssets();
            DefinitionDatabaseSync.SyncAll();

            Debug.Log(
                "Tree content ready: Wood (drops Log, NeedsSupport) and Leaves (transparent, NeedsSupport) blocks, " +
                "bark/leaf textures, Oak + Pine templates. Trees scatter on grassy land next time the world " +
                "generates (already-generated chunk data keeps its treeless state for the session — restart play " +
                "mode to see them everywhere). Existing assets were left untouched.");
        }

        // ------------------------------------------------------------------
        // Field setters
        // ------------------------------------------------------------------

        private static void SetBlockFields(
            BlockDefinition block, string id, string displayName, Texture2D texture,
            bool isSolid, bool isTransparent, float hardness, int requiredToolTier,
            ItemDefinition dropItem, int dropCountMin, int dropCountMax, BlockBehaviorFlags flags)
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
            serialized.FindProperty("behaviorFlags").intValue = (int)flags;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void SetTemplateFields(
            TreeTemplateDefinition template, string id, string displayName,
            BlockDefinition trunkBlock, BlockDefinition leavesBlock,
            int resolution, float spawnWeight, TreeStroke[] strokes)
        {
            var serialized = new SerializedObject(template);
            serialized.FindProperty("id").stringValue = id;
            serialized.FindProperty("displayName").stringValue = displayName;
            serialized.FindProperty("trunkBlock").objectReferenceValue = trunkBlock;
            serialized.FindProperty("leavesBlock").objectReferenceValue = leavesBlock;
            serialized.FindProperty("resolution").intValue = resolution;
            serialized.FindProperty("spawnWeight").floatValue = spawnWeight;

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

        private static ItemDefinition FindItem(string id)
        {
            return AssetDatabase.FindAssets("t:ItemDefinition")
                .Select(AssetDatabase.GUIDToAssetPath)
                .Select(AssetDatabase.LoadAssetAtPath<ItemDefinition>)
                .FirstOrDefault(item => item != null && item.Id == id);
        }

        // ------------------------------------------------------------------
        // Textures
        // ------------------------------------------------------------------

        private static Color32 BarkPixel(int x, int y, int size, System.Random random)
        {
            // Vertical streaks: per-column base shade with grain noise.
            float column = 0.42f + ((x * 7 + 3) % 5) * 0.035f;
            float grain = (float)(random.NextDouble() - 0.5) * 0.08f;
            float value = Mathf.Clamp01(column + grain);

            return new Color32(
                (byte)Mathf.Clamp(Mathf.RoundToInt(value * 0.85f * 255f), 0, 255),
                (byte)Mathf.Clamp(Mathf.RoundToInt(value * 0.62f * 255f), 0, 255),
                (byte)Mathf.Clamp(Mathf.RoundToInt(value * 0.40f * 255f), 0, 255),
                255);
        }

        private static Color32 LeavesPixel(int x, int y, int size, System.Random random)
        {
            // Opaque green mottle with darker specks. Deliberately no alpha
            // holes: the transparent submesh alpha-BLENDS (water shares it),
            // and blended leaf quads with holes would sort badly — a real
            // alpha-CLIP leaf material is a noted improvement for the visual
            // content pass.
            float value = 0.45f + (float)(random.NextDouble() - 0.5) * 0.2f;
            if (random.NextDouble() < 0.12)
                value -= 0.15f;

            return new Color32(
                (byte)Mathf.Clamp(Mathf.RoundToInt(value * 0.45f * 255f), 0, 255),
                (byte)Mathf.Clamp(Mathf.RoundToInt(value * 255f), 0, 255),
                (byte)Mathf.Clamp(Mathf.RoundToInt(value * 0.35f * 255f), 0, 255),
                255);
        }
    }
}
