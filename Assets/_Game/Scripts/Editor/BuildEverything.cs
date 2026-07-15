using System;
using IslandGame.EditorTools.Data;
using IslandGame.Player;
using IslandGame.Terrain;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace IslandGame.EditorTools
{
    /// <summary>
    /// One-click full project bootstrap: runs every builder menu in dependency
    /// order — databases and example content, player rig, generated animations,
    /// hold sockets, inventory/creative/crafting UI, voxel world — and finishes
    /// by placing the player over the spawn island.
    ///
    /// CLEAN REBUILD: existing built SCENE objects (Player, the three UI
    /// canvases, VoxelWorld) are DELETED first and rebuilt from scratch, and
    /// the generated Animations folder is regenerated — so per-object tweaks
    /// on those are lost (a confirmation dialog says so). Content ASSETS
    /// (items/blocks/recipes/textures) are NOT deleted: the content creators
    /// are idempotent — they recreate anything missing and never overwrite
    /// hand-edited assets, so your authored data survives.
    /// </summary>
    public static class BuildEverything
    {
        private static readonly Vector3 SpawnPosition = new Vector3(0f, 45f, 0f);

        [MenuItem("Island Game/Everything/Build Everything In Order")]
        public static void Run()
        {
            if (Application.isPlaying)
            {
                Debug.LogError("Build Everything: exit play mode first.");
                return;
            }

            if (!EditorUtility.DisplayDialog(
                    "Build Everything In Order",
                    "This runs every builder in dependency order.\n\n" +
                    "DELETED and rebuilt from scratch:\n" +
                    "  • Player (and all component tweaks on it)\n" +
                    "  • InventoryCanvas / CreativeMenuCanvas / CraftingMenuCanvas / StatsHudCanvas\n" +
                    "  • VoxelWorld\n" +
                    "  • Generated animations folder\n\n" +
                    "KEPT: all content assets (items, blocks, recipes, textures) — " +
                    "the content creators only fill in what's missing.\n\nContinue?",
                    "Build Everything", "Cancel"))
                return;

            Undo.IncrementCurrentGroup();
            Undo.SetCurrentGroupName("Build Everything In Order");

            var steps = new (string name, Action action)[]
            {
                ("Delete existing built scene objects", DeleteExistingSceneObjects),
                ("Sync databases", DefinitionDatabaseSync.SyncAll),
                ("Create example content (items/blocks)", ExampleContentCreator.Create),
                ("Create world-gen content (sand/dirt/grass/water)", WorldGenContentCreator.Create),
                ("Create tree content (wood/leaves blocks + templates)", ExampleTreeContentCreator.Create),
                ("Create foliage content (bushes/reeds + items)", FoliageContentCreator.Create),
                ("Create example recipes (+ Stone Pickaxe)", ExampleRecipeCreator.Create),
                ("Create example building pieces", ExampleBuildingPieceCreator.Create),
                ("Generate base content set", BaseContentSetGenerator.Run),
                ("Create tree variant content (birch/willow/dead + rope)", TreeVariantContentCreator.Create),
                ("Create death content (gravestone + penalty policy)", DeathContentCreator.Create),
                ("Create player stat definitions", StatContentCreator.Create),
                ("Create example creatures", CreatureContentCreator.Create),
                ("Create example structures", StructureContentCreator.Create),
                ("Build player rig", PlayerRigBuilder.BuildPlayerRig),
                ("Build player animations & controller", PlayerAnimationBuilder.Build),
                ("Create hold sockets", HoldSocketBuilder.Create),
                ("Build inventory UI", InventoryUIBuilder.Build),
                ("Build creative menu UI", CreativeMenuUIBuilder.Build),
                ("Create voxel world", VoxelWorldBuilder.Create),
                ("Add structure system", StructureSystemBuilder.Create),
                ("Add foliage system", FoliageSystemBuilder.Create),
                ("Add building system to player", BuildingSystemBuilder.Create),
                ("Add stats system to player", StatsSystemBuilder.Create),
                ("Create day/night cycle", DayNightBuilder.Create),
                ("Add weather system", WeatherBuilder.Create),
                ("Add respawn system", RespawnSystemBuilder.Create),
                ("Build crafting menu UI", CraftingMenuUIBuilder.Build),
                ("Build stats HUD", StatsHudBuilder.Build),
                ("Build interaction prompt HUD", InteractionHudBuilder.Build),
                ("Create example creature spawners", CreatureContentCreator.CreateExampleSpawners),
                ("Add save system", SaveSystemBuilder.Create),
                ("Place player at spawn", PlacePlayerAtSpawn),
            };

            try
            {
                for (int i = 0; i < steps.Length; i++)
                {
                    (string name, Action action) = steps[i];
                    EditorUtility.DisplayProgressBar(
                        "Build Everything In Order", $"{i + 1}/{steps.Length}  {name}", (float)i / steps.Length);
                    Debug.Log($"[BuildEverything] Step {i + 1}/{steps.Length}: {name}");

                    try
                    {
                        action();
                    }
                    catch (Exception exception)
                    {
                        Debug.LogError(
                            $"[BuildEverything] Step '{name}' FAILED — aborting the remaining steps. See the exception below.");
                        Debug.LogException(exception);
                        return;
                    }
                }
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }

            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());

            var player = UnityEngine.Object.FindFirstObjectByType<PlayerReferences>();
            if (player != null)
                Selection.activeGameObject = player.gameObject;

            Debug.Log(
                "[BuildEverything] All steps complete. SAVE THE SCENE. Reminders: disable/delete any old ground " +
                "plane (it z-fights the terrain), and add Starting Items on the player's InventorySystem if you " +
                "want test content on spawn.");
        }

        // ------------------------------------------------------------------
        // Steps
        // ------------------------------------------------------------------

        private static void DeleteExistingSceneObjects()
        {
            // Player (found by component, not name, in case it was renamed).
            var player = UnityEngine.Object.FindFirstObjectByType<PlayerReferences>();
            if (player != null)
                DeleteObject(player.gameObject);

            // The three UI canvases the builders own.
            DeleteByName("InventoryCanvas");
            DeleteByName("CreativeMenuCanvas");
            DeleteByName("CraftingMenuCanvas");
            DeleteByName("StatsHudCanvas");
            DeleteByName("InteractionPromptCanvas");
            DeleteByName("DeathScreenCanvas");
            DeleteByName("CreatureSpawners");
            DeleteByName("SaveSystem");

            var world = UnityEngine.Object.FindFirstObjectByType<VoxelWorld>();
            if (world != null)
                DeleteObject(world.gameObject);

            // The EventSystem is deliberately kept: the UI builders reuse it,
            // and other tooling may depend on it existing.
        }

        private static void DeleteByName(string objectName)
        {
            GameObject existing = GameObject.Find(objectName);
            if (existing != null)
                DeleteObject(existing);
        }

        private static void DeleteObject(GameObject target)
        {
            Debug.Log($"[BuildEverything] Deleting existing '{target.name}'.");
            Undo.DestroyObjectImmediate(target);
        }

        private static void PlacePlayerAtSpawn()
        {
            var player = UnityEngine.Object.FindFirstObjectByType<PlayerReferences>();
            if (player == null)
                throw new InvalidOperationException("No player found after the rig build — earlier step failed?");

            player.transform.position = SpawnPosition;
            Debug.Log($"[BuildEverything] Player placed at {SpawnPosition} — it drops onto the guaranteed spawn island on play.");
        }
    }
}
