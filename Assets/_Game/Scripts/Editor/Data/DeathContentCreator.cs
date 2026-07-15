using IslandGame.Building;
using IslandGame.Data.Building;
using IslandGame.Inventory;
using IslandGame.Player;
using IslandGame.Texturing;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using static IslandGame.EditorTools.Data.BaseContentSetGenerator;

namespace IslandGame.EditorTools.Data
{
    /// <summary>
    /// Death-phase content: the Gravestone piece (the chest pattern — its own
    /// InventorySystem + ChestBehavior on a BuildingPiece prefab, so looting,
    /// deconstruction and SAVE PERSISTENCE all come from existing systems)
    /// and the default DropBackpackDeathPenalty policy asset the respawn
    /// controller references.
    ///
    /// The gravestone deliberately has NO recipe and NO placer item — it is
    /// system-spawned only (dying is how you "craft" one). 36 slots so even
    /// the harsher drop-hotbar-too policy variant fits a full 9+27 inventory.
    ///
    /// Idempotent like every content creator: existing assets are left
    /// untouched, only missing pieces are created.
    /// </summary>
    public static class DeathContentCreator
    {
        private const string PlayerContentFolder = "Assets/_Game/Content/Player";
        private const string PolicyAssetPath = PlayerContentFolder + "/DropBackpackDeathPenalty.asset";

        [MenuItem("Island Game/Data/Create Death Content")]
        public static void Create()
        {
            DefinitionDatabaseSync.EnsureFolderExists(PlayerContentFolder);
            DefinitionDatabaseSync.EnsureFolderExists(PieceFolder);
            DefinitionDatabaseSync.EnsureFolderExists(PrefabFolder);
            DefinitionDatabaseSync.EnsureFolderExists(MaterialFolder);

            // --- Gravestone prefab + piece ----------------------------------
            Material stone = GetOrCreateMaterial("GravestoneStone", new Color(0.42f, 0.43f, 0.46f));
            GameObject prefab = BuildGravestonePrefab(stone);
            CreateGravestonePiece(prefab);

            // --- Default penalty policy asset --------------------------------
            var policy = AssetDatabase.LoadAssetAtPath<DropBackpackDeathPenalty>(PolicyAssetPath);
            if (policy == null)
            {
                policy = ScriptableObject.CreateInstance<DropBackpackDeathPenalty>();
                AssetDatabase.CreateAsset(policy, PolicyAssetPath);
                Debug.Log($"Created default death penalty policy at {PolicyAssetPath}.");
            }

            AssetDatabase.SaveAssets();
            DefinitionDatabaseSync.SyncAll();

            Debug.Log(
                "Death content ready: Gravestone piece (36-slot chest, system-spawned only) and the " +
                "DropBackpackDeathPenalty policy asset. Wire the scene with Island Game/World/Add Respawn System.");
        }

        /// <summary>The policy asset, for the scene builder to wire (created if missing).</summary>
        public static DropBackpackDeathPenalty LoadOrCreatePolicy()
        {
            var policy = AssetDatabase.LoadAssetAtPath<DropBackpackDeathPenalty>(PolicyAssetPath);
            if (policy == null)
            {
                Create();
                policy = AssetDatabase.LoadAssetAtPath<DropBackpackDeathPenalty>(PolicyAssetPath);
            }

            return policy;
        }

        // ------------------------------------------------------------------
        // Gravestone (the Chest prefab pattern, in stone)
        // ------------------------------------------------------------------

        private static GameObject BuildGravestonePrefab(Material stone)
        {
            string assetPath = $"{PrefabFolder}/Gravestone.prefab";
            var existing = AssetDatabase.LoadAssetAtPath<GameObject>(assetPath);
            if (existing != null)
                return existing;

            var root = new GameObject("Gravestone");
            try
            {
                // Low plinth, upright slab, rounded-top cap (a 45° cube) — an
                // unmistakable headstone from primitives. Primitive colliders
                // stay ON (building pieces keep per-box colliders — the chest
                // pattern; they're the interact/aim surface).
                Box(root, "Plinth", new Vector3(0f, 0.09f, 0f), new Vector3(0.85f, 0.18f, 0.55f), Vector3.zero, stone);
                Box(root, "Slab", new Vector3(0f, 0.62f, 0f), new Vector3(0.62f, 0.9f, 0.16f), Vector3.zero, stone);
                Box(root, "Cap", new Vector3(0f, 1.07f, 0f), new Vector3(0.44f, 0.44f, 0.155f), new Vector3(0f, 0f, 45f), stone);

                // The chest half: storage sized for a full player inventory.
                var storage = root.AddComponent<InventorySystem>();
                var serializedStorage = new SerializedObject(storage);
                serializedStorage.FindProperty("hotbarSize").intValue = 1;
                serializedStorage.FindProperty("backpackSize").intValue = 35;
                serializedStorage.ApplyModifiedPropertiesWithoutUndo();

                root.AddComponent<ChestBehavior>();

                var piece = root.AddComponent<BuildingPiece>();
                var serializedPiece = new SerializedObject(piece);
                serializedPiece.FindProperty("pieceId").stringValue = "gravestone";
                serializedPiece.ApplyModifiedPropertiesWithoutUndo();

                GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, assetPath);
                Debug.Log($"Built gravestone prefab at {assetPath}.", prefab);
                return prefab;
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        private static void CreateGravestonePiece(GameObject prefab)
        {
            string path = $"{PieceFolder}/Gravestone.asset";
            BuildingPieceDefinition piece = ExampleContentCreator.CreateOrLoad<BuildingPieceDefinition>(path, out bool created);
            if (!created)
                return;

            Sprite icon = Icon("piece_gravestone", IconShape.RoundedSquare,
                new Color(0.45f, 0.46f, 0.5f), new Color(0.2f, 0.2f, 0.24f));

            var serialized = new SerializedObject(piece);
            serialized.FindProperty("id").stringValue = "gravestone";
            serialized.FindProperty("displayName").stringValue = "Gravestone";
            serialized.FindProperty("description").stringValue =
                "Where you fell. Holds everything the fall shook loose — interact empty-handed to take it back.";
            serialized.FindProperty("category").intValue = (int)BuildingCategory.Functional;
            serialized.FindProperty("prefab").objectReferenceValue = prefab;
            serialized.FindProperty("icon").objectReferenceValue = icon;
            serialized.FindProperty("maxHealth").floatValue = 200f;
            serialized.FindProperty("materialCost").arraySize = 0;
            serialized.FindProperty("sockets").arraySize = 0;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void Box(
            GameObject parent, string name, Vector3 center, Vector3 size, Vector3 euler, Material material)
        {
            GameObject child = GameObject.CreatePrimitive(PrimitiveType.Cube);
            child.name = name;
            child.transform.SetParent(parent.transform, false);
            child.transform.localPosition = center;
            child.transform.localEulerAngles = euler;
            child.transform.localScale = size;
            child.GetComponent<MeshRenderer>().sharedMaterial = material;
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
    }
}
