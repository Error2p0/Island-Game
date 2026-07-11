using System.IO;
using UnityEditor;
using UnityEngine;

namespace IslandGame.EditorTools.Data
{
    /// <summary>
    /// Persists TextureSynth output as real .png assets with the import
    /// settings pixel art needs: POINT filtering (no bilinear smear),
    /// UNCOMPRESSED (block compression destroys 16 px art), no mipmaps.
    /// Block textures additionally import Read/Write enabled — the runtime
    /// atlas packs by reading pixels (Phase 1 atlas convention); icons import
    /// as single Sprites.
    ///
    /// OVERWRITE semantics on purpose (unlike the idempotent content
    /// creators): regeneration is this tool's whole point, and a stable path
    /// per asset (gen_&lt;id&gt;.png) means references never dangle when a
    /// designer re-rolls a texture.
    /// </summary>
    public static class GeneratedTextureAssets
    {
        public const string BlockTextureFolder = "Assets/_Game/Content/Textures/Blocks";
        public const string IconFolder = "Assets/_Game/Content/Textures/Icons";

        /// <summary>Writes/overwrites a block-material texture .png and returns the imported asset.</summary>
        public static Texture2D WriteBlockTexture(string assetPath, Color32[] pixels, int size)
        {
            WritePng(assetPath, pixels, size, asSprite: false);
            return AssetDatabase.LoadAssetAtPath<Texture2D>(assetPath);
        }

        /// <summary>Writes/overwrites an icon .png imported as a Sprite and returns it.</summary>
        public static Sprite WriteIconSprite(string assetPath, Color32[] pixels, int size)
        {
            WritePng(assetPath, pixels, size, asSprite: true);
            return AssetDatabase.LoadAssetAtPath<Sprite>(assetPath);
        }

        private static void WritePng(string assetPath, Color32[] pixels, int size, bool asSprite)
        {
            DefinitionDatabaseSync.EnsureFolderExists(Path.GetDirectoryName(assetPath).Replace('\\', '/'));

            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
            texture.SetPixels32(pixels);
            texture.Apply(false, false);
            byte[] png = texture.EncodeToPNG();
            Object.DestroyImmediate(texture);

            File.WriteAllBytes(Path.GetFullPath(assetPath), png);
            AssetDatabase.ImportAsset(assetPath);

            var importer = (TextureImporter)AssetImporter.GetAtPath(assetPath);
            if (asSprite)
            {
                importer.textureType = TextureImporterType.Sprite;
                importer.spriteImportMode = SpriteImportMode.Single;
            }
            else
            {
                importer.textureType = TextureImporterType.Default;
                importer.isReadable = true; // the runtime atlas reads pixels
            }

            importer.filterMode = FilterMode.Point;
            importer.mipmapEnabled = false;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.SaveAndReimport();
        }
    }
}
