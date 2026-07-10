using System;
using IslandGame.Data.Blocks;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace IslandGame.EditorTools.Data
{
    /// <summary>
    /// Live 3D cube preview for the Block Editor, rendered offscreen with
    /// PreviewRenderUtility (no scene or play-mode state involved). The cube
    /// has six single-quad submeshes in BlockFace order so every face carries
    /// its own material/texture; face UVs follow the same orientation the
    /// terrain mesher will use (texture-up = +Y on sides, +Z on top/bottom).
    /// Faces that resolve to no texture render a magenta/black checker — same
    /// idea as BlockTextureAtlas' error tile.
    ///
    /// Left-drag rotates (and pauses auto-rotation); the owner window drives
    /// continuous repaints while AutoRotate is on. Call Dispose from the
    /// window's OnDisable or the preview scene leaks.
    /// </summary>
    internal sealed class BlockCubePreview : IDisposable
    {
        private const float DragDegreesPerPixel = 0.7f;
        private const float AutoRotateDegreesPerSecond = 25f;

        private PreviewRenderUtility utility;
        private Mesh cubeMesh;
        private Material[] faceMaterials;
        private Texture2D missingTexture;

        private float yaw = 32f;
        private float pitch = 22f;
        private double lastFrameTime;

        public bool AutoRotate { get; set; } = true;

        public void ResetView()
        {
            yaw = 32f;
            pitch = 22f;
        }

        /// <summary>Renders the cube into rect, pulling each face's texture from the provider (the edit session).</summary>
        public void Draw(Rect rect, Func<BlockFace, Texture2D> faceTextureProvider)
        {
            EnsureResources();
            HandleInput(rect);

            double now = EditorApplication.timeSinceStartup;
            if (AutoRotate)
                yaw = Mathf.Repeat(yaw + (float)(now - lastFrameTime) * AutoRotateDegreesPerSecond, 360f);
            lastFrameTime = now;

            if (Event.current.type != EventType.Repaint)
                return;

            for (int i = 0; i < BlockFaces.Count; i++)
            {
                Texture2D texture = faceTextureProvider(BlockFaces.All[i]);
                faceMaterials[i].mainTexture = texture != null ? texture : missingTexture;
            }

            utility.BeginPreview(rect, "PreBackground");

            Matrix4x4 matrix = Matrix4x4.Rotate(Quaternion.Euler(pitch, yaw, 0f));
            for (int i = 0; i < BlockFaces.Count; i++)
                utility.DrawMesh(cubeMesh, matrix, faceMaterials[i], i);

            utility.camera.Render();
            utility.EndAndDrawPreview(rect);
        }

        public void Dispose()
        {
            if (utility != null)
            {
                utility.Cleanup();
                utility = null;
            }

            if (faceMaterials != null)
            {
                foreach (Material material in faceMaterials)
                {
                    if (material != null)
                        UnityEngine.Object.DestroyImmediate(material);
                }

                faceMaterials = null;
            }

            if (cubeMesh != null)
            {
                UnityEngine.Object.DestroyImmediate(cubeMesh);
                cubeMesh = null;
            }

            if (missingTexture != null)
            {
                UnityEngine.Object.DestroyImmediate(missingTexture);
                missingTexture = null;
            }
        }

        private void HandleInput(Rect rect)
        {
            int controlId = GUIUtility.GetControlID("IslandGameBlockCubePreview".GetHashCode(), FocusType.Passive, rect);
            Event current = Event.current;

            switch (current.GetTypeForControl(controlId))
            {
                case EventType.MouseDown:
                    if (current.button == 0 && rect.Contains(current.mousePosition))
                    {
                        GUIUtility.hotControl = controlId;
                        current.Use();
                    }

                    break;

                case EventType.MouseDrag:
                    if (GUIUtility.hotControl == controlId)
                    {
                        AutoRotate = false;
                        yaw = Mathf.Repeat(yaw + current.delta.x * DragDegreesPerPixel, 360f);
                        pitch = Mathf.Clamp(pitch + current.delta.y * DragDegreesPerPixel, -89f, 89f);
                        current.Use();
                    }

                    break;

                case EventType.MouseUp:
                    if (GUIUtility.hotControl == controlId)
                    {
                        GUIUtility.hotControl = 0;
                        current.Use();
                    }

                    break;
            }
        }

        private void EnsureResources()
        {
            if (utility != null)
                return;

            utility = new PreviewRenderUtility();
            utility.camera.transform.position = new Vector3(0f, 0f, -3.8f);
            utility.camera.transform.rotation = Quaternion.identity;
            utility.camera.fieldOfView = 30f;
            utility.camera.nearClipPlane = 0.1f;
            utility.camera.farClipPlane = 20f;

            utility.lights[0].intensity = 1.1f;
            utility.lights[0].transform.rotation = Quaternion.Euler(45f, 35f, 0f);
            utility.lights[1].intensity = 0.45f;
            utility.lights[1].transform.rotation = Quaternion.Euler(340f, 218f, 177f);
            utility.ambientColor = new Color(0.32f, 0.32f, 0.34f, 1f);

            cubeMesh = BuildCubeMesh();
            missingTexture = CreateMissingTexture();

            // The active pipeline's default lit shader (URP Lit here) so the
            // preview matches how terrain will actually be shaded; Standard as
            // the built-in-pipeline fallback keeps the tool portable.
            Shader shader = GraphicsSettings.currentRenderPipeline != null
                ? GraphicsSettings.currentRenderPipeline.defaultShader
                : Shader.Find("Standard");

            faceMaterials = new Material[BlockFaces.Count];
            for (int i = 0; i < faceMaterials.Length; i++)
            {
                var material = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
                if (material.HasProperty("_Smoothness"))
                    material.SetFloat("_Smoothness", 0.05f);
                if (material.HasProperty("_Glossiness"))
                    material.SetFloat("_Glossiness", 0.05f);
                faceMaterials[i] = material;
            }

            lastFrameTime = EditorApplication.timeSinceStartup;
        }

        /// <summary>
        /// Unit cube, one submesh per BlockFace (enum order). Per face: texture
        /// right axis u = Cross(n, v) so textures read unmirrored from outside,
        /// and winding (0,1,2)(0,2,3) over bl,tl,tr,br is clockwise = front-facing.
        /// </summary>
        private static Mesh BuildCubeMesh()
        {
            Vector3[] faceNormals =
            {
                Vector3.up, Vector3.down, Vector3.forward, Vector3.back, Vector3.right, Vector3.left,
            };
            Vector3[] faceUps =
            {
                Vector3.forward, Vector3.forward, Vector3.up, Vector3.up, Vector3.up, Vector3.up,
            };

            var vertices = new Vector3[BlockFaces.Count * 4];
            var normals = new Vector3[BlockFaces.Count * 4];
            var uvs = new Vector2[BlockFaces.Count * 4];

            var mesh = new Mesh
            {
                name = "BlockPreviewCube",
                hideFlags = HideFlags.HideAndDontSave,
                subMeshCount = BlockFaces.Count,
            };

            for (int face = 0; face < BlockFaces.Count; face++)
            {
                Vector3 n = faceNormals[face];
                Vector3 v = faceUps[face];
                Vector3 u = Vector3.Cross(n, v);
                int baseIndex = face * 4;

                vertices[baseIndex + 0] = (n - u - v) * 0.5f;
                vertices[baseIndex + 1] = (n - u + v) * 0.5f;
                vertices[baseIndex + 2] = (n + u + v) * 0.5f;
                vertices[baseIndex + 3] = (n + u - v) * 0.5f;

                uvs[baseIndex + 0] = new Vector2(0f, 0f);
                uvs[baseIndex + 1] = new Vector2(0f, 1f);
                uvs[baseIndex + 2] = new Vector2(1f, 1f);
                uvs[baseIndex + 3] = new Vector2(1f, 0f);

                for (int i = 0; i < 4; i++)
                    normals[baseIndex + i] = n;
            }

            mesh.vertices = vertices;
            mesh.normals = normals;
            mesh.uv = uvs;

            for (int face = 0; face < BlockFaces.Count; face++)
            {
                int b = face * 4;
                mesh.SetTriangles(new[] { b, b + 1, b + 2, b, b + 2, b + 3 }, face);
            }

            return mesh;
        }

        private static Texture2D CreateMissingTexture()
        {
            const int size = 8;
            const int checker = 4;
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                name = "BlockPreviewMissingTexture",
                hideFlags = HideFlags.HideAndDontSave,
                filterMode = FilterMode.Point,
            };

            var magenta = new Color32(255, 0, 255, 255);
            var black = new Color32(0, 0, 0, 255);
            var pixels = new Color32[size * size];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                    pixels[y * size + x] = (x / checker + y / checker) % 2 == 0 ? magenta : black;
            }

            texture.SetPixels32(pixels);
            texture.Apply(false, false);
            return texture;
        }
    }
}
