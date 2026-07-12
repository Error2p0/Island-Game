using IslandGame.Data.Building;
using IslandGame.Data.Creatures;
using IslandGame.Data.Items;
using IslandGame.Data.World;
using UnityEditor;
using UnityEngine;

namespace IslandGame.EditorTools.Data
{
    /// <summary>
    /// Creates the example StructureTemplates: a guarded Ruined Watchtower
    /// (Inland), a Wrecked Dock (Coast) and an Abandoned Camp (Inland) — all
    /// composed of EXISTING building pieces (foundation/wall/floor/door/
    /// campfire/workbench/chest) with per-piece omit chances doing the
    /// "ruined" work, chest loot in the shared LootTableEntry format, and one
    /// stalker guard nest proving the spawner hookup. Idempotent: existing
    /// template assets are never overwritten.
    /// </summary>
    public static class StructureContentCreator
    {
        private const string StructureFolder = "Assets/_Game/Content/Structures";

        // Authoring shorthand used by the layout tables below.
        private struct PieceSpec
        {
            public string pieceId;
            public Vector3 position;
            public float yaw;
            public float omit;

            public PieceSpec(string pieceId, Vector3 position, float yaw = 0f, float omit = 0f)
            {
                this.pieceId = pieceId;
                this.position = position;
                this.yaw = yaw;
                this.omit = omit;
            }
        }

        private struct LootSpec
        {
            public string itemId;
            public int min;
            public int max;
            public float chance;

            public LootSpec(string itemId, int min, int max, float chance)
            {
                this.itemId = itemId;
                this.min = min;
                this.max = max;
                this.chance = chance;
            }
        }

        [MenuItem("Island Game/Data/Create Example Structures")]
        public static void Create()
        {
            DefinitionDatabaseSync.EnsureFolderExists(StructureFolder);

            var pieceDatabase = AssetDatabase.LoadAssetAtPath<BuildingPieceDatabase>(
                $"{DefinitionDatabaseSync.DatabaseFolder}/BuildingPieceDatabase.asset");
            var itemDatabase = AssetDatabase.LoadAssetAtPath<ItemDatabase>(
                $"{DefinitionDatabaseSync.DatabaseFolder}/ItemDatabase.asset");
            var creatureDatabase = AssetDatabase.LoadAssetAtPath<CreatureDatabase>(
                $"{DefinitionDatabaseSync.DatabaseFolder}/CreatureDatabase.asset");

            if (pieceDatabase == null || pieceDatabase.Count == 0 || itemDatabase == null)
            {
                Debug.LogError(
                    "Structures: BuildingPieceDatabase/ItemDatabase missing or empty — run the content creators " +
                    "(Create Example Building Pieces, Generate Base Content Set) first.");
                return;
            }

            // ---------------- Ruined Watchtower (Inland, GUARDED) ----------
            CreateTemplate(pieceDatabase, itemDatabase, creatureDatabase,
                "ruined_tower", "Ruined Watchtower",
                "A collapsed stone-and-timber watch post. Something nests in it now.",
                StructureSurface.Inland, footprintRadius: 5f, maxHeightVariance: 2, spawnWeight: 1f,
                coastSeawardExtent: 8f,
                pieces: new[]
                {
                    // Foundations — a 4×4 m stone base, always intact.
                    new PieceSpec("foundation_stone", new Vector3(-1f, 0f, -1f)),
                    new PieceSpec("foundation_stone", new Vector3(1f, 0f, -1f)),
                    new PieceSpec("foundation_stone", new Vector3(-1f, 0f, 1f)),
                    new PieceSpec("foundation_stone", new Vector3(1f, 0f, 1f)),
                    // Ground walls — the ruin rolls eat some of them.
                    new PieceSpec("wall_wood", new Vector3(-1f, 0.5f, 2f), 0f, 0.2f),
                    new PieceSpec("wall_wood", new Vector3(1f, 0.5f, 2f), 0f, 0.35f),
                    new PieceSpec("wall_wood", new Vector3(-1f, 0.5f, -2f), 0f, 0.35f),
                    new PieceSpec("door_frame_wood", new Vector3(1f, 0.5f, -2f)),
                    new PieceSpec("wall_wood", new Vector3(2f, 0.5f, -1f), 90f, 0.2f),
                    new PieceSpec("wall_wood", new Vector3(2f, 0.5f, 1f), 90f, 0.35f),
                    new PieceSpec("wall_wood", new Vector3(-2f, 0.5f, -1f), 90f, 0.35f),
                    new PieceSpec("wall_wood", new Vector3(-2f, 0.5f, 1f), 90f, 0.2f),
                    // Partial upper floor and heavily-eroded upper walls.
                    new PieceSpec("floor_wood", new Vector3(-1f, 2.5f, -1f), 0f, 0.3f),
                    new PieceSpec("floor_wood", new Vector3(1f, 2.5f, -1f), 0f, 0.5f),
                    new PieceSpec("floor_wood", new Vector3(-1f, 2.5f, 1f), 0f, 0.5f),
                    new PieceSpec("floor_wood", new Vector3(1f, 2.5f, 1f), 0f, 0.3f),
                    new PieceSpec("wall_wood", new Vector3(-1f, 2.5f, 2f), 0f, 0.55f),
                    new PieceSpec("wall_wood", new Vector3(1f, 2.5f, 2f), 0f, 0.7f),
                    new PieceSpec("wall_wood", new Vector3(2f, 2.5f, -1f), 90f, 0.55f),
                    new PieceSpec("wall_wood", new Vector3(-2f, 2.5f, 1f), 90f, 0.7f),
                },
                chests: new[]
                {
                    ("chest", new Vector3(0.4f, 0.5f, 0.6f), 25f, new[]
                    {
                        new LootSpec("stone_pickaxe", 1, 1, 0.35f),
                        new LootSpec("arrow", 4, 10, 0.8f),
                        new LootSpec("torch", 1, 2, 0.7f),
                        new LootSpec("cooked_berries", 2, 4, 0.8f),
                        new LootSpec("bone", 1, 3, 0.5f),
                    }),
                },
                guards: new[]
                {
                    // Nesting stalkers: always present, not night-gated — the
                    // guarded-ruins feel by day too.
                    ("stalker", new Vector3(0f, 0f, 0f), 2, 6f, false),
                });

            // ---------------- Wrecked Dock (Coast, unguarded) --------------
            CreateTemplate(pieceDatabase, itemDatabase, creatureDatabase,
                "ruined_dock", "Wrecked Dock",
                "A storm-broken pier reaching into the shallows. Whoever built it left in a hurry.",
                StructureSurface.Coast, footprintRadius: 4f, maxHeightVariance: 3, spawnWeight: 1f,
                coastSeawardExtent: 9f,
                pieces: new[]
                {
                    new PieceSpec("foundation_stone", new Vector3(0f, -0.55f, 0f)),
                    // The pier marches seaward (+Z), losing planks as it goes.
                    new PieceSpec("floor_wood", new Vector3(0f, 0f, 1f)),
                    new PieceSpec("floor_wood", new Vector3(0f, 0f, 3f), 0f, 0.25f),
                    new PieceSpec("floor_wood", new Vector3(0f, 0f, 5f), 0f, 0.3f),
                    new PieceSpec("floor_wood", new Vector3(0f, 0f, 7f), 0f, 0.35f),
                    new PieceSpec("floor_wood", new Vector3(2f, 0f, 5f), 0f, 0.5f), // collapsed boat slip
                    new PieceSpec("door_frame_wood", new Vector3(0f, 0f, 1f), 0f, 0.5f), // gantry skeleton
                },
                chests: new[]
                {
                    ("chest", new Vector3(0f, 0.05f, 7f), 90f, new[]
                    {
                        new LootSpec("wood_plank", 6, 14, 1f),
                        new LootSpec("log", 2, 5, 0.7f),
                        new LootSpec("torch", 1, 2, 0.6f),
                        new LootSpec("arrow", 5, 12, 0.5f),
                        new LootSpec("bucket", 1, 1, 0.3f),
                    }),
                },
                guards: null);

            // ---------------- Abandoned Camp (Inland, unguarded) -----------
            CreateTemplate(pieceDatabase, itemDatabase, creatureDatabase,
                "abandoned_camp", "Abandoned Camp",
                "A cold firepit and scattered gear. The owners are days gone — or worse.",
                StructureSurface.Inland, footprintRadius: 4f, maxHeightVariance: 1, spawnWeight: 1.2f,
                coastSeawardExtent: 8f,
                pieces: new[]
                {
                    new PieceSpec("campfire", new Vector3(0f, 0f, 0f)),
                    new PieceSpec("workbench", new Vector3(2.4f, 0f, 0.3f), 90f, 0.3f),
                },
                chests: new[]
                {
                    ("chest", new Vector3(-2f, 0f, 0.8f), -30f, new[]
                    {
                        new LootSpec("cooked_berries", 2, 5, 0.9f),
                        new LootSpec("raw_meat", 1, 2, 0.5f),
                        new LootSpec("log", 2, 4, 0.7f),
                        new LootSpec("hide", 1, 2, 0.4f),
                        new LootSpec("torch", 1, 1, 0.5f),
                    }),
                },
                guards: null);

            AssetDatabase.SaveAssets();
            DefinitionDatabaseSync.SyncAll();

            Debug.Log(
                "Example structures ready: Ruined Watchtower (guarded by stalkers), Wrecked Dock (coast), " +
                "Abandoned Camp — real building pieces, shared-format chest loot, database synced. " +
                "Existing template assets were left untouched.");
            EditorGUIUtility.PingObject(AssetDatabase.LoadAssetAtPath<Object>(StructureFolder));
        }

        // ------------------------------------------------------------------
        // Asset writing
        // ------------------------------------------------------------------

        private static void CreateTemplate(
            BuildingPieceDatabase pieceDatabase, ItemDatabase itemDatabase, CreatureDatabase creatureDatabase,
            string id, string displayName, string description,
            StructureSurface surface, float footprintRadius, int maxHeightVariance, float spawnWeight,
            float coastSeawardExtent,
            PieceSpec[] pieces,
            (string pieceId, Vector3 position, float yaw, LootSpec[] loot)[] chests,
            (string creatureId, Vector3 offset, int population, float radius, bool nightOnly)[] guards)
        {
            string path = $"{StructureFolder}/{ToAssetName(id)}.asset";
            StructureTemplate template = ExampleContentCreator.CreateOrLoad<StructureTemplate>(path, out bool created);
            if (!created)
                return; // never overwrite hand-tuned layouts

            var serialized = new SerializedObject(template);
            serialized.FindProperty("id").stringValue = id;
            serialized.FindProperty("displayName").stringValue = displayName;
            serialized.FindProperty("description").stringValue = description;
            serialized.FindProperty("surface").intValue = (int)surface;
            serialized.FindProperty("footprintRadius").floatValue = footprintRadius;
            serialized.FindProperty("maxHeightVariance").intValue = maxHeightVariance;
            serialized.FindProperty("spawnWeight").floatValue = spawnWeight;
            serialized.FindProperty("coastSeawardExtent").floatValue = coastSeawardExtent;

            SerializedProperty pieceList = serialized.FindProperty("pieces");
            pieceList.arraySize = pieces.Length;
            for (int i = 0; i < pieces.Length; i++)
            {
                SerializedProperty element = pieceList.GetArrayElementAtIndex(i);
                element.FindPropertyRelative("piece").objectReferenceValue = FindPiece(pieceDatabase, id, pieces[i].pieceId);
                element.FindPropertyRelative("localPosition").vector3Value = pieces[i].position;
                element.FindPropertyRelative("localYawDegrees").floatValue = pieces[i].yaw;
                element.FindPropertyRelative("omitChance").floatValue = pieces[i].omit;
            }

            SerializedProperty chestList = serialized.FindProperty("chests");
            chestList.arraySize = chests.Length;
            for (int i = 0; i < chests.Length; i++)
            {
                SerializedProperty element = chestList.GetArrayElementAtIndex(i);
                element.FindPropertyRelative("chestPiece").objectReferenceValue = FindPiece(pieceDatabase, id, chests[i].pieceId);
                element.FindPropertyRelative("localPosition").vector3Value = chests[i].position;
                element.FindPropertyRelative("localYawDegrees").floatValue = chests[i].yaw;

                SerializedProperty lootList = element.FindPropertyRelative("loot");
                LootSpec[] loot = chests[i].loot;
                lootList.arraySize = loot.Length;
                for (int j = 0; j < loot.Length; j++)
                {
                    SerializedProperty line = lootList.GetArrayElementAtIndex(j);
                    ItemDefinition item = null;
                    if (!itemDatabase.TryGet(loot[j].itemId, out item))
                        Debug.LogWarning($"Structure '{id}': loot item '{loot[j].itemId}' not found — line will be empty.");

                    line.FindPropertyRelative("item").objectReferenceValue = item;
                    line.FindPropertyRelative("countMin").intValue = loot[j].min;
                    line.FindPropertyRelative("countMax").intValue = loot[j].max;
                    line.FindPropertyRelative("dropChance").floatValue = loot[j].chance;
                }
            }

            SerializedProperty spawnerList = serialized.FindProperty("spawners");
            spawnerList.arraySize = guards?.Length ?? 0;
            for (int i = 0; i < spawnerList.arraySize; i++)
            {
                SerializedProperty element = spawnerList.GetArrayElementAtIndex(i);
                CreatureDefinition creature = null;
                if (creatureDatabase == null || !creatureDatabase.TryGet(guards[i].creatureId, out creature))
                    Debug.LogWarning($"Structure '{id}': guard creature '{guards[i].creatureId}' not found — run Create Example Creatures first.");

                element.FindPropertyRelative("creature").objectReferenceValue = creature;
                element.FindPropertyRelative("localOffset").vector3Value = guards[i].offset;
                element.FindPropertyRelative("maxPopulation").intValue = guards[i].population;
                element.FindPropertyRelative("spawnRadius").floatValue = guards[i].radius;
                element.FindPropertyRelative("spawnOnlyAtNight").boolValue = guards[i].nightOnly;
            }

            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private static BuildingPieceDefinition FindPiece(BuildingPieceDatabase database, string templateId, string pieceId)
        {
            if (database.TryGet(pieceId, out BuildingPieceDefinition piece))
                return piece;

            Debug.LogError($"Structure '{templateId}': building piece '{pieceId}' not found in the BuildingPieceDatabase.");
            return null;
        }

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
    }
}
