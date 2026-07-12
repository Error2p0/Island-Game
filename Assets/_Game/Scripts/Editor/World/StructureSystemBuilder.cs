using IslandGame.Terrain;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace IslandGame.EditorTools
{
    /// <summary>
    /// Adds the StructurePlacementSystem to the scene's VoxelWorld object
    /// (same builder workflow as every world system): structures are part of
    /// world generation, so they live and die with the world object —
    /// Build Everything's world deletion covers them automatically.
    /// Idempotent — an existing component and its tweaks are left untouched.
    /// </summary>
    public static class StructureSystemBuilder
    {
        [MenuItem("Island Game/World/Add Structure System")]
        public static void Create()
        {
            var world = Object.FindFirstObjectByType<VoxelWorld>();
            if (world == null)
            {
                Debug.LogError("Structure System: no VoxelWorld in the scene — run Island Game/World/Create Voxel World first.");
                return;
            }

            if (world.GetComponent<StructurePlacementSystem>() != null)
            {
                Debug.Log("Structure System: already set up — nothing to do.");
                return;
            }

            var system = Undo.AddComponent<StructurePlacementSystem>(world.gameObject);
            var serialized = new SerializedObject(system);
            serialized.FindProperty("world").objectReferenceValue = world;
            serialized.ApplyModifiedPropertiesWithoutUndo();

            EditorSceneManager.MarkSceneDirty(world.gameObject.scene);
            Debug.Log("Structure System: added StructurePlacementSystem to the VoxelWorld object. Save the scene.");
        }
    }
}
