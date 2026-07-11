using IslandGame.Data.Items;
using UnityEngine;

namespace IslandGame.Combat
{
    /// <summary>
    /// The minimal projectile the bow needed to be complete content: manual
    /// ballistic flight (no Rigidbody — a raycast along each frame's travel
    /// segment can never tunnel through thin walls at high speed), reduced
    /// gravity for an arrow-like arc, IDamageable damage through the same
    /// DamageInfo path melee uses, and stick-then-despawn on any surface.
    /// Spawned by PlayerWeaponAttack for IsRangedWeapon items; the visual is
    /// a stretched shaft primitive so no art asset is required.
    /// </summary>
    public sealed class Projectile : MonoBehaviour
    {
        private const float GravityScale = 0.4f;   // arrows arc, but flatter than a thrown rock
        private const float MaxLifetimeSeconds = 8f;
        private const float StickDespawnSeconds = 10f;

        private static readonly RaycastHit[] castBuffer = new RaycastHit[8];

        private Vector3 velocity;
        private float damage;
        private DamageType damageType;
        private GameObject source;
        private Transform ignoreRoot;
        private float dieAt;
        private bool stuck;

        /// <summary>Spawns a flying projectile. ignoreRoot (the shooter's rig) is transparent to its flight.</summary>
        public static Projectile Spawn(
            Vector3 position, Vector3 direction, float speed,
            float damage, DamageType damageType, GameObject source, Transform ignoreRoot)
        {
            var root = new GameObject("Projectile");
            root.transform.SetPositionAndRotation(position, Quaternion.LookRotation(direction));

            // Shaft visual from a primitive — collider removed, flight is raycast-driven.
            GameObject shaft = GameObject.CreatePrimitive(PrimitiveType.Cube);
            shaft.name = "Shaft";
            shaft.transform.SetParent(root.transform, false);
            shaft.transform.localScale = new Vector3(0.03f, 0.03f, 0.5f);
            shaft.transform.localPosition = new Vector3(0f, 0f, -0.25f);
            Destroy(shaft.GetComponent<Collider>());

            var projectile = root.AddComponent<Projectile>();
            projectile.velocity = direction.normalized * Mathf.Max(1f, speed);
            projectile.damage = damage;
            projectile.damageType = damageType;
            projectile.source = source;
            projectile.ignoreRoot = ignoreRoot;
            projectile.dieAt = Time.time + MaxLifetimeSeconds;
            return projectile;
        }

        private void Update()
        {
            if (stuck)
                return;

            if (Time.time >= dieAt)
            {
                Destroy(gameObject);
                return;
            }

            velocity += Physics.gravity * (GravityScale * Time.deltaTime);
            Vector3 step = velocity * Time.deltaTime;
            float distance = step.magnitude;
            if (distance <= 0f)
                return;

            int hitCount = Physics.RaycastNonAlloc(
                transform.position, step / distance, castBuffer, distance, ~0, QueryTriggerInteraction.Ignore);

            int best = -1;
            for (int i = 0; i < hitCount; i++)
            {
                if (ignoreRoot != null && castBuffer[i].collider.transform.IsChildOf(ignoreRoot))
                    continue;
                if (best < 0 || castBuffer[i].distance < castBuffer[best].distance)
                    best = i;
            }

            if (best < 0)
            {
                transform.position += step;
                transform.rotation = Quaternion.LookRotation(velocity);
                return;
            }

            RaycastHit hit = castBuffer[best];

            var damageable = hit.collider.GetComponentInParent<IDamageable>();
            if (damageable != null)
            {
                var info = new DamageInfo(damage, damageType, hit.point, velocity.normalized, source);
                damageable.ApplyDamage(in info);
            }

            // Stick in the surface (nudged in so the shaft reads as embedded),
            // then quietly despawn.
            stuck = true;
            transform.position = hit.point + velocity.normalized * 0.05f;
            Destroy(gameObject, StickDespawnSeconds);
        }
    }
}
