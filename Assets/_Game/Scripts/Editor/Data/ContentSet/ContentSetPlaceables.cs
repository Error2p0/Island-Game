using IslandGame.Building;
using IslandGame.Crafting;
using IslandGame.Data.Building;
using IslandGame.Data.Crafting;
using IslandGame.Inventory;
using IslandGame.Texturing;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using static IslandGame.EditorTools.Data.BaseContentSetGenerator;

namespace IslandGame.EditorTools.Data
{
    /// <summary>
    /// The placeable half of the base content set: the structural pieces that
    /// complete a buildable house (half-wall, window, stairs, pillar, a REAL
    /// hinged door that snaps into the Phase 1 door frame's doorway socket,
    /// fence) and the functional set (furnace = campfire-pattern fuel/fire +
    /// a Furnace station marker; chest = its own InventorySystem; bed, boat,
    /// torch stand). All prefabs are primitive-built, collidable, stamped
    /// with their piece IDs — the same contract every earlier piece follows.
    /// </summary>
    internal static class ContentSetPlaceables
    {
        private static Material wood;
        private static Material woodDark;
        private static Material stone;
        private static Material fabric;

        public static void Create()
        {
            wood = MaterialAsset("BuildingWood", new Color(0.72f, 0.55f, 0.35f));
            woodDark = MaterialAsset("BuildingWoodDark", new Color(0.52f, 0.40f, 0.26f));
            stone = MaterialAsset("BuildingStone", new Color(0.55f, 0.55f, 0.58f));
            fabric = MaterialAsset("BuildingFabric", new Color(0.78f, 0.72f, 0.6f));

            CreateStructuralPieces();
            CreateFunctionalPieces();
        }

        // --------------------------------------------------------------
        // Structural wood set
        // --------------------------------------------------------------

        private static void CreateStructuralPieces()
        {
            // Half wall — same mating rules as the full wall, one meter tall.
            GameObject halfWall = Prefab("HalfWall_Wood", "half_wall_wood", root =>
            {
                AddBox(root, "Panel", new Vector3(0f, 0.5f, 0f), new Vector3(2f, 1f, 0.2f), Vector3.zero, wood);
            });
            Piece("half_wall_wood", "Wooden Half-Wall", "A 1 m parapet — railings, window sills, battlements.",
                BuildingCategory.Wall, halfWall, 150f, new Color32(184, 140, 89, 255), new[]
                {
                    new SnapSocket("bottom", Vector3.zero, new Vector3(0f, 180f, 0f), SnapTags.WallBottom, SnapTags.FoundationTop, SnapTags.WallTop),
                    new SnapSocket("top", new Vector3(0f, 1f, 0f), Vector3.zero, SnapTags.WallTop, SnapTags.WallBottom),
                    new SnapSocket("side_east", new Vector3(1f, 0.5f, 0f), new Vector3(0f, 90f, 0f), SnapTags.WallSide, SnapTags.WallSide),
                    new SnapSocket("side_west", new Vector3(-1f, 0.5f, 0f), new Vector3(0f, 270f, 0f), SnapTags.WallSide, SnapTags.WallSide),
                });

            // Window — a wall with a 1×0.9 m opening.
            GameObject window = Prefab("Window_Wood", "window_wood", root =>
            {
                AddBox(root, "Sill", new Vector3(0f, 0.4f, 0f), new Vector3(2f, 0.8f, 0.2f), Vector3.zero, wood);
                AddBox(root, "Header", new Vector3(0f, 1.85f, 0f), new Vector3(2f, 0.3f, 0.2f), Vector3.zero, wood);
                AddBox(root, "JambLeft", new Vector3(-0.75f, 1.25f, 0f), new Vector3(0.5f, 0.9f, 0.2f), Vector3.zero, wood);
                AddBox(root, "JambRight", new Vector3(0.75f, 1.25f, 0f), new Vector3(0.5f, 0.9f, 0.2f), Vector3.zero, wood);
            });
            Piece("window_wood", "Wooden Window", "A wall piece with a view — and an arrow slit when it matters.",
                BuildingCategory.Window, window, 220f, new Color32(199, 170, 120, 255), WallSockets());

            // Stairs — four chunky steps climbing a full 2 m story.
            GameObject stairs = Prefab("Stairs_Wood", "stairs_wood", root =>
            {
                for (int i = 0; i < 4; i++)
                {
                    AddBox(root, $"Step{i}",
                        new Vector3(0f, 0.25f + i * 0.5f, -0.75f + i * 0.5f),
                        new Vector3(2f, 0.5f, 0.5f), Vector3.zero, wood);
                }
            });
            Piece("stairs_wood", "Wooden Stairs", "Climbs one full story over a 2 m run.",
                BuildingCategory.Stair, stairs, 250f, new Color32(170, 130, 85, 255), new[]
                {
                    new SnapSocket("bottom_front", new Vector3(0f, 0f, -1f), new Vector3(0f, 180f, 0f), SnapTags.WallBottom, SnapTags.FoundationTop, SnapTags.FloorEdge),
                });

            // Support pillar — stacks, and floors/roofs rest on its top.
            GameObject pillar = Prefab("Pillar_Wood", "pillar_wood", root =>
            {
                AddBox(root, "Post", new Vector3(0f, 1f, 0f), new Vector3(0.3f, 2f, 0.3f), Vector3.zero, woodDark);
            });
            Piece("pillar_wood", "Support Pillar", "A 2 m post. Holds floors where walls would be in the way.",
                BuildingCategory.Support, pillar, 300f, new Color32(140, 105, 70, 255), new[]
                {
                    new SnapSocket("bottom", Vector3.zero, new Vector3(0f, 180f, 0f), SnapTags.WallBottom, SnapTags.FoundationTop, SnapTags.WallTop),
                    new SnapSocket("top", new Vector3(0f, 2f, 0f), Vector3.zero, SnapTags.WallTop, SnapTags.WallBottom, SnapTags.FloorEdge, SnapTags.RoofBottom),
                });

            // Door — the leaf. Root AT the hinge; the door_hinge socket mates
            // the frame's doorway socket (authored back in Building Phase 1).
            GameObject door = Prefab("Door_Wood", "door_wood", root =>
            {
                AddBox(root, "Leaf", new Vector3(0.5f, 0.85f, 0f), new Vector3(0.96f, 1.7f, 0.08f), Vector3.zero, woodDark);
                AddBox(root, "Handle", new Vector3(0.86f, 0.85f, 0.08f), new Vector3(0.08f, 0.08f, 0.08f), Vector3.zero, stone);
                root.AddComponent<DoorBehavior>();
            });
            Piece("door_wood", "Wooden Door", "Hangs in a door frame's hinge socket. Interact to swing.",
                BuildingCategory.Door, door, 180f, new Color32(158, 116, 72, 255), new[]
                {
                    new SnapSocket("hinge", Vector3.zero, new Vector3(0f, 180f, 0f), SnapTags.DoorHinge, SnapTags.Doorway),
                });

            // Fence — posts and rails; chains sideways with its own tag.
            GameObject fence = Prefab("Fence_Wood", "fence_wood", root =>
            {
                AddBox(root, "PostLeft", new Vector3(-0.94f, 0.5f, 0f), new Vector3(0.12f, 1f, 0.12f), Vector3.zero, woodDark);
                AddBox(root, "PostRight", new Vector3(0.94f, 0.5f, 0f), new Vector3(0.12f, 1f, 0.12f), Vector3.zero, woodDark);
                AddBox(root, "RailTop", new Vector3(0f, 0.85f, 0f), new Vector3(2f, 0.12f, 0.08f), Vector3.zero, wood);
                AddBox(root, "RailLow", new Vector3(0f, 0.45f, 0f), new Vector3(2f, 0.12f, 0.08f), Vector3.zero, wood);
            });
            Piece("fence_wood", "Wooden Fence", "Keeps things out. Or in. Chains end to end.",
                BuildingCategory.Decoration, fence, 120f, new Color32(150, 115, 75, 255), new[]
                {
                    new SnapSocket("bottom", Vector3.zero, new Vector3(0f, 180f, 0f), SnapTags.WallBottom, SnapTags.FoundationTop),
                    new SnapSocket("side_east", new Vector3(1f, 0.5f, 0f), new Vector3(0f, 90f, 0f), "fence_side", "fence_side"),
                    new SnapSocket("side_west", new Vector3(-1f, 0.5f, 0f), new Vector3(0f, 270f, 0f), "fence_side", "fence_side"),
                });
        }

        // --------------------------------------------------------------
        // Functional pieces
        // --------------------------------------------------------------

        private static void CreateFunctionalPieces()
        {
            // Furnace: the campfire pattern (fuel + fire + light) plus the
            // Furnace station marker that gates the smelting recipes.
            GameObject furnace = Prefab("Furnace", "furnace", root =>
            {
                AddBox(root, "Body", new Vector3(0f, 0.7f, 0f), new Vector3(1.2f, 1.4f, 1.2f), Vector3.zero, stone);
                AddBox(root, "Chimney", new Vector3(0f, 1.65f, 0f), new Vector3(0.4f, 0.5f, 0.4f), Vector3.zero, stone);
                AddBox(root, "Mouth", new Vector3(0f, 0.35f, -0.61f), new Vector3(0.5f, 0.5f, 0.06f), Vector3.zero, woodDark);

                var lightObject = new GameObject("FireLight");
                lightObject.transform.SetParent(root.transform, false);
                lightObject.transform.localPosition = new Vector3(0f, 0.4f, -0.75f);
                var fireLight = lightObject.AddComponent<Light>();
                fireLight.type = LightType.Point;
                fireLight.color = new Color(1f, 0.55f, 0.2f);
                fireLight.range = 6f;
                fireLight.intensity = 1.8f;
                fireLight.shadows = LightShadows.None;
                fireLight.enabled = false;

                var marker = root.AddComponent<CraftingStationMarker>();
                var serializedMarker = new SerializedObject(marker);
                serializedMarker.FindProperty("stationType").intValue = (int)CraftingStationType.Furnace;
                serializedMarker.ApplyModifiedPropertiesWithoutUndo();

                var behavior = root.AddComponent<CampfireBehavior>();
                var serializedBehavior = new SerializedObject(behavior);
                serializedBehavior.FindProperty("fireLight").objectReferenceValue = fireLight;
                SerializedProperty fuel = serializedBehavior.FindProperty("acceptedFuel");
                fuel.arraySize = 0;
                AddFuel(fuel, "log");
                AddFuel(fuel, "wood_plank");
                serializedBehavior.ApplyModifiedPropertiesWithoutUndo();
            });
            Piece("furnace", "Furnace", "Stone smelter. Feed it wood, stand near it to smelt ore into bars.",
                BuildingCategory.Functional, furnace, 400f, new Color32(140, 140, 148, 255), null);

            // Chest: its own InventorySystem (12 slots) + deposit/withdraw interact.
            GameObject chest = Prefab("Chest", "chest", root =>
            {
                AddBox(root, "Base", new Vector3(0f, 0.28f, 0f), new Vector3(0.9f, 0.55f, 0.6f), Vector3.zero, wood);
                AddBox(root, "Lid", new Vector3(0f, 0.65f, 0f), new Vector3(0.9f, 0.2f, 0.6f), Vector3.zero, woodDark);
                AddBox(root, "Clasp", new Vector3(0f, 0.5f, -0.31f), new Vector3(0.12f, 0.2f, 0.04f), Vector3.zero, stone);

                var storage = root.AddComponent<InventorySystem>();
                var serializedStorage = new SerializedObject(storage);
                serializedStorage.FindProperty("hotbarSize").intValue = 1;
                serializedStorage.FindProperty("backpackSize").intValue = 11;
                serializedStorage.ApplyModifiedPropertiesWithoutUndo();

                root.AddComponent<ChestBehavior>();
            });
            Piece("chest", "Chest", "12 slots of storage. Interact with an item to store the stack; empty-handed to take.",
                BuildingCategory.Functional, chest, 150f, new Color32(160, 120, 70, 255), null);

            // Bed: skip-to-morning through the day/night controller.
            GameObject bed = Prefab("Bed", "bed", root =>
            {
                AddBox(root, "Frame", new Vector3(0f, 0.15f, 0f), new Vector3(1.1f, 0.3f, 2.1f), Vector3.zero, woodDark);
                AddBox(root, "Mattress", new Vector3(0f, 0.38f, 0f), new Vector3(1f, 0.16f, 1.9f), Vector3.zero, fabric);
                AddBox(root, "Pillow", new Vector3(0f, 0.5f, 0.75f), new Vector3(0.8f, 0.12f, 0.4f), Vector3.zero, fabric);
                root.AddComponent<BedBehavior>();
            });
            Piece("bed", "Bed", "Sleep through the night (interact after dark).",
                BuildingCategory.Functional, bed, 120f, new Color32(200, 185, 155, 255), null);

            // Torch stand: an always-lit fixture — cheap placed light.
            GameObject torchStand = Prefab("TorchStand", "torch_stand", root =>
            {
                AddBox(root, "Pole", new Vector3(0f, 0.7f, 0f), new Vector3(0.12f, 1.4f, 0.12f), Vector3.zero, woodDark);
                AddBox(root, "Head", new Vector3(0f, 1.48f, 0f), new Vector3(0.18f, 0.18f, 0.18f), Vector3.zero, wood);

                var lightObject = new GameObject("TorchLight");
                lightObject.transform.SetParent(root.transform, false);
                lightObject.transform.localPosition = new Vector3(0f, 1.6f, 0f);
                var light = lightObject.AddComponent<Light>();
                light.type = LightType.Point;
                light.color = new Color(1f, 0.62f, 0.25f);
                light.range = 8f;
                light.intensity = 1.7f;
                light.shadows = LightShadows.None;
            });
            Piece("torch_stand", "Torch Stand", "A standing brand that never goes out. Base lighting.",
                BuildingCategory.Decoration, torchStand, 80f, new Color32(230, 150, 60, 255), null);

            // Boat: rideable — see BoatBehavior for the possession loop.
            GameObject boat = Prefab("Boat", "boat", root =>
            {
                AddBox(root, "Hull", new Vector3(0f, 0.15f, 0f), new Vector3(1.1f, 0.25f, 2.6f), Vector3.zero, woodDark);
                AddBox(root, "SideLeft", new Vector3(-0.55f, 0.4f, 0f), new Vector3(0.12f, 0.35f, 2.6f), Vector3.zero, wood);
                AddBox(root, "SideRight", new Vector3(0.55f, 0.4f, 0f), new Vector3(0.12f, 0.35f, 2.6f), Vector3.zero, wood);
                AddBox(root, "Bow", new Vector3(0f, 0.4f, 1.3f), new Vector3(1.1f, 0.35f, 0.12f), Vector3.zero, wood);
                AddBox(root, "Stern", new Vector3(0f, 0.4f, -1.3f), new Vector3(1.1f, 0.35f, 0.12f), Vector3.zero, wood);
                AddBox(root, "Bench", new Vector3(0f, 0.42f, -0.55f), new Vector3(0.9f, 0.1f, 0.45f), Vector3.zero, wood);

                var seat = new GameObject("Seat");
                seat.transform.SetParent(root.transform, false);
                seat.transform.localPosition = new Vector3(0f, 0.55f, -0.45f);

                var behavior = root.AddComponent<BoatBehavior>();
                var serializedBehavior = new SerializedObject(behavior);
                serializedBehavior.FindProperty("seat").objectReferenceValue = seat.transform;
                serializedBehavior.ApplyModifiedPropertiesWithoutUndo();
            });
            Piece("boat", "Boat", "Place on water, interact to ride. W/S paddle, A/D steer, interact again to hop out.",
                BuildingCategory.Functional, boat, 150f, new Color32(120, 90, 60, 255), null);
        }

        // ==================================================================
        // Helpers
        // ==================================================================

        private static SnapSocket[] WallSockets()
        {
            return new[]
            {
                new SnapSocket("bottom", Vector3.zero, new Vector3(0f, 180f, 0f), SnapTags.WallBottom, SnapTags.FoundationTop, SnapTags.WallTop),
                new SnapSocket("top", new Vector3(0f, 2f, 0f), Vector3.zero, SnapTags.WallTop, SnapTags.WallBottom, SnapTags.FloorEdge, SnapTags.RoofBottom),
                new SnapSocket("side_east", new Vector3(1f, 1f, 0f), new Vector3(0f, 90f, 0f), SnapTags.WallSide, SnapTags.WallSide),
                new SnapSocket("side_west", new Vector3(-1f, 1f, 0f), new Vector3(0f, 270f, 0f), SnapTags.WallSide, SnapTags.WallSide),
            };
        }

        private static GameObject Prefab(string assetName, string pieceId, System.Action<GameObject> build)
        {
            string assetPath = $"{PrefabFolder}/{assetName}.prefab";
            var existing = AssetDatabase.LoadAssetAtPath<GameObject>(assetPath);
            if (existing != null)
                return existing;

            var root = new GameObject(assetName);
            try
            {
                build(root);

                var piece = root.AddComponent<BuildingPiece>();
                var serializedPiece = new SerializedObject(piece);
                serializedPiece.FindProperty("pieceId").stringValue = pieceId;
                serializedPiece.ApplyModifiedPropertiesWithoutUndo();

                GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, assetPath);
                PrefabsCreated++;
                return prefab;
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        private static void AddBox(
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

        private static void Piece(
            string id, string displayName, string description, BuildingCategory category,
            GameObject prefab, float maxHealth, Color32 iconColor, SnapSocket[] sockets)
        {
            string path = $"{PieceFolder}/{ContentSetItemsAndBlocks.ToAssetName(id)}.asset";
            BuildingPieceDefinition piece = ExampleContentCreator.CreateOrLoad<BuildingPieceDefinition>(path, out bool created);
            if (!created)
                return;

            Sprite icon = Icon($"piece_{id}", IconShape.RoundedSquare, iconColor,
                Color.Lerp((Color)iconColor, Color.black, 0.35f));

            var serialized = new SerializedObject(piece);
            serialized.FindProperty("id").stringValue = id;
            serialized.FindProperty("displayName").stringValue = displayName;
            serialized.FindProperty("description").stringValue = description;
            serialized.FindProperty("category").intValue = (int)category;
            serialized.FindProperty("prefab").objectReferenceValue = prefab;
            serialized.FindProperty("icon").objectReferenceValue = icon;
            serialized.FindProperty("maxHealth").floatValue = maxHealth;
            serialized.FindProperty("materialCost").arraySize = 0; // recipes are the cost authority since Building Phase 3

            SerializedProperty socketList = serialized.FindProperty("sockets");
            socketList.arraySize = sockets?.Length ?? 0;
            for (int i = 0; i < socketList.arraySize; i++)
            {
                SnapSocket socket = sockets[i];
                SerializedProperty element = socketList.GetArrayElementAtIndex(i);
                element.FindPropertyRelative("socketName").stringValue = socket.SocketName;
                element.FindPropertyRelative("localPosition").vector3Value = socket.LocalPosition;
                element.FindPropertyRelative("localRotationEuler").vector3Value = socket.LocalRotationEuler;
                element.FindPropertyRelative("tag").stringValue = socket.Tag;

                SerializedProperty accepted = element.FindPropertyRelative("acceptedTags");
                accepted.arraySize = socket.AcceptedTags.Count;
                for (int t = 0; t < socket.AcceptedTags.Count; t++)
                    accepted.GetArrayElementAtIndex(t).stringValue = socket.AcceptedTags[t];
            }

            serialized.ApplyModifiedPropertiesWithoutUndo();
            PiecesCreated++;
        }

        private static void AddFuel(SerializedProperty fuelList, string itemId)
        {
            var item = FindItem(itemId);
            if (item == null)
                return;

            fuelList.arraySize++;
            fuelList.GetArrayElementAtIndex(fuelList.arraySize - 1).objectReferenceValue = item;
        }

        private static Material MaterialAsset(string name, Color color)
        {
            string assetPath = $"{MaterialFolder}/{name}.mat";
            var existing = AssetDatabase.LoadAssetAtPath<Material>(assetPath);
            if (existing != null)
                return existing;

            Shader shader = GraphicsSettings.currentRenderPipeline != null
                ? GraphicsSettings.currentRenderPipeline.defaultShader
                : Shader.Find("Standard");

            var material = new Material(shader) { color = color };
            AssetDatabase.CreateAsset(material, assetPath);
            return material;
        }
    }
}
