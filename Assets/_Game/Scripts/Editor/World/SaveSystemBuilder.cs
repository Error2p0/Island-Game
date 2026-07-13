using IslandGame.Saving;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace IslandGame.EditorTools
{
    /// <summary>
    /// Creates the SaveSystem object (SaveManager + SaveLoadMenu) in the
    /// scene — same builder workflow as every world system. The object marks
    /// itself DontDestroyOnLoad at runtime and survives the load-time scene
    /// reload; the copy in the reloaded scene self-destructs (SaveManager's
    /// singleton guard). Idempotent.
    /// </summary>
    public static class SaveSystemBuilder
    {
        [MenuItem("Island Game/World/Add Save System")]
        public static void Create()
        {
            if (Object.FindFirstObjectByType<SaveManager>() != null)
            {
                Debug.Log("Save System: already set up — nothing to do.");
                return;
            }

            var systemObject = new GameObject("SaveSystem");
            Undo.RegisterCreatedObjectUndo(systemObject, "Add Save System");
            systemObject.AddComponent<SaveManager>();
            systemObject.AddComponent<SaveLoadMenu>();

            EditorSceneManager.MarkSceneDirty(systemObject.scene);
            Debug.Log("Save System: created 'SaveSystem' (SaveManager + F9 SaveLoadMenu). Save the scene.");
        }
    }
}
