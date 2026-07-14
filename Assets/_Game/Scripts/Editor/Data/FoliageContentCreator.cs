using IslandGame.Data.Foliage;
using IslandGame.Data.Items;
using IslandGame.Foliage;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace IslandGame.EditorTools.Data
{
    /// <summary>
    /// Creates the Phase 1 foliage content: the Berries and Plant Fiber items
    /// (with generated icons), leaf/berry/reed materials, three primitive-
    /// built prefabs in the established "real geometry from primitives"
    /// style — Berry Bush (berry clusters that vanish when picked), plain
    /// Shrub (ambient, no yield, no interaction) and Reed Cluster (tall
    /// stalks that cut down to stubble, yielding Plant Fiber) — and the
    /// FoliageDefinition assets the scatter system reads.
    ///
    /// Prefab conventions match the creature builder: ONE solid collider on
    /// the root (the interact aim ray ignores triggers), collider-less
    /// primitive visuals under organizing children, HarvestableFoliage wired
    /// on harvestables only — the shrub carries no interactable component at
    /// all, so it can never show a prompt.
    ///
    /// Idempotent like every content creator: existing assets are left
    /// untouched, only missing pieces are created.
    /// </summary>
    public static class FoliageContentCreator
    {
        private const string FoliageFolder = "Assets/_Game/Content/Foliage";
        private const string PrefabFolder = FoliageFolder + "/Prefabs";
        private const string MaterialFolder = FoliageFolder + "/Materials";
        private const string ItemFolder = "Assets/_Game/Content/Items";
        private const string IconFolder = "Assets/_Game/Content/Textures/Icons";

        [MenuItem("Island Game/Data/Create Foliage Content")]
        public static void Create()
        {
            DefinitionDatabaseSync.EnsureFolderExists(FoliageFolder);
            DefinitionDatabaseSync.EnsureFolderExists(PrefabFolder);
            DefinitionDatabaseSync.EnsureFolderExists(MaterialFolder);
            DefinitionDatabaseSync.EnsureFolderExists(ItemFolder);
            DefinitionDatabaseSync.EnsureFolderExists(IconFolder);

            // --- Items -----------------------------------------------------
            ItemDefinition berries = CreateItem(
                "berries", "Berries", "A handful of wild island berries. Sweet, juicy, and gone too fast.",
                BerriesPixel, maxStack: 20, weightKg: 0.1f);
            MakeEdible(berries, hungerRestore: 8f, thirstRestore: 4f);

            ItemDefinition plantFiber = CreateItem(
                "plant_fiber", "Plant Fiber", "Tough reed strands. Cordage, weaving and kindling all start here.",
                FiberPixel, maxStack: 30, weightKg: 0.1f);

            // --- Materials ---------------------------------------------------
            Material bushLeaf = GetOrCreateMaterial("FoliageBushLeaf", new Color(0.18f, 0.42f, 0.16f));
            Material bushLeafPicked = GetOrCreateMaterial("FoliageBushLeafPicked", new Color(0.33f, 0.36f, 0.20f));
            Material berry = GetOrCreateMaterial("FoliageBerry", new Color(0.72f, 0.12f, 0.16f));
            Material shrubLeaf = GetOrCreateMaterial("FoliageShrubLeaf", new Color(0.30f, 0.46f, 0.24f));
            Material reed = GetOrCreateMaterial("FoliageReed", new Color(0.45f, 0.56f, 0.22f));
            Material reedDry = GetOrCreateMaterial("FoliageReedDry", new Color(0.62f, 0.55f, 0.32f));
            Material stem = GetOrCreateMaterial("FoliageStem", new Color(0.36f, 0.26f, 0.16f));

            // --- Definitions + prefabs (definition first: the prefab's
            // HarvestableFoliage references it; the prefab reference is filled
            // back in afterwards, also when it went missing on an old asset) ---
            FoliageDefinition berryBush = ExampleContentCreator.CreateOrLoad<FoliageDefinition>(
                $"{FoliageFolder}/BerryBush.asset", out bool berryBushCreated);
            GameObject berryBushPrefab = BuildBerryBushPrefab(berryBush, bushLeaf, berry, stem);
            if (berryBushCreated)
            {
                SetFoliageFields(berryBush,
                    id: "berry_bush", displayName: "Berry Bush", category: FoliageCategory.Bush,
                    prefab: berryBushPrefab, yieldItem: berries, yieldCountMin: 2, yieldCountMax: 4,
                    regrowHours: 12f, depletedMaterial: bushLeafPicked,
                    surface: FoliageSurface.Grass, spawnWeight: 1f);
            }
            else
            {
                EnsurePrefabReference(berryBush, berryBushPrefab);
            }

            FoliageDefinition shrub = ExampleContentCreator.CreateOrLoad<FoliageDefinition>(
                $"{FoliageFolder}/PlainShrub.asset", out bool shrubCreated);
            GameObject shrubPrefab = BuildShrubPrefab(shrubLeaf, stem);
            if (shrubCreated)
            {
                SetFoliageFields(shrub,
                    id: "plain_shrub", displayName: "Shrub", category: FoliageCategory.Shrub,
                    prefab: shrubPrefab, yieldItem: null, yieldCountMin: 0, yieldCountMax: 0,
                    regrowHours: 1f, depletedMaterial: null,
                    surface: FoliageSurface.Grass, spawnWeight: 1.4f);
            }
            else
            {
                EnsurePrefabReference(shrub, shrubPrefab);
            }

            FoliageDefinition reedCluster = ExampleContentCreator.CreateOrLoad<FoliageDefinition>(
                $"{FoliageFolder}/ReedCluster.asset", out bool reedCreated);
            GameObject reedPrefab = BuildReedClusterPrefab(reedCluster, reed, reedDry);
            if (reedCreated)
            {
                SetFoliageFields(reedCluster,
                    id: "reed_cluster", displayName: "Reeds", category: FoliageCategory.Bush,
                    prefab: reedPrefab, yieldItem: plantFiber, yieldCountMin: 2, yieldCountMax: 3,
                    regrowHours: 8f, depletedMaterial: reedDry,
                    surface: FoliageSurface.Shore, spawnWeight: 1f);
            }
            else
            {
                EnsurePrefabReference(reedCluster, reedPrefab);
            }

            AssetDatabase.SaveAssets();
            DefinitionDatabaseSync.SyncAll();

            Debug.Log(
                "Foliage content ready: Berries (edible) + Plant Fiber items, Berry Bush / Shrub / Reed Cluster " +
                "prefabs and definitions. Add the scatter system with Island Game/World/Add Foliage System — " +
                "plants stream in around the player on grass and along the shoreline. Existing assets were left " +
                "untouched.");
        }

        // ------------------------------------------------------------------
        // Prefabs (primitive-built, one root collider, collider-less visuals)
        // ------------------------------------------------------------------

        private static GameObject BuildBerryBushPrefab(
            FoliageDefinition definition, Material leaf, Material berry, Material stem)
        {
            string prefabPath = $"{PrefabFolder}/BerryBush.prefab";
            var existing = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            if (existing != null)
                return existing;

            var root = new GameObject("BerryBush");
            try
            {
                var collider = root.AddComponent<SphereCollider>();
                collider.center = new Vector3(0f, 0.45f, 0f);
                collider.radius = 0.5f;

                // Woody base peeking out under the canopy.
                Visual(PrimitiveType.Cylinder, "Stem", root.transform,
                    new Vector3(0f, 0.15f, 0f), new Vector3(0.12f, 0.15f, 0.12f), Vector3.zero, stem);

                // Canopy: overlapping squashed spheres — a full, irregular bush.
                var canopy = Child("Canopy", root.transform);
                Renderer[] canopyRenderers =
                {
                    Visual(PrimitiveType.Sphere, "Blob1", canopy, new Vector3(0f, 0.42f, 0f),
                        new Vector3(0.95f, 0.72f, 0.95f), Vector3.zero, leaf),
                    Visual(PrimitiveType.Sphere, "Blob2", canopy, new Vector3(0.28f, 0.36f, 0.18f),
                        new Vector3(0.60f, 0.48f, 0.60f), Vector3.zero, leaf),
                    Visual(PrimitiveType.Sphere, "Blob3", canopy, new Vector3(-0.26f, 0.38f, -0.16f),
                        new Vector3(0.65f, 0.52f, 0.65f), Vector3.zero, leaf),
                    Visual(PrimitiveType.Sphere, "Blob4", canopy, new Vector3(0.02f, 0.64f, -0.08f),
                        new Vector3(0.52f, 0.42f, 0.52f), Vector3.zero, leaf),
                };

                // Berry clusters ON the canopy shell — hidden while depleted,
                // back on regrowth (the requirement: berries visibly vanish,
                // not a generic bush swap).
                var berriesRoot = Child("Berries", root.transform);
                Vector3[] berrySpots =
                {
                    new Vector3(0.34f, 0.56f, 0.30f), new Vector3(-0.36f, 0.52f, 0.22f),
                    new Vector3(0.42f, 0.38f, -0.18f), new Vector3(-0.30f, 0.42f, -0.32f),
                    new Vector3(0.06f, 0.78f, 0.14f), new Vector3(-0.14f, 0.72f, -0.06f),
                    new Vector3(0.20f, 0.36f, 0.44f), new Vector3(-0.44f, 0.34f, 0.02f),
                };
                foreach (Vector3 spot in berrySpots)
                    Visual(PrimitiveType.Sphere, "Berry", berriesRoot, spot,
                        Vector3.one * 0.09f, Vector3.zero, berry);

                var harvestable = root.AddComponent<HarvestableFoliage>();
                WireHarvestable(harvestable, definition, berriesRoot.gameObject, canopyRenderers);

                GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
                Debug.Log($"Built foliage prefab 'BerryBush' at {prefabPath}.", prefab);
                return prefab;
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        private static GameObject BuildShrubPrefab(Material leaf, Material stem)
        {
            string prefabPath = $"{PrefabFolder}/Shrub.prefab";
            var existing = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            if (existing != null)
                return existing;

            var root = new GameObject("Shrub");
            try
            {
                var collider = root.AddComponent<SphereCollider>();
                collider.center = new Vector3(0f, 0.3f, 0f);
                collider.radius = 0.35f;

                var canopy = Child("Canopy", root.transform);
                Visual(PrimitiveType.Sphere, "Blob1", canopy, new Vector3(0f, 0.30f, 0f),
                    new Vector3(0.70f, 0.50f, 0.70f), Vector3.zero, leaf);
                Visual(PrimitiveType.Sphere, "Blob2", canopy, new Vector3(0.21f, 0.24f, 0.12f),
                    new Vector3(0.45f, 0.34f, 0.45f), Vector3.zero, leaf);
                Visual(PrimitiveType.Sphere, "Blob3", canopy, new Vector3(-0.18f, 0.27f, -0.10f),
                    new Vector3(0.50f, 0.38f, 0.50f), Vector3.zero, leaf);

                // A couple of dead twigs poking out — reads as scrub, not topiary.
                Visual(PrimitiveType.Cube, "Twig1", canopy, new Vector3(0.10f, 0.42f, 0.02f),
                    new Vector3(0.025f, 0.34f, 0.025f), new Vector3(18f, 15f, -12f), stem);
                Visual(PrimitiveType.Cube, "Twig2", canopy, new Vector3(-0.09f, 0.38f, 0.06f),
                    new Vector3(0.02f, 0.26f, 0.02f), new Vector3(-14f, 40f, 10f), stem);

                // Deliberately NO HarvestableFoliage: the shrub is ambient
                // content — no component, no prompt, no interaction path.

                GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
                Debug.Log($"Built foliage prefab 'Shrub' at {prefabPath}.", prefab);
                return prefab;
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        private static GameObject BuildReedClusterPrefab(
            FoliageDefinition definition, Material reed, Material reedDry)
        {
            string prefabPath = $"{PrefabFolder}/ReedCluster.prefab";
            var existing = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            if (existing != null)
                return existing;

            var root = new GameObject("ReedCluster");
            try
            {
                var collider = root.AddComponent<CapsuleCollider>();
                collider.center = new Vector3(0f, 0.6f, 0f);
                collider.height = 1.2f;
                collider.radius = 0.26f;

                // Tall stalks — the harvestable half, hidden while depleted.
                var stalks = Child("TallStalks", root.transform);
                for (int i = 0; i < 7; i++)
                {
                    float angle = i * 51.4f * Mathf.Deg2Rad;
                    float radius = 0.08f + (i % 3) * 0.055f;
                    float height = 0.52f + (i % 4) * 0.07f; // capsule scaleY: 2 units tall at 1
                    Visual(PrimitiveType.Capsule, $"Stalk{i + 1}", stalks,
                        new Vector3(Mathf.Cos(angle) * radius, height, Mathf.Sin(angle) * radius),
                        new Vector3(0.045f, height, 0.045f),
                        new Vector3((i % 3 - 1) * 6f, 0f, (i % 2 == 0 ? 1f : -1f) * 5f), reed);
                }

                // Stubble — what a cut cluster leaves behind. Always present
                // (buried inside the stalks while grown), and the depleted
                // material swap turns it dry once picked.
                var stubble = Child("Stubble", root.transform);
                var stubbleRenderers = new Renderer[5];
                for (int i = 0; i < 5; i++)
                {
                    float angle = (i * 72f + 25f) * Mathf.Deg2Rad;
                    float radius = 0.06f + (i % 2) * 0.05f;
                    stubbleRenderers[i] = Visual(PrimitiveType.Capsule, $"Stub{i + 1}", stubble,
                        new Vector3(Mathf.Cos(angle) * radius, 0.08f, Mathf.Sin(angle) * radius),
                        new Vector3(0.04f, 0.08f, 0.04f), Vector3.zero, reed);
                }

                var harvestable = root.AddComponent<HarvestableFoliage>();
                WireHarvestable(harvestable, definition, stalks.gameObject, stubbleRenderers);

                GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
                Debug.Log($"Built foliage prefab 'ReedCluster' at {prefabPath}.", prefab);
                return prefab;
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        private static Transform Child(string childName, Transform parent)
        {
            Transform child = new GameObject(childName).transform;
            child.SetParent(parent, false);
            return child;
        }

        private static Renderer Visual(
            PrimitiveType type, string visualName, Transform parent,
            Vector3 localPosition, Vector3 localScale, Vector3 localEuler, Material material)
        {
            GameObject visual = GameObject.CreatePrimitive(type);
            visual.name = visualName;

            // The root collider is the plant's only collider — same rule as
            // the creature/player rigs (per-visual colliders would clutter
            // the aim ray and physics).
            Object.DestroyImmediate(visual.GetComponent<Collider>());

            Transform t = visual.transform;
            t.SetParent(parent, false);
            t.localPosition = localPosition;
            t.localRotation = Quaternion.Euler(localEuler);
            t.localScale = localScale;

            var renderer = visual.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            return renderer;
        }

        private static void WireHarvestable(
            HarvestableFoliage harvestable, FoliageDefinition definition,
            GameObject yieldVisualRoot, Renderer[] depletionSwapRenderers)
        {
            var serialized = new SerializedObject(harvestable);
            serialized.FindProperty("definition").objectReferenceValue = definition;
            serialized.FindProperty("yieldVisualRoot").objectReferenceValue = yieldVisualRoot;

            SerializedProperty list = serialized.FindProperty("depletionSwapRenderers");
            list.arraySize = depletionSwapRenderers.Length;
            for (int i = 0; i < depletionSwapRenderers.Length; i++)
                list.GetArrayElementAtIndex(i).objectReferenceValue = depletionSwapRenderers[i];

            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        // ------------------------------------------------------------------
        // Items, materials, definition fields
        // ------------------------------------------------------------------

        private static ItemDefinition CreateItem(
            string id, string displayName, string description,
            ExampleContentCreator.PixelFunc iconPixel, int maxStack, float weightKg)
        {
            string path = $"{ItemFolder}/{ToAssetName(id)}.asset";
            ItemDefinition item = ExampleContentCreator.CreateOrLoad<ItemDefinition>(path, out bool created);
            if (!created)
                return item;

            Sprite icon = ExampleContentCreator.LoadSprite(
                ExampleContentCreator.CreateTexture($"{IconFolder}/icon_{id}.png", 64, iconPixel, asSprite: true));
            ExampleContentCreator.SetItemFields(item, id, displayName, description,
                icon, ItemCategory.Resource, maxStack, weightKg);
            return item;
        }

        private static void MakeEdible(ItemDefinition item, float hungerRestore, float thirstRestore)
        {
            if (item == null)
                return;

            var serialized = new SerializedObject(item);
            if ((ItemCategory)serialized.FindProperty("category").intValue == ItemCategory.Consumable)
                return; // pre-existing, already configured

            serialized.FindProperty("category").intValue = (int)ItemCategory.Consumable;
            serialized.FindProperty("hungerRestore").floatValue = hungerRestore;
            serialized.FindProperty("thirstRestore").floatValue = thirstRestore;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private static Material GetOrCreateMaterial(string materialName, Color color)
        {
            string path = $"{MaterialFolder}/{materialName}.mat";
            var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (existing != null)
                return existing;

            Shader lit = GraphicsSettings.currentRenderPipeline != null
                ? GraphicsSettings.currentRenderPipeline.defaultShader
                : Shader.Find("Standard");

            var material = new Material(lit) { name = materialName, color = color };
            if (material.HasProperty("_Smoothness"))
                material.SetFloat("_Smoothness", 0f);

            AssetDatabase.CreateAsset(material, path);
            return material;
        }

        private static void SetFoliageFields(
            FoliageDefinition definition, string id, string displayName, FoliageCategory category,
            GameObject prefab, ItemDefinition yieldItem, int yieldCountMin, int yieldCountMax,
            float regrowHours, Material depletedMaterial, FoliageSurface surface, float spawnWeight)
        {
            var serialized = new SerializedObject(definition);
            serialized.FindProperty("id").stringValue = id;
            serialized.FindProperty("displayName").stringValue = displayName;
            serialized.FindProperty("category").intValue = (int)category;
            serialized.FindProperty("prefab").objectReferenceValue = prefab;
            serialized.FindProperty("yieldItem").objectReferenceValue = yieldItem;
            serialized.FindProperty("yieldCountMin").intValue = yieldCountMin;
            serialized.FindProperty("yieldCountMax").intValue = yieldCountMax;
            serialized.FindProperty("regrowHours").floatValue = regrowHours;
            serialized.FindProperty("depletedMaterial").objectReferenceValue = depletedMaterial;
            serialized.FindProperty("surface").intValue = (int)surface;
            serialized.FindProperty("spawnWeight").floatValue = spawnWeight;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        /// <summary>Fills a missing prefab reference on an existing definition (deleted/re-created prefab) without touching hand-tuned fields.</summary>
        private static void EnsurePrefabReference(FoliageDefinition definition, GameObject prefab)
        {
            if (definition.Prefab != null || prefab == null)
                return;

            var serialized = new SerializedObject(definition);
            serialized.FindProperty("prefab").objectReferenceValue = prefab;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        /// <summary>lowercase_id → PascalCase asset name (plant_fiber → PlantFiber), matching the content-set convention.</summary>
        private static string ToAssetName(string id)
        {
            string[] parts = id.Split('_');
            var builder = new System.Text.StringBuilder();
            foreach (string part in parts)
            {
                if (part.Length == 0)
                    continue;
                builder.Append(char.ToUpperInvariant(part[0]));
                if (part.Length > 1)
                    builder.Append(part, 1, part.Length - 1);
            }

            return builder.ToString();
        }

        // ------------------------------------------------------------------
        // Icon pixels (64 px, transparent background — the icon convention)
        // ------------------------------------------------------------------

        private static Color32 BerriesPixel(int x, int y, int size, System.Random random)
        {
            float u = (x + 0.5f) / size;
            float v = (y + 0.5f) / size;

            // Three overlapping berries with a darker rim, green stems on top.
            Vector2[] centers = { new Vector2(0.37f, 0.40f), new Vector2(0.63f, 0.45f), new Vector2(0.49f, 0.64f) };
            const float radius = 0.165f;

            foreach (Vector2 center in centers)
            {
                float distance = Vector2.Distance(new Vector2(u, v), center);
                if (distance > radius)
                    continue;

                float value = 1f + (float)(random.NextDouble() - 0.5) * 0.12f;
                if (distance > radius - 0.03f)
                    value *= 0.55f; // rim
                else if (u < center.x - 0.04f && v > center.y + 0.04f)
                    value *= 1.25f; // top-left sheen

                return new Color32(
                    (byte)Mathf.Clamp(Mathf.RoundToInt(0.72f * value * 255f), 0, 255),
                    (byte)Mathf.Clamp(Mathf.RoundToInt(0.12f * value * 255f), 0, 255),
                    (byte)Mathf.Clamp(Mathf.RoundToInt(0.16f * value * 255f), 0, 255),
                    255);
            }

            // Stems reaching up from the cluster.
            if (v > 0.72f && v < 0.9f && (Mathf.Abs(u - 0.45f) < 0.02f || Mathf.Abs(u - 0.58f) < 0.02f))
                return new Color32(60, 110, 45, 255);

            return new Color32(0, 0, 0, 0);
        }

        private static Color32 FiberPixel(int x, int y, int size, System.Random random)
        {
            float u = (x + 0.5f) / size;
            float v = (y + 0.5f) / size;

            // A tied bundle of swaying strands.
            bool onStrand = false;
            for (int i = 0; i < 6; i++)
            {
                float strandU = 0.30f + i * 0.08f + Mathf.Sin(v * 7f + i * 1.7f) * 0.025f;
                if (Mathf.Abs(u - strandU) < 0.018f && v > 0.08f && v < 0.92f)
                {
                    onStrand = true;
                    break;
                }
            }

            if (!onStrand)
                return new Color32(0, 0, 0, 0);

            // The tie band across the middle reads darker.
            float value = 1f + (float)(random.NextDouble() - 0.5) * 0.15f;
            if (v > 0.44f && v < 0.53f)
                value *= 0.55f;

            return new Color32(
                (byte)Mathf.Clamp(Mathf.RoundToInt(0.76f * value * 255f), 0, 255),
                (byte)Mathf.Clamp(Mathf.RoundToInt(0.66f * value * 255f), 0, 255),
                (byte)Mathf.Clamp(Mathf.RoundToInt(0.38f * value * 255f), 0, 255),
                255);
        }
    }
}
