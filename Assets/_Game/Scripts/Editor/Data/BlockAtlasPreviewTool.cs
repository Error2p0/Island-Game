using System.IO;
using IslandGame.Data.Blocks;
using UnityEditor;
using UnityEngine;

namespace IslandGame.EditorTools.Data
{
    /// <summary>
    /// Proves the atlas convention without waiting for the Phase 6 terrain
    /// mesher: builds a BlockTextureAtlas from the current BlockDatabase,
    /// logs every block's per-face UV rects, and saves the packed atlas as a
    /// PNG for visual inspection. Purely diagnostic — the PNG is a throwaway
    /// preview, never referenced by runtime code.
    /// </summary>
    public static class BlockAtlasPreviewTool
    {
        private const string PreviewFolder = "Assets/_Game/Content/Debug";
        private const string PreviewPath = PreviewFolder + "/BlockAtlasPreview.png";

        [MenuItem("Island Game/Data/Build Block Atlas Preview")]
        public static void BuildPreview()
        {
            BlockDatabase database = BlockDatabase.Instance;
            if (database == null)
                return; // Instance already logged the fix (run Sync Databases).

            BlockTextureAtlas atlas = BlockTextureAtlas.Build(database.All);
            Debug.Log(atlas.DescribeLayout(database.All));

            byte[] png = atlas.Texture.EncodeToPNG();
            atlas.Release();

            if (png == null)
            {
                Debug.LogError("BlockAtlasPreviewTool: could not encode the atlas to PNG.");
                return;
            }

            DefinitionDatabaseSync.EnsureFolderExists(PreviewFolder);
            File.WriteAllBytes(Path.GetFullPath(PreviewPath), png);
            AssetDatabase.ImportAsset(PreviewPath);

            var preview = AssetDatabase.LoadAssetAtPath<Texture2D>(PreviewPath);
            Debug.Log($"Block atlas preview written to {PreviewPath}.", preview);
            EditorGUIUtility.PingObject(preview);
        }
    }
}
