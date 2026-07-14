using IslandGame.Foliage;
using IslandGame.Terrain;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace IslandGame.EditorTools
{
    /// <summary>
    /// Adds the FoliageScatterSystem to the scene's VoxelWorld object (same
    /// builder workflow as the structure system): foliage is part of world
    /// generation, so it lives and dies with the world object — Build
    /// Everything's world deletion covers it automatically. Idempotent — an
    /// existing component and its tweaks are left untouched.
    /// </summary>
    public static class FoliageSystemBuilder
    {
        [MenuItem("Island Game/World/Add Foliage System")]
        public static void Create()
        {
            var world = Object.FindFirstObjectByType<VoxelWorld>();
            if (world == null)
            {
                Debug.LogError("Foliage System: no VoxelWorld in the scene — run Island Game/World/Create Voxel World first.");
                return;
            }

            if (world.GetComponent<FoliageScatterSystem>() != null)
            {
                Debug.Log("Foliage System: already set up — nothing to do.");
                return;
            }

            var system = Undo.AddComponent<FoliageScatterSystem>(world.gameObject);
            var serialized = new SerializedObject(system);
            serialized.FindProperty("world").objectReferenceValue = world;
            serialized.ApplyModifiedPropertiesWithoutUndo();

            EditorSceneManager.MarkSceneDirty(world.gameObject.scene);
            Debug.Log("Foliage System: added FoliageScatterSystem to the VoxelWorld object. Save the scene.");
        }
    }
}
