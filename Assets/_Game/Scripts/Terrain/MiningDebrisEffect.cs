using System.Collections.Generic;
using IslandGame.Data.Blocks;
using UnityEngine;
using UnityEngine.Rendering;

namespace IslandGame.Terrain
{
    /// <summary>
    /// Lightweight mining feedback: one shared world-space ParticleSystem that
    /// bursts a handful of small gravity-affected chips at the hit point,
    /// tinted with the mined block's average texture color — reads as "bits
    /// of that material" without any per-block art or physics debris objects.
    /// Deliberately NOT a fracture simulation: a dozen billboard particles
    /// per bite, pooled by the particle system itself, zero per-hit
    /// allocation.
    ///
    /// Auto-created on first use (GetOrCreate) — no scene setup, no builder
    /// step. Average colors are computed once per block definition from its
    /// top-face texture (block textures are Read/Write enabled per the atlas
    /// convention) and cached for the session.
    /// </summary>
    public sealed class MiningDebrisEffect : MonoBehaviour
    {
        private static MiningDebrisEffect instance;

        private static readonly Dictionary<BlockDefinition, Color> colorCache =
            new Dictionary<BlockDefinition, Color>();

        private ParticleSystem system;
        private Material material;

        /// <summary>Scene singleton, created on demand.</summary>
        public static MiningDebrisEffect GetOrCreate()
        {
            if (instance == null)
            {
                instance = FindFirstObjectByType<MiningDebrisEffect>();
                if (instance == null)
                    instance = new GameObject("MiningDebris").AddComponent<MiningDebrisEffect>();
            }

            return instance;
        }

        /// <summary>Bursts chip particles of the given block's color at a world position.</summary>
        public void EmitBurst(Vector3 position, BlockDefinition block, int count)
        {
            EmitBurst(position, GetBlockColor(block), count);
        }

        /// <summary>Bursts chip particles of an explicit color — building pieces and other non-block destructibles share the effect.</summary>
        public void EmitBurst(Vector3 position, Color color, int count)
        {
            EnsureSystem();

            var emitParams = new ParticleSystem.EmitParams
            {
                position = position,
                applyShapeToPosition = true,
                startColor = color,
            };
            system.Emit(emitParams, count);
        }

        // ------------------------------------------------------------------
        // Internals
        // ------------------------------------------------------------------

        private void EnsureSystem()
        {
            if (system != null)
                return;

            system = gameObject.AddComponent<ParticleSystem>();

            ParticleSystem.MainModule main = system.main;
            main.loop = false;
            main.playOnAwake = false;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.35f, 0.7f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(1.2f, 2.8f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.05f, 0.12f);
            main.gravityModifier = 1.6f;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 256;

            ParticleSystem.EmissionModule emission = system.emission;
            emission.rateOverTime = 0f; // bursts only, via Emit

            ParticleSystem.ShapeModule shape = system.shape;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = 0.06f;

            var renderer = GetComponent<ParticleSystemRenderer>();
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;

            // Vertex-color friendly under URP and built-in alike; null main
            // texture renders as solid squares — exactly the chip look.
            material = new Material(Shader.Find("Sprites/Default"));
            renderer.sharedMaterial = material;
        }

        private static Color GetBlockColor(BlockDefinition block)
        {
            if (block == null)
                return new Color(0.6f, 0.6f, 0.6f);

            if (colorCache.TryGetValue(block, out Color cached))
                return cached;

            Color color = ComputeAverageColor(block.GetFaceTexture(BlockFace.Top));
            colorCache.Add(block, color);
            return color;
        }

        private static Color ComputeAverageColor(Texture2D texture)
        {
            var fallback = new Color(0.6f, 0.6f, 0.6f);
            if (texture == null || !texture.isReadable)
                return fallback;

            Color32[] pixels = texture.GetPixels32();
            if (pixels.Length == 0)
                return fallback;

            // Stride so a 64×64 icon costs the same as a 16×16 block texture.
            int stride = Mathf.Max(1, pixels.Length / 64);
            float r = 0f, g = 0f, b = 0f;
            int samples = 0;
            for (int i = 0; i < pixels.Length; i += stride)
            {
                r += pixels[i].r;
                g += pixels[i].g;
                b += pixels[i].b;
                samples++;
            }

            float inverse = 1f / (samples * 255f);
            return new Color(r * inverse, g * inverse, b * inverse);
        }

        private void OnDestroy()
        {
            if (material != null)
                Destroy(material);
            if (instance == this)
                instance = null;
        }
    }
}
