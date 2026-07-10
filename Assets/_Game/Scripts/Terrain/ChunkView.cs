using UnityEngine;

namespace IslandGame.Terrain
{
    /// <summary>
    /// The scene-side half of a chunk: GameObject with MeshFilter/Renderer
    /// (two materials: opaque + cutout, matching the mesher's submeshes) and a
    /// MeshCollider. Owns its two Mesh instances for life and gets rebuilt in
    /// place — VoxelWorld pools these views aggressively while chunk DATA
    /// stays resident, so streaming never reallocates meshes.
    /// </summary>
    public sealed class ChunkView : MonoBehaviour
    {
        private MeshFilter meshFilter;
        private MeshCollider meshCollider;

        public Mesh RenderMesh { get; private set; }
        public Mesh CollisionMesh { get; private set; }

        public static ChunkView Create(Transform parent, Material opaqueMaterial, Material cutoutMaterial)
        {
            var gameObject = new GameObject("Chunk");
            gameObject.transform.SetParent(parent, false);

            var view = gameObject.AddComponent<ChunkView>();
            view.meshFilter = gameObject.AddComponent<MeshFilter>();
            var meshRenderer = gameObject.AddComponent<MeshRenderer>();
            view.meshCollider = gameObject.AddComponent<MeshCollider>();

            meshRenderer.sharedMaterials = new[] { opaqueMaterial, cutoutMaterial };
            meshRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;

            view.RenderMesh = new Mesh { name = "ChunkRender" };
            view.RenderMesh.MarkDynamic();
            view.CollisionMesh = new Mesh { name = "ChunkCollision" };
            view.meshFilter.sharedMesh = view.RenderMesh;

            return view;
        }

        /// <summary>Positions the view at a chunk coordinate and activates it.</summary>
        public void Bind(Vector2Int coord)
        {
            name = $"Chunk ({coord.x}, {coord.y})";
            transform.position = new Vector3(coord.x * Chunk.SizeX, 0f, coord.y * Chunk.SizeZ);
            gameObject.SetActive(true);
        }

        /// <summary>
        /// Call after the mesher wrote into the meshes: re-cooks the collider.
        /// Null-then-assign forces PhysX to pick up the new geometry; an empty
        /// collision mesh must never reach the collider (cook error).
        /// </summary>
        public void RefreshAfterBuild(bool hasCollision)
        {
            meshCollider.sharedMesh = null;
            if (hasCollision)
                meshCollider.sharedMesh = CollisionMesh;
        }

        public void Release()
        {
            gameObject.SetActive(false);
        }

        private void OnDestroy()
        {
            if (RenderMesh != null)
                Destroy(RenderMesh);
            if (CollisionMesh != null)
                Destroy(CollisionMesh);
        }
    }
}
