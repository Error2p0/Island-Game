using UnityEngine;

namespace IslandGame.Combat
{
    /// <summary>
    /// Minimal IDamageable for testing the weapon hook until real enemies
    /// exist: logs every hit, tracks health, dies by deactivating. Drop it on
    /// any object with a collider (e.g. a cube on the beach) and whack it.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class DamageableTestDummy : MonoBehaviour, IDamageable
    {
        [Min(1f)]
        [SerializeField] private float maxHealth = 50f;

        private float health;

        public float Health => health;

        private void Awake()
        {
            health = maxHealth;
        }

        public void ApplyDamage(in DamageInfo damage)
        {
            health = Mathf.Max(0f, health - damage.Amount);
            Debug.Log(
                $"[DamageableTestDummy] '{name}' took {damage.Amount:0.#} {damage.Type} damage from " +
                $"'{(damage.Source != null ? damage.Source.name : "unknown")}' — {health:0.#}/{maxHealth:0.#} HP left.",
                this);

            if (health <= 0f)
            {
                Debug.Log($"[DamageableTestDummy] '{name}' destroyed.", this);
                gameObject.SetActive(false);
            }
        }
    }
}
