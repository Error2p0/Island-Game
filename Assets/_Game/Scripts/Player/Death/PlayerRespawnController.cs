using IslandGame.Data.Stats;
using IslandGame.Inventory.UI;
using IslandGame.Player.UI;
using IslandGame.Stats;
using UnityEngine;
using UnityEngine.InputSystem;

namespace IslandGame.Player
{
    /// <summary>
    /// The real death flow, subscribed to the stats system's OnPlayerDeath
    /// hook (this is the phase that hook waited for — PlayerHealth's
    /// placeholder respawn goes dormant via ExternalRespawnHandling; how
    /// health depletes and how the event fires are untouched):
    ///
    ///   DEATH    — capture the cause, run the DeathPenaltyPolicy (asset-
    ///     swappable strategy; default drops the backpack into a gravestone),
    ///     show the death screen and take UI focus (the same refcounted
    ///     UIInputFocus every menu uses — gameplay input blocks, cursor
    ///     frees; PlayerLocomotion itself is never touched).
    ///
    ///   RESPAWN  — after the configurable delay, on button/Space: teleport
    ///     to the respawn point (last bed the player slept in, else the
    ///     recorded world spawn), then set the post-respawn stats: Health,
    ///     Stamina and Warmth FULL, Hunger and Thirst LOW. Rationale: full
    ///     health/warmth prevents the respawn→instant-death spiral (and a
    ///     freezing respawn would just be the same spiral in a coat), while
    ///     waking up famished makes death cost real momentum — the first
    ///     order of business is food and water, so dying is never free.
    ///
    ///   MARKER   — the death marker HUD points at whatever the penalty
    ///     dropped until it's been emptied (session-scoped; the gravestone
    ///     itself persists through the save system as an ordinary piece).
    ///
    /// The respawn POINT persists through the save system's additive-field
    /// mechanism (SavedPlayer.hasRespawnPoint & friends); the bed sets it via
    /// SetRespawnPoint on successful sleep.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(PlayerHealth))]
    [RequireComponent(typeof(StatContainer))]
    public sealed class PlayerRespawnController : MonoBehaviour
    {
        [Header("Penalty")]
        [Tooltip("The swappable what-does-dying-cost strategy. Default asset: DropBackpackDeathPenalty (backpack → gravestone, hotbar kept).")]
        [SerializeField] private DeathPenaltyPolicy penaltyPolicy;

        [Header("Respawn Flow")]
        [Tooltip("Seconds before the Respawn button unlocks — a beat to register the death.")]
        [Range(0f, 15f)]
        [SerializeField] private float respawnDelaySeconds = 3f;

        [Header("Post-Respawn Stats (fraction of modified max)")]
        [Range(0f, 1f)]
        [SerializeField] private float healthFraction = 1f;

        [Range(0f, 1f)]
        [SerializeField] private float staminaFraction = 1f;

        [Range(0f, 1f)]
        [SerializeField] private float warmthFraction = 1f;

        [Range(0f, 1f)]
        [SerializeField] private float hungerFraction = 0.3f;

        [Range(0f, 1f)]
        [SerializeField] private float thirstFraction = 0.3f;

        [Header("UI (wired by the builder; auto-resolved when empty)")]
        [SerializeField] private DeathScreenView deathScreen;
        [SerializeField] private DeathMarkerView deathMarker;

        private PlayerReferences references;
        private PlayerHealth health;
        private PlayerSurvival survival;
        private StatContainer statContainer;

        private Vector3 worldSpawnPosition;
        private Quaternion worldSpawnRotation;
        private bool deathScreenOpen;
        private float respawnUnlockTime;

        /// <summary>True once a bed set a respawn point (persisted by the save system).</summary>
        public bool HasRespawnPoint { get; private set; }

        /// <summary>Bed respawn position — meaningful only while HasRespawnPoint.</summary>
        public Vector3 RespawnPosition { get; private set; }

        /// <summary>Bed respawn yaw, degrees — meaningful only while HasRespawnPoint.</summary>
        public float RespawnYaw { get; private set; }

        private void Awake()
        {
            references = GetComponent<PlayerReferences>();
            health = GetComponent<PlayerHealth>();
            survival = GetComponent<PlayerSurvival>();
            statContainer = GetComponent<StatContainer>();
        }

        private void Start()
        {
            // Same convention as PlayerHealth: the world spawn is wherever
            // scene-load/Build-Everything placement settled the player.
            worldSpawnPosition = transform.position;
            worldSpawnRotation = transform.rotation;

            if (penaltyPolicy == null)
                Debug.LogWarning("[Respawn] No DeathPenaltyPolicy assigned — dying will drop nothing.", this);

            ResolveViews();
        }

        private void OnEnable()
        {
            health.ExternalRespawnHandling = true;
            health.OnPlayerDeath += OnDeath;
        }

        private void OnDisable()
        {
            health.ExternalRespawnHandling = false;
            health.OnPlayerDeath -= OnDeath;

            if (deathScreenOpen)
            {
                deathScreenOpen = false;
                UIInputFocus.Release(references.InputHandler);
                if (deathScreen != null)
                    deathScreen.Hide();
            }
        }

        private void Update()
        {
            if (!deathScreenOpen)
                return;

            UIInputFocus.EnforceCursor();

            bool unlocked = Time.time >= respawnUnlockTime;
            if (deathScreen != null)
                deathScreen.SetRespawnAvailable(unlocked, respawnUnlockTime - Time.time);

            if (unlocked && Keyboard.current != null && Keyboard.current.spaceKey.wasPressedThisFrame)
                RespawnNow();
        }

        // ------------------------------------------------------------------
        // Bed API (BedBehavior calls this on successful sleep) + save hooks
        // ------------------------------------------------------------------

        /// <summary>Sets the bed respawn point (world position + facing yaw).</summary>
        public void SetRespawnPoint(Vector3 position, float yawDegrees)
        {
            HasRespawnPoint = true;
            RespawnPosition = position;
            RespawnYaw = yawDegrees;
            Debug.Log($"[Respawn] Respawn point set at ({position.x:0.#}, {position.y:0.#}, {position.z:0.#}).");
        }

        /// <summary>Load path: same write, no log (the save system restores silently).</summary>
        public void RestoreRespawnPoint(Vector3 position, float yawDegrees)
        {
            HasRespawnPoint = true;
            RespawnPosition = position;
            RespawnYaw = yawDegrees;
        }

        // ------------------------------------------------------------------
        // Death → screen → respawn
        // ------------------------------------------------------------------

        private void OnDeath()
        {
            Vector3 deathPosition = transform.position;

            // Penalty first, while the corpse is still where it fell.
            Transform lootTarget = null;
            if (penaltyPolicy != null)
                lootTarget = penaltyPolicy.Apply(gameObject, deathPosition);

            ResolveViews();
            if (deathMarker != null && lootTarget != null)
                deathMarker.SetTarget(lootTarget);

            deathScreenOpen = true;
            respawnUnlockTime = Time.time + respawnDelaySeconds;
            UIInputFocus.Acquire(references.InputHandler);

            if (deathScreen != null)
            {
                deathScreen.Show(BuildCauseText(), RespawnNow);
                deathScreen.SetRespawnAvailable(false, respawnDelaySeconds);
            }
            else
            {
                // No UI in this scene: still honor the delay via Update? No
                // screen means no button — respawn immediately instead of
                // trapping the player in a black-less limbo.
                RespawnNow();
            }
        }

        private void RespawnNow()
        {
            if (!health.IsDead)
                return;

            if (deathScreenOpen)
            {
                deathScreenOpen = false;
                UIInputFocus.Release(references.InputHandler);
                if (deathScreen != null)
                    deathScreen.Hide();
            }

            Vector3 position = HasRespawnPoint ? RespawnPosition : worldSpawnPosition;
            Quaternion rotation = HasRespawnPoint ? Quaternion.Euler(0f, RespawnYaw, 0f) : worldSpawnRotation;

            health.ExternalRespawnAt(position, rotation);
            ApplyPostRespawnStats();

            Debug.Log($"[Respawn] Respawned at {(HasRespawnPoint ? "bed" : "world spawn")}.");
        }

        private void ApplyPostRespawnStats()
        {
            SetStatFraction(StatIds.Health, healthFraction);
            SetStatFraction(StatIds.Stamina, staminaFraction);
            SetStatFraction(StatIds.Warmth, warmthFraction);
            SetStatFraction(StatIds.Hunger, hungerFraction);
            SetStatFraction(StatIds.Thirst, thirstFraction);
        }

        private void SetStatFraction(string statId, float fraction)
        {
            float max = statContainer.GetModifiedValue(statId);
            if (max > 0f)
                statContainer.SetCurrent(statId, max * Mathf.Clamp01(fraction));
        }

        private string BuildCauseText()
        {
            // Freezing bypasses ApplyDamage (direct stat drain), so ask the
            // survival rules first; then a recent recorded hit; else generic.
            if (survival != null && survival.IsFreezing)
                return "You froze to death.";

            if (!string.IsNullOrEmpty(health.LastDamageSourceName)
                && Time.time - health.LastDamageTime < 8f)
                return $"Slain by {health.LastDamageSourceName}.";

            return "You died.";
        }

        private void ResolveViews()
        {
            if (deathScreen == null)
                deathScreen = FindFirstObjectByType<DeathScreenView>(FindObjectsInactive.Include);
            if (deathMarker == null)
                deathMarker = FindFirstObjectByType<DeathMarkerView>(FindObjectsInactive.Include);
        }
    }
}
