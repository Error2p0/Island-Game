using IslandGame.Building;
using IslandGame.Crafting;
using IslandGame.Data.Building;
using IslandGame.Data.Crafting;
using IslandGame.Data.Items;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace IslandGame.EditorTools.Data
{
    /// <summary>
    /// Creates the starter set of building content: five primitive-built
    /// structural prefabs (foundation, wall, floor, 45° roof, door frame —
    /// correctly scaled, collidable, BuildingPiece root + stamped ID), their
    /// BuildingPieceDefinitions with fully authored snap sockets, the Campfire
    /// and Workbench functional placeables (CampfireBehavior with light +
    /// flame particles + fuel, WorkbenchBehavior + station marker), one
    /// building RECIPE per piece (Phase 3 — placement charges these), piece
    /// icons, one Placeable ITEM per structural piece (the hotbar/portable-kit
    /// path), and a database sync at the end.
    ///
    /// GRID: pieces are authored on a 2 m building grid (Valheim-style) —
    /// foundations 2×0.5×2 with the walkable top at local y=0.5, walls/door
    /// frames 2 wide × 2 high, floors 2×2, roofs 2×2 footprint rising 2 m at
    /// 45°. All prefab pivots sit at the piece's bottom-center. Socket
    /// positions in this file are the reference example of the convention —
    /// the door frame is deliberately a wall-compatible piece (same wall_*
    /// sockets) plus a doorway socket for the future door leaf.
    ///
    /// Idempotent: assets that already exist are left untouched (hand edits
    /// survive re-running it), only missing pieces are created.
    /// </summary>
    public static class ExampleBuildingPieceCreator
    {
        private const string MaterialFolder = "Assets/_Game/Content/Building/Materials";
        private const string PrefabFolder = "Assets/_Game/Content/Building/Prefabs";
        private const string PieceFolder = "Assets/_Game/Content/Building/Pieces";
        private const string ItemFolder = "Assets/_Game/Content/Items";
        private const string IconFolder = "Assets/_Game/Content/Textures/Icons";
        private const string RecipeFolder = "Assets/_Game/Content/Recipes";

        // Cross-links into the Phase 1 example items (see ExampleContentCreator).
        private const string PlankItemPath = "Assets/_Game/Content/Items/WoodPlank.asset";
        private const string StoneItemPath = "Assets/_Game/Content/Items/Stone.asset";
        private const string LogItemPath = "Assets/_Game/Content/Items/Log.asset";

        [MenuItem("Island Game/Data/Create Example Building Pieces")]
        public static void Create()
        {
            DefinitionDatabaseSync.EnsureFolderExists(MaterialFolder);
            DefinitionDatabaseSync.EnsureFolderExists(PrefabFolder);
            DefinitionDatabaseSync.EnsureFolderExists(PieceFolder);
            DefinitionDatabaseSync.EnsureFolderExists(ItemFolder);
            DefinitionDatabaseSync.EnsureFolderExists(IconFolder);
            DefinitionDatabaseSync.EnsureFolderExists(RecipeFolder);

            // --- Materials -------------------------------------------------
            Material wood = CreateMaterial($"{MaterialFolder}/BuildingWood.mat", new Color(0.72f, 0.55f, 0.35f));
            Material woodDark = CreateMaterial($"{MaterialFolder}/BuildingWoodDark.mat", new Color(0.52f, 0.40f, 0.26f));
            Material stone = CreateMaterial($"{MaterialFolder}/BuildingStone.mat", new Color(0.55f, 0.55f, 0.58f));

            // --- Cost items (may be absent if example content wasn't created) ---
            var plankItem = AssetDatabase.LoadAssetAtPath<ItemDefinition>(PlankItemPath);
            var stoneItem = AssetDatabase.LoadAssetAtPath<ItemDefinition>(StoneItemPath);
            var logItem = AssetDatabase.LoadAssetAtPath<ItemDefinition>(LogItemPath);
            if (plankItem == null || stoneItem == null)
            {
                Debug.LogWarning(
                    "Example items (Wood Plank/Stone) not found — building pieces are created WITHOUT material " +
                    "costs. Run Island Game/Data/Create Example Content first, then re-run this to fill them in... " +
                    "or author costs in the Building Piece Editor.");
            }

            // --- Foundation ------------------------------------------------
            GameObject foundationPrefab = CreatePrefab(
                $"{PrefabFolder}/Foundation_Stone.prefab", "foundation_stone",
                new ChildBox("Slab", new Vector3(0f, 0.25f, 0f), new Vector3(2f, 0.5f, 2f), Vector3.zero, stone));

            BuildingPieceDefinition foundationPiece = CreatePiece($"{PieceFolder}/FoundationStone.asset",
                id: "foundation_stone", displayName: "Stone Foundation",
                description: "A level 2 m stone base. Everything else stands on these.",
                category: BuildingCategory.Foundation, prefab: foundationPrefab, maxHealth: 500f,
                cost: new[] { new BuildingMaterialCost(stoneItem, 4) },
                sockets: new[]
                {
                    new SnapSocket("top_north", new Vector3(0f, 0.5f, 1f), new Vector3(0f, 0f, 0f), SnapTags.FoundationTop, SnapTags.WallBottom, SnapTags.FloorEdge),
                    new SnapSocket("top_east", new Vector3(1f, 0.5f, 0f), new Vector3(0f, 90f, 0f), SnapTags.FoundationTop, SnapTags.WallBottom, SnapTags.FloorEdge),
                    new SnapSocket("top_south", new Vector3(0f, 0.5f, -1f), new Vector3(0f, 180f, 0f), SnapTags.FoundationTop, SnapTags.WallBottom, SnapTags.FloorEdge),
                    new SnapSocket("top_west", new Vector3(-1f, 0.5f, 0f), new Vector3(0f, 270f, 0f), SnapTags.FoundationTop, SnapTags.WallBottom, SnapTags.FloorEdge),
                    new SnapSocket("side_north", new Vector3(0f, 0.25f, 1f), new Vector3(0f, 0f, 0f), SnapTags.FoundationSide, SnapTags.FoundationSide),
                    new SnapSocket("side_east", new Vector3(1f, 0.25f, 0f), new Vector3(0f, 90f, 0f), SnapTags.FoundationSide, SnapTags.FoundationSide),
                    new SnapSocket("side_south", new Vector3(0f, 0.25f, -1f), new Vector3(0f, 180f, 0f), SnapTags.FoundationSide, SnapTags.FoundationSide),
                    new SnapSocket("side_west", new Vector3(-1f, 0.25f, 0f), new Vector3(0f, 270f, 0f), SnapTags.FoundationSide, SnapTags.FoundationSide),
                });

            // --- Wall ------------------------------------------------------
            GameObject wallPrefab = CreatePrefab(
                $"{PrefabFolder}/Wall_Wood.prefab", "wall_wood",
                new ChildBox("Panel", new Vector3(0f, 1f, 0f), new Vector3(2f, 2f, 0.2f), Vector3.zero, wood));

            BuildingPieceDefinition wallPiece = CreatePiece($"{PieceFolder}/WallWood.asset",
                id: "wall_wood", displayName: "Wooden Wall",
                description: "A 2×2 m plank wall. Stands on foundation edges or other walls.",
                category: BuildingCategory.Wall, prefab: wallPrefab, maxHealth: 250f,
                cost: new[] { new BuildingMaterialCost(plankItem, 4) },
                sockets: new[]
                {
                    new SnapSocket("bottom", new Vector3(0f, 0f, 0f), new Vector3(0f, 180f, 0f), SnapTags.WallBottom, SnapTags.FoundationTop, SnapTags.WallTop),
                    new SnapSocket("top", new Vector3(0f, 2f, 0f), new Vector3(0f, 0f, 0f), SnapTags.WallTop, SnapTags.WallBottom, SnapTags.FloorEdge, SnapTags.RoofBottom),
                    new SnapSocket("side_east", new Vector3(1f, 1f, 0f), new Vector3(0f, 90f, 0f), SnapTags.WallSide, SnapTags.WallSide),
                    new SnapSocket("side_west", new Vector3(-1f, 1f, 0f), new Vector3(0f, 270f, 0f), SnapTags.WallSide, SnapTags.WallSide),
                });

            // --- Floor -----------------------------------------------------
            GameObject floorPrefab = CreatePrefab(
                $"{PrefabFolder}/Floor_Wood.prefab", "floor_wood",
                new ChildBox("Deck", new Vector3(0f, 0.05f, 0f), new Vector3(2f, 0.1f, 2f), Vector3.zero, wood));

            BuildingPieceDefinition floorPiece = CreatePiece($"{PieceFolder}/FloorWood.asset",
                id: "floor_wood", displayName: "Wooden Floor",
                description: "A 2×2 m plank floor tile. Sits flush on foundation tops and wall tops.",
                category: BuildingCategory.Floor, prefab: floorPrefab, maxHealth: 200f,
                cost: new[] { new BuildingMaterialCost(plankItem, 3) },
                sockets: new[]
                {
                    new SnapSocket("edge_north", new Vector3(0f, 0f, 1f), new Vector3(0f, 0f, 0f), SnapTags.FloorEdge, SnapTags.FloorEdge, SnapTags.FoundationTop, SnapTags.WallTop),
                    new SnapSocket("edge_east", new Vector3(1f, 0f, 0f), new Vector3(0f, 90f, 0f), SnapTags.FloorEdge, SnapTags.FloorEdge, SnapTags.FoundationTop, SnapTags.WallTop),
                    new SnapSocket("edge_south", new Vector3(0f, 0f, -1f), new Vector3(0f, 180f, 0f), SnapTags.FloorEdge, SnapTags.FloorEdge, SnapTags.FoundationTop, SnapTags.WallTop),
                    new SnapSocket("edge_west", new Vector3(-1f, 0f, 0f), new Vector3(0f, 270f, 0f), SnapTags.FloorEdge, SnapTags.FloorEdge, SnapTags.FoundationTop, SnapTags.WallTop),
                });

            // --- Sloped roof (45°) ------------------------------------------
            // One thin box, long axis √8 m, pitched -45° about X: it spans the
            // 2×2 footprint from the eave edge (0,0,-1) up to the ridge edge
            // (0,2,1). Cheapest possible sloped collider — one rotated box.
            GameObject roofPrefab = CreatePrefab(
                $"{PrefabFolder}/Roof_Wood_45.prefab", "roof_wood_45",
                new ChildBox("Panel", new Vector3(0f, 1f, 0f), new Vector3(2f, 0.15f, 2.828427f), new Vector3(-45f, 0f, 0f), woodDark));

            BuildingPieceDefinition roofPiece = CreatePiece($"{PieceFolder}/RoofWood45.asset",
                id: "roof_wood_45", displayName: "Wooden Roof 45°",
                description: "A 45° roof panel over a 2×2 m footprint. Eave sits on wall tops; rows stack toward the ridge.",
                category: BuildingCategory.Roof, prefab: roofPrefab, maxHealth: 200f,
                cost: new[] { new BuildingMaterialCost(plankItem, 3) },
                sockets: new[]
                {
                    new SnapSocket("eave", new Vector3(0f, 0f, -1f), new Vector3(0f, 180f, 0f), SnapTags.RoofBottom, SnapTags.WallTop, SnapTags.RoofTop),
                    new SnapSocket("ridge", new Vector3(0f, 2f, 1f), new Vector3(0f, 0f, 0f), SnapTags.RoofTop, SnapTags.RoofBottom),
                    new SnapSocket("side_east", new Vector3(1f, 1f, 0f), new Vector3(0f, 90f, 0f), SnapTags.RoofSide, SnapTags.RoofSide),
                    new SnapSocket("side_west", new Vector3(-1f, 1f, 0f), new Vector3(0f, 270f, 0f), SnapTags.RoofSide, SnapTags.RoofSide),
                });

            // --- Door frame --------------------------------------------------
            // Wall-compatible piece (same wall_* sockets) with a 1.0×1.7 m
            // opening and a doorway socket where the future door leaf hinges.
            GameObject doorFramePrefab = CreatePrefab(
                $"{PrefabFolder}/DoorFrame_Wood.prefab", "door_frame_wood",
                new ChildBox("ColumnLeft", new Vector3(-0.75f, 1f, 0f), new Vector3(0.5f, 2f, 0.2f), Vector3.zero, wood),
                new ChildBox("ColumnRight", new Vector3(0.75f, 1f, 0f), new Vector3(0.5f, 2f, 0.2f), Vector3.zero, wood),
                new ChildBox("Header", new Vector3(0f, 1.85f, 0f), new Vector3(1f, 0.3f, 0.2f), Vector3.zero, wood));

            BuildingPieceDefinition doorFramePiece = CreatePiece($"{PieceFolder}/DoorFrameWood.asset",
                id: "door_frame_wood", displayName: "Wooden Door Frame",
                description: "A wall piece with a 1×1.7 m opening. A door leaf hinges into the doorway socket.",
                category: BuildingCategory.Door, prefab: doorFramePrefab, maxHealth: 250f,
                cost: new[] { new BuildingMaterialCost(plankItem, 4) },
                sockets: new[]
                {
                    new SnapSocket("bottom", new Vector3(0f, 0f, 0f), new Vector3(0f, 180f, 0f), SnapTags.WallBottom, SnapTags.FoundationTop, SnapTags.WallTop),
                    new SnapSocket("top", new Vector3(0f, 2f, 0f), new Vector3(0f, 0f, 0f), SnapTags.WallTop, SnapTags.WallBottom, SnapTags.FloorEdge, SnapTags.RoofBottom),
                    new SnapSocket("side_east", new Vector3(1f, 1f, 0f), new Vector3(0f, 90f, 0f), SnapTags.WallSide, SnapTags.WallSide),
                    new SnapSocket("side_west", new Vector3(-1f, 1f, 0f), new Vector3(0f, 270f, 0f), SnapTags.WallSide, SnapTags.WallSide),
                    new SnapSocket("hinge", new Vector3(-0.5f, 0f, 0f), new Vector3(0f, 0f, 0f), SnapTags.Doorway, SnapTags.DoorHinge),
                });

            // --- Campfire (functional: fuel/light/burn — CampfireBehavior) ---
            GameObject campfirePrefab = CreateCampfirePrefab(
                $"{PrefabFolder}/Campfire.prefab", stone, woodDark, logItem, plankItem);

            BuildingPieceDefinition campfirePiece = CreatePiece($"{PieceFolder}/Campfire.asset",
                id: "campfire", displayName: "Campfire",
                description: "A stone fire pit. Feed it wood (interact while holding logs or planks), light it for warmth and light.",
                category: BuildingCategory.Functional, prefab: campfirePrefab, maxHealth: 100f,
                cost: new BuildingMaterialCost[0], sockets: new SnapSocket[0]);

            // --- Workbench (functional: station gating + opens the menu) ----
            GameObject workbenchPrefab = CreateWorkbenchPrefab(
                $"{PrefabFolder}/Workbench.prefab", wood, woodDark, stone);

            BuildingPieceDefinition workbenchPiece = CreatePiece($"{PieceFolder}/Workbench.asset",
                id: "workbench", displayName: "Workbench",
                description: "A sturdy work surface. Standing near it unlocks Workbench recipes; interact to open the crafting menu.",
                category: BuildingCategory.Functional, prefab: workbenchPrefab, maxHealth: 200f,
                cost: new BuildingMaterialCost[0], sockets: new SnapSocket[0]);

            // --- Piece icons (fill-if-empty so the crafting menu has visuals
            // even for pieces created by earlier phases) ----------------------
            EnsurePieceIcon(foundationPiece, new Color32(140, 140, 148, 255));
            EnsurePieceIcon(wallPiece, new Color32(184, 140, 89, 255));
            EnsurePieceIcon(floorPiece, new Color32(199, 161, 110, 255));
            EnsurePieceIcon(roofPiece, new Color32(133, 102, 66, 255));
            EnsurePieceIcon(doorFramePiece, new Color32(158, 116, 72, 255));
            EnsurePieceIcon(campfirePiece, new Color32(226, 128, 51, 255));
            EnsurePieceIcon(workbenchPiece, new Color32(120, 96, 160, 255));

            // --- Building recipes (the Phase 3 cost link; placement charges
            // these on confirm) ----------------------------------------------
            if (plankItem != null && stoneItem != null)
            {
                CreateBuildingRecipe($"{RecipeFolder}/BuildFoundationStone.asset",
                    id: "build_foundation_stone", displayName: "Stone Foundation",
                    piece: foundationPiece, station: CraftingStationType.None,
                    ingredients: new[] { (stoneItem, 4) });

                CreateBuildingRecipe($"{RecipeFolder}/BuildWallWood.asset",
                    id: "build_wall_wood", displayName: "Wooden Wall",
                    piece: wallPiece, station: CraftingStationType.None,
                    ingredients: new[] { (plankItem, 6) });

                CreateBuildingRecipe($"{RecipeFolder}/BuildFloorWood.asset",
                    id: "build_floor_wood", displayName: "Wooden Floor",
                    piece: floorPiece, station: CraftingStationType.None,
                    ingredients: new[] { (plankItem, 3) });

                CreateBuildingRecipe($"{RecipeFolder}/BuildRoofWood45.asset",
                    id: "build_roof_wood_45", displayName: "Wooden Roof 45°",
                    piece: roofPiece, station: CraftingStationType.None,
                    ingredients: new[] { (plankItem, 4) });

                // The station-gated building example: door frames need a
                // workbench nearby — both to see it enabled in the menu AND to
                // place it (the ghost enforces the same recipe requirements).
                CreateBuildingRecipe($"{RecipeFolder}/BuildDoorFrameWood.asset",
                    id: "build_door_frame_wood", displayName: "Wooden Door Frame",
                    piece: doorFramePiece, station: CraftingStationType.Workbench,
                    ingredients: new[] { (plankItem, 4) });

                CreateBuildingRecipe($"{RecipeFolder}/BuildWorkbench.asset",
                    id: "build_workbench", displayName: "Workbench",
                    piece: workbenchPiece, station: CraftingStationType.None,
                    ingredients: new[] { (plankItem, 6) });

                if (logItem != null)
                {
                    CreateBuildingRecipe($"{RecipeFolder}/BuildCampfire.asset",
                        id: "build_campfire", displayName: "Campfire",
                        piece: campfirePiece, station: CraftingStationType.None,
                        ingredients: new[] { (stoneItem, 4), (logItem, 2) });
                }
            }
            else
            {
                Debug.LogWarning("Building recipes skipped — example items missing (see the warning above).");
            }

            // --- Placeable items (hotbar entry point for each piece) --------
            CreatePlaceableItem($"{ItemFolder}/FoundationStone.asset",
                id: "foundation_stone", displayName: "Stone Foundation",
                description: "Places a stone foundation. Everything else stands on these.",
                piece: foundationPiece, weightKg: 8f, iconColor: new Color32(140, 140, 148, 255));

            CreatePlaceableItem($"{ItemFolder}/WallWood.asset",
                id: "wall_wood", displayName: "Wooden Wall",
                description: "Places a wooden wall on foundation edges, wall tops or wall sides.",
                piece: wallPiece, weightKg: 4f, iconColor: new Color32(184, 140, 89, 255));

            CreatePlaceableItem($"{ItemFolder}/FloorWood.asset",
                id: "floor_wood", displayName: "Wooden Floor",
                description: "Places a wooden floor tile flush with foundation and wall tops.",
                piece: floorPiece, weightKg: 3f, iconColor: new Color32(199, 161, 110, 255));

            CreatePlaceableItem($"{ItemFolder}/RoofWood45.asset",
                id: "roof_wood_45", displayName: "Wooden Roof 45°",
                description: "Places a 45° roof panel. Eave snaps to wall tops; rows stack toward the ridge.",
                piece: roofPiece, weightKg: 3f, iconColor: new Color32(133, 102, 66, 255));

            CreatePlaceableItem($"{ItemFolder}/DoorFrameWood.asset",
                id: "door_frame_wood", displayName: "Wooden Door Frame",
                description: "Places a wall piece with a door opening. A door leaf arrives in a later phase.",
                piece: doorFramePiece, weightKg: 4f, iconColor: new Color32(158, 116, 72, 255));

            AssetDatabase.SaveAssets();
            DefinitionDatabaseSync.SyncAll();

            Debug.Log(
                "Example building content ready: Foundation/Wall/Floor/Roof/Door Frame pieces + Campfire and " +
                "Workbench functional placeables, building recipes (Wall = 6 planks; Door Frame needs a Workbench; " +
                "Campfire/Workbench hand-buildable), piece icons, and the Phase 2 placeable items. Build from the " +
                "crafting menu's Building tab (B); ingredients are charged on placement. Existing assets were left " +
                "untouched.");
            EditorGUIUtility.PingObject(AssetDatabase.LoadAssetAtPath<Object>(PieceFolder));
        }

        // ------------------------------------------------------------------
        // Prefabs from primitives
        // ------------------------------------------------------------------

        private readonly struct ChildBox
        {
            public readonly string Name;
            public readonly Vector3 Center;
            public readonly Vector3 Size;
            public readonly Vector3 RotationEuler;
            public readonly Material Material;

            public ChildBox(string name, Vector3 center, Vector3 size, Vector3 rotationEuler, Material material)
            {
                Name = name;
                Center = center;
                Size = size;
                RotationEuler = rotationEuler;
                Material = material;
            }
        }

        /// <summary>
        /// Builds a piece prefab: empty root (pivot = bottom-center by
        /// authoring convention) carrying BuildingPiece with the stamped ID,
        /// one primitive cube child per box — each keeps its BoxCollider, so
        /// the piece is collidable exactly as it looks. Existing prefabs are
        /// returned untouched.
        /// </summary>
        private static GameObject CreatePrefab(string assetPath, string pieceId, params ChildBox[] boxes)
        {
            var existing = AssetDatabase.LoadAssetAtPath<GameObject>(assetPath);
            if (existing != null)
                return existing;

            var root = new GameObject(System.IO.Path.GetFileNameWithoutExtension(assetPath));
            try
            {
                foreach (ChildBox box in boxes)
                    AddBox(root.transform, box.Name, box.Center, box.Size, box.RotationEuler, box.Material);

                StampBuildingPiece(root, pieceId);

                GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, assetPath);
                Debug.Log($"Created building prefab {assetPath}.", prefab);
                return prefab;
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        private static GameObject AddBox(
            Transform parent, string name, Vector3 center, Vector3 size, Vector3 rotationEuler, Material material)
        {
            GameObject child = GameObject.CreatePrimitive(PrimitiveType.Cube);
            child.name = name;
            child.transform.SetParent(parent, false);
            child.transform.localPosition = center;
            child.transform.localEulerAngles = rotationEuler;
            child.transform.localScale = size;
            child.GetComponent<MeshRenderer>().sharedMaterial = material;
            return child;
        }

        private static BuildingPiece StampBuildingPiece(GameObject root, string pieceId)
        {
            var piece = root.AddComponent<BuildingPiece>();
            var serializedPiece = new SerializedObject(piece);
            serializedPiece.FindProperty("pieceId").stringValue = pieceId;
            serializedPiece.ApplyModifiedPropertiesWithoutUndo();
            return piece;
        }

        // ------------------------------------------------------------------
        // Functional prefabs (campfire, workbench)
        // ------------------------------------------------------------------

        private static GameObject CreateCampfirePrefab(
            string assetPath, Material stone, Material woodDark, ItemDefinition logItem, ItemDefinition plankItem)
        {
            var existing = AssetDatabase.LoadAssetAtPath<GameObject>(assetPath);
            if (existing != null)
                return existing;

            var root = new GameObject("Campfire");
            try
            {
                // Stone ring: six blocks around the pit.
                for (int i = 0; i < 6; i++)
                {
                    float angle = i * 60f * Mathf.Deg2Rad;
                    var position = new Vector3(Mathf.Cos(angle) * 0.55f, 0.125f, Mathf.Sin(angle) * 0.55f);
                    AddBox(root.transform, $"Stone_{i}", position, new Vector3(0.35f, 0.25f, 0.35f),
                        new Vector3(0f, -i * 60f, 0f), stone);
                }

                // Crossed logs in the pit.
                AddBox(root.transform, "LogA", new Vector3(0f, 0.15f, 0f), new Vector3(0.7f, 0.14f, 0.14f),
                    new Vector3(0f, 45f, 0f), woodDark);
                AddBox(root.transform, "LogB", new Vector3(0f, 0.24f, 0f), new Vector3(0.7f, 0.14f, 0.14f),
                    new Vector3(0f, -45f, 0f), woodDark);

                // Fire light — disabled cold, CampfireBehavior drives it.
                var lightObject = new GameObject("FireLight");
                lightObject.transform.SetParent(root.transform, false);
                lightObject.transform.localPosition = new Vector3(0f, 0.55f, 0f);
                var fireLight = lightObject.AddComponent<Light>();
                fireLight.type = LightType.Point;
                fireLight.color = new Color(1f, 0.62f, 0.28f);
                fireLight.range = 9f;
                fireLight.intensity = 2.2f;
                fireLight.shadows = LightShadows.None; // point shadows are pricey; revisit with the day/night phase
                fireLight.enabled = false;

                ParticleSystem flames = CreateFlameParticles(root.transform);

                // Station marker: Campfire-station recipes (future cooking)
                // work the moment one burns nearby — the existing convention.
                var marker = root.AddComponent<CraftingStationMarker>();
                var serializedMarker = new SerializedObject(marker);
                serializedMarker.FindProperty("stationType").intValue = (int)CraftingStationType.Campfire;
                serializedMarker.ApplyModifiedPropertiesWithoutUndo();

                var behavior = root.AddComponent<CampfireBehavior>();
                var serializedBehavior = new SerializedObject(behavior);
                serializedBehavior.FindProperty("fireLight").objectReferenceValue = fireLight;
                serializedBehavior.FindProperty("flames").objectReferenceValue = flames;
                SerializedProperty fuel = serializedBehavior.FindProperty("acceptedFuel");
                fuel.arraySize = 0;
                if (logItem != null)
                {
                    fuel.arraySize++;
                    fuel.GetArrayElementAtIndex(fuel.arraySize - 1).objectReferenceValue = logItem;
                }

                if (plankItem != null)
                {
                    fuel.arraySize++;
                    fuel.GetArrayElementAtIndex(fuel.arraySize - 1).objectReferenceValue = plankItem;
                }

                serializedBehavior.ApplyModifiedPropertiesWithoutUndo();

                StampBuildingPiece(root, "campfire");

                GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, assetPath);
                Debug.Log($"Created building prefab {assetPath}.", prefab);
                return prefab;
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        private static ParticleSystem CreateFlameParticles(Transform parent)
        {
            var flamesObject = new GameObject("Flames");
            flamesObject.transform.SetParent(parent, false);
            flamesObject.transform.localPosition = new Vector3(0f, 0.28f, 0f);

            var flames = flamesObject.AddComponent<ParticleSystem>();

            ParticleSystem.MainModule main = flames.main;
            main.loop = true;
            main.playOnAwake = false; // CampfireBehavior plays/stops it
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.5f, 0.9f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.8f, 1.6f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.15f, 0.35f);
            main.startColor = new ParticleSystem.MinMaxGradient(
                new Color(1f, 0.55f, 0.15f, 0.85f), new Color(1f, 0.85f, 0.3f, 0.85f));
            main.simulationSpace = ParticleSystemSimulationSpace.Local;
            main.maxParticles = 128;

            ParticleSystem.EmissionModule emission = flames.emission;
            emission.rateOverTime = 28f;

            ParticleSystem.ShapeModule shape = flames.shape;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 12f;
            shape.radius = 0.18f;

            ParticleSystem.ColorOverLifetimeModule colorOverLifetime = flames.colorOverLifetime;
            colorOverLifetime.enabled = true;
            var gradient = new Gradient();
            gradient.SetKeys(
                new[]
                {
                    new GradientColorKey(Color.white, 0f),
                    new GradientColorKey(new Color(1f, 0.35f, 0.1f), 0.7f),
                    new GradientColorKey(new Color(0.4f, 0.08f, 0.02f), 1f),
                },
                new[]
                {
                    new GradientAlphaKey(0.9f, 0f),
                    new GradientAlphaKey(0.6f, 0.6f),
                    new GradientAlphaKey(0f, 1f),
                });
            colorOverLifetime.color = new ParticleSystem.MinMaxGradient(gradient);

            ParticleSystem.SizeOverLifetimeModule sizeOverLifetime = flames.sizeOverLifetime;
            sizeOverLifetime.enabled = true;
            sizeOverLifetime.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 1f, 1f, 0.35f));

            var renderer = flamesObject.GetComponent<ParticleSystemRenderer>();
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.sharedMaterial = CreateFlameMaterial();

            return flames;
        }

        private static Material CreateFlameMaterial()
        {
            string materialPath = $"{MaterialFolder}/FireParticle.mat";
            var existing = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
            if (existing != null)
                return existing;

            // Soft radial glow sprite; Sprites/Default is vertex-color friendly
            // and renders under URP (same reasoning as BlockTargetIndicator).
            Texture2D glow = ExampleContentCreator.CreateTexture(
                $"{MaterialFolder}/fire_glow.png", 32, GlowPixel, asSprite: false);

            var material = new Material(Shader.Find("Sprites/Default")) { mainTexture = glow };
            AssetDatabase.CreateAsset(material, materialPath);
            return material;
        }

        private static Color32 GlowPixel(int x, int y, int size, System.Random random)
        {
            float half = size * 0.5f;
            float dx = (x + 0.5f - half) / half;
            float dy = (y + 0.5f - half) / half;
            float falloff = Mathf.Clamp01(1f - Mathf.Sqrt(dx * dx + dy * dy));
            byte alpha = (byte)Mathf.RoundToInt(falloff * falloff * 255f);
            return new Color32(255, 255, 255, alpha);
        }

        private static GameObject CreateWorkbenchPrefab(
            string assetPath, Material wood, Material woodDark, Material stone)
        {
            var existing = AssetDatabase.LoadAssetAtPath<GameObject>(assetPath);
            if (existing != null)
                return existing;

            var root = new GameObject("Workbench");
            try
            {
                AddBox(root.transform, "Top", new Vector3(0f, 0.9f, 0f), new Vector3(2f, 0.15f, 1f), Vector3.zero, wood);
                AddBox(root.transform, "LegNE", new Vector3(0.85f, 0.41f, 0.35f), new Vector3(0.15f, 0.82f, 0.15f), Vector3.zero, woodDark);
                AddBox(root.transform, "LegNW", new Vector3(-0.85f, 0.41f, 0.35f), new Vector3(0.15f, 0.82f, 0.15f), Vector3.zero, woodDark);
                AddBox(root.transform, "LegSE", new Vector3(0.85f, 0.41f, -0.35f), new Vector3(0.15f, 0.82f, 0.15f), Vector3.zero, woodDark);
                AddBox(root.transform, "LegSW", new Vector3(-0.85f, 0.41f, -0.35f), new Vector3(0.15f, 0.82f, 0.15f), Vector3.zero, woodDark);
                AddBox(root.transform, "Anvil", new Vector3(0.6f, 1.05f, 0f), new Vector3(0.3f, 0.15f, 0.3f), Vector3.zero, stone);

                // The existing station convention does the recipe gating.
                var marker = root.AddComponent<CraftingStationMarker>();
                var serializedMarker = new SerializedObject(marker);
                serializedMarker.FindProperty("stationType").intValue = (int)CraftingStationType.Workbench;
                serializedMarker.ApplyModifiedPropertiesWithoutUndo();

                root.AddComponent<WorkbenchBehavior>();
                StampBuildingPiece(root, "workbench");

                GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, assetPath);
                Debug.Log($"Created building prefab {assetPath}.", prefab);
                return prefab;
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        // ------------------------------------------------------------------
        // Building recipes + piece icons
        // ------------------------------------------------------------------

        private static void CreateBuildingRecipe(
            string assetPath, string id, string displayName, BuildingPieceDefinition piece,
            CraftingStationType station, (ItemDefinition item, int count)[] ingredients)
        {
            RecipeDefinition recipe = ExampleContentCreator.CreateOrLoad<RecipeDefinition>(assetPath, out bool created);
            if (!created)
                return; // idempotency: never overwrite hand-edited recipes

            var serialized = new SerializedObject(recipe);
            serialized.FindProperty("id").stringValue = id;
            serialized.FindProperty("displayName").stringValue = displayName;
            serialized.FindProperty("output").objectReferenceValue = null;
            serialized.FindProperty("outputPiece").objectReferenceValue = piece;
            serialized.FindProperty("outputCount").intValue = 1;
            serialized.FindProperty("station").intValue = (int)station;
            serialized.FindProperty("craftSeconds").floatValue = 0f;

            SerializedProperty list = serialized.FindProperty("ingredients");
            list.arraySize = ingredients.Length;
            for (int i = 0; i < ingredients.Length; i++)
            {
                SerializedProperty element = list.GetArrayElementAtIndex(i);
                element.FindPropertyRelative("item").objectReferenceValue = ingredients[i].item;
                element.FindPropertyRelative("count").intValue = ingredients[i].count;
            }

            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        /// <summary>
        /// Gives a piece an icon only when it has NONE — safe on pieces created
        /// by earlier phases (never clobbers an authored icon) while ensuring
        /// the crafting menu's Building tab has visuals.
        /// </summary>
        private static void EnsurePieceIcon(BuildingPieceDefinition piece, Color32 color)
        {
            if (piece == null || piece.Icon != null)
                return;

            string iconPath = $"{IconFolder}/icon_piece_{piece.Id}.png";
            Sprite icon = ExampleContentCreator.LoadSprite(
                ExampleContentCreator.CreateTexture(iconPath, 64, SwatchIcon(color), asSprite: true));
            ExampleContentCreator.SetObjectReference(piece, "icon", icon);
        }

        // ------------------------------------------------------------------
        // Definition assets
        // ------------------------------------------------------------------

        private static BuildingPieceDefinition CreatePiece(
            string assetPath, string id, string displayName, string description,
            BuildingCategory category, GameObject prefab, float maxHealth,
            BuildingMaterialCost[] cost, SnapSocket[] sockets)
        {
            BuildingPieceDefinition piece =
                ExampleContentCreator.CreateOrLoad<BuildingPieceDefinition>(assetPath, out bool created);
            if (!created)
                return piece; // idempotency: never overwrite hand-edited definitions

            var serialized = new SerializedObject(piece);
            serialized.FindProperty("id").stringValue = id;
            serialized.FindProperty("displayName").stringValue = displayName;
            serialized.FindProperty("description").stringValue = description;
            serialized.FindProperty("category").intValue = (int)category;
            serialized.FindProperty("prefab").objectReferenceValue = prefab;
            serialized.FindProperty("maxHealth").floatValue = maxHealth;

            SerializedProperty costProperty = serialized.FindProperty("materialCost");
            costProperty.arraySize = 0;
            foreach (BuildingMaterialCost line in cost)
            {
                if (line.Item == null)
                    continue; // example items missing — see the warning in Create()

                costProperty.arraySize++;
                SerializedProperty entry = costProperty.GetArrayElementAtIndex(costProperty.arraySize - 1);
                entry.FindPropertyRelative("item").objectReferenceValue = line.Item;
                entry.FindPropertyRelative("count").intValue = line.Count;
            }

            SerializedProperty socketsProperty = serialized.FindProperty("sockets");
            socketsProperty.arraySize = sockets.Length;
            for (int i = 0; i < sockets.Length; i++)
            {
                SnapSocket socket = sockets[i];
                SerializedProperty element = socketsProperty.GetArrayElementAtIndex(i);
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
            return piece;
        }

        // ------------------------------------------------------------------
        // Placeable items (the hotbar entry point — item.PlacedPiece drives
        // BuildingPlacementController; grab them from the creative menu)
        // ------------------------------------------------------------------

        private static void CreatePlaceableItem(
            string assetPath, string id, string displayName, string description,
            BuildingPieceDefinition piece, float weightKg, Color32 iconColor)
        {
            string iconPath = $"{IconFolder}/icon_{id}.png";
            Sprite icon = ExampleContentCreator.LoadSprite(
                ExampleContentCreator.CreateTexture(iconPath, 64, SwatchIcon(iconColor), asSprite: true));

            ItemDefinition item = ExampleContentCreator.CreateOrLoad<ItemDefinition>(assetPath, out bool created);
            if (!created)
                return; // idempotency: never overwrite hand-edited items

            ExampleContentCreator.SetItemFields(item,
                id: id, displayName: displayName, description: description,
                icon: icon, category: ItemCategory.Placeable, maxStackSize: 20, weightKg: weightKg);
            ExampleContentCreator.SetObjectReference(item, "placedPiece", piece);
        }

        /// <summary>Flat swatch with a darker border — enough to tell the pieces apart on the hotbar.</summary>
        private static ExampleContentCreator.PixelFunc SwatchIcon(Color32 fill)
        {
            return (x, y, size, random) =>
            {
                int border = Mathf.Max(2, size / 12);
                bool onBorder = x < border || y < border || x >= size - border || y >= size - border;
                if (onBorder)
                    return new Color32((byte)(fill.r / 2), (byte)(fill.g / 2), (byte)(fill.b / 2), 255);

                float noise = 1f + (float)(random.NextDouble() - 0.5) * 0.08f;
                return new Color32(
                    (byte)Mathf.Clamp(Mathf.RoundToInt(fill.r * noise), 0, 255),
                    (byte)Mathf.Clamp(Mathf.RoundToInt(fill.g * noise), 0, 255),
                    (byte)Mathf.Clamp(Mathf.RoundToInt(fill.b * noise), 0, 255),
                    255);
            };
        }

        // ------------------------------------------------------------------
        // Materials
        // ------------------------------------------------------------------

        private static Material CreateMaterial(string assetPath, Color color)
        {
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
