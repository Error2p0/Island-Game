using IslandGame.Player;
using IslandGame.Terrain;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace IslandGame.EditorTools
{
    /// <summary>
    /// One-click scene setup for the Phase 6 voxel terrain (same builder
    /// workflow as the inventory UI builder): creates the VoxelWorld object,
    /// wires it to the player, and ensures the player has
    /// PlayerBlockInteraction with its world reference set. Idempotent.
    /// </summary>
    public static class VoxelWorldBuilder
    {
        private const string WorldObjectName = "VoxelWorld";

        [MenuItem("Island Game/World/Create Voxel World")]
        public static void Create()
        {
            var playerReferences = Object.FindFirstObjectByType<PlayerReferences>();
            if (playerReferences == null)
            {
                Debug.LogError("No PlayerReferences found in the open scene — open the gameplay scene with the player first.");
                return;
            }

            VoxelWorld world = Object.FindFirstObjectByType<VoxelWorld>();
            if (world == null)
            {
                var worldObject = new GameObject(WorldObjectName);
                Undo.RegisterCreatedObjectUndo(worldObject, "Create Voxel World");
                world = worldObject.AddComponent<VoxelWorld>();
                Debug.Log($"Created '{WorldObjectName}'.", worldObject);
            }
            else
            {
                Debug.Log("VoxelWorld already exists — re-wiring references only.", world);
            }

            var serializedWorld = new SerializedObject(world);
            serializedWorld.FindProperty("player").objectReferenceValue = playerReferences.transform;
            serializedWorld.ApplyModifiedPropertiesWithoutUndo();

            var interaction = playerReferences.GetComponent<PlayerBlockInteraction>();
            if (interaction == null)
            {
                interaction = Undo.AddComponent<PlayerBlockInteraction>(playerReferences.gameObject);
                Debug.Log($"Added PlayerBlockInteraction to '{playerReferences.name}'.", playerReferences);
            }

            var serializedInteraction = new SerializedObject(interaction);
            serializedInteraction.FindProperty("world").objectReferenceValue = world;
            serializedInteraction.ApplyModifiedPropertiesWithoutUndo();

            var waterSensor = playerReferences.GetComponent<PlayerVoxelWaterSensor>();
            if (waterSensor == null)
            {
                waterSensor = Undo.AddComponent<PlayerVoxelWaterSensor>(playerReferences.gameObject);
                Debug.Log($"Added PlayerVoxelWaterSensor to '{playerReferences.name}'.", playerReferences);
            }

            var serializedSensor = new SerializedObject(waterSensor);
            serializedSensor.FindProperty("world").objectReferenceValue = world;
            serializedSensor.ApplyModifiedPropertiesWithoutUndo();

            if (playerReferences.GetComponent<BlockTargetIndicator>() == null)
            {
                Undo.AddComponent<BlockTargetIndicator>(playerReferences.gameObject);
                Debug.Log($"Added BlockTargetIndicator to '{playerReferences.name}'.", playerReferences);
            }

            EditorSceneManager.MarkSceneDirty(world.gameObject.scene);
            Selection.activeGameObject = world.gameObject;
            Debug.Log(
                "Voxel world ready. REMINDER: position the player ABOVE the island surface " +
                "(default ground height 24 → e.g. (0, 27, 0)) and disable any old ground plane so it doesn't z-fight the terrain.");
        }
    }
}
