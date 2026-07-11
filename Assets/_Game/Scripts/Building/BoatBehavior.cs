using IslandGame.Player;
using IslandGame.Terrain;
using UnityEngine;

namespace IslandGame.Building
{
    /// <summary>
    /// The rideable watercraft — a minimal, complete vehicle-possession loop:
    ///
    ///   PLACE — free-placed like any functional piece. Water blocks have no
    ///   colliders (voxel liquids are volume data), so the placement ray hits
    ///   the seafloor; Init therefore snaps the hull UP to the water surface,
    ///   found by the same column probe the swimming sensor uses.
    ///
    ///   RIDE — Interact seats the player: PlayerLocomotion and the
    ///   CharacterController are DISABLED (the possession override — normal
    ///   movement code never fights the boat) and the player parents to the
    ///   seat. While possessed, this component reads MoveInput directly from
    ///   the shared PlayerInputHandler: W/S thrust along the hull, A/D turn.
    ///   Mouse look stays native (the camera rig is untouched).
    ///
    ///   DRIVE — kinematic, not Rigidbody physics: the hull holds the water
    ///   surface (with a gentle bob), and thrust is blocked when the bow
    ///   would enter a solid cell at the waterline — beaching stops you,
    ///   reversing frees you. A buoyancy sim adds nothing at this fidelity.
    ///
    ///   EXIT — Interact again (the aim ray hits the hull from the seat)
    ///   restores the CharacterController, then PlayerLocomotion, unparents,
    ///   and drops the rider beside the hull. OnDestroy force-exits so a
    ///   deconstructed boat can never strand a disabled player.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class BoatBehavior : MonoBehaviour, IFunctionalPlaceable
    {
        [Header("Drive")]
        [SerializeField] private float moveSpeed = 5.5f;

        [Tooltip("Degrees per second at full rudder.")]
        [SerializeField] private float turnSpeed = 55f;

        [Header("Hull")]
        [Tooltip("Rider anchor. Wired by the content creator; falls back to a child named 'Seat'.")]
        [SerializeField] private Transform seat;

        [Tooltip("How deep the hull sits below the water surface.")]
        [SerializeField] private float draft = 0.12f;

        [Tooltip("Distance from center to the bow, for the beaching check.")]
        [SerializeField] private float bowOffset = 1.3f;

        [SerializeField] private float bobAmplitude = 0.05f;

        private BuildingPiece piece;
        private VoxelWorld world;
        private PlayerReferences rider;
        private Transform riderOriginalParent;
        private float bobPhase;

        public BuildingPiece Piece => piece;

        public bool IsRidden => rider != null;

        public string InteractionPrompt => IsRidden ? "Leave boat" : "Ride boat";

        public void Init(BuildingPiece owner)
        {
            piece = owner;

            if (seat == null)
                seat = transform.Find("Seat");
            if (world == null)
                world = FindFirstObjectByType<VoxelWorld>();

            // Placement ray hit the seafloor through the water — float up.
            if (world != null && TryFindWaterSurface(transform.position + Vector3.up * 0.5f, out float surfaceY))
                transform.position = new Vector3(transform.position.x, surfaceY - draft, transform.position.z);
        }

        public void Interact(GameObject interactor)
        {
            if (IsRidden)
            {
                Exit();
                return;
            }

            var references = interactor.GetComponent<PlayerReferences>();
            if (references == null || seat == null)
                return;

            rider = references;
            riderOriginalParent = references.transform.parent;

            // The possession override: normal movement fully off, camera untouched.
            if (references.Locomotion != null)
                references.Locomotion.enabled = false;
            if (references.Controller != null)
                references.Controller.enabled = false;

            references.transform.SetParent(seat, worldPositionStays: false);
            references.transform.localPosition = Vector3.zero;
            references.transform.localRotation = Quaternion.identity;
        }

        private void Update()
        {
            if (rider == null)
                return;

            Vector2 input = rider.InputHandler != null ? rider.InputHandler.MoveInput : Vector2.zero;

            transform.Rotate(0f, input.x * turnSpeed * Time.deltaTime, 0f, Space.World);

            if (!Mathf.Approximately(input.y, 0f))
            {
                Vector3 step = transform.forward * (input.y * moveSpeed * Time.deltaTime);
                Vector3 probe = transform.position + transform.forward * (Mathf.Sign(input.y) * bowOffset) + step;
                if (CanOccupy(probe))
                    transform.position += step;
            }

            // Hold the water line with a gentle bob.
            if (world != null && TryFindWaterSurface(transform.position + Vector3.up * 0.5f, out float surfaceY))
            {
                bobPhase += Time.deltaTime * 1.4f;
                float targetY = surfaceY - draft + Mathf.Sin(bobPhase) * bobAmplitude;
                Vector3 position = transform.position;
                position.y = Mathf.Lerp(position.y, targetY, 6f * Time.deltaTime);
                transform.position = position;
            }
        }

        private void Exit()
        {
            if (rider == null)
                return;

            PlayerReferences references = rider;
            rider = null;

            references.transform.SetParent(riderOriginalParent, worldPositionStays: true);
            references.transform.position = transform.position + transform.right * 1.6f + Vector3.up * 1.0f;
            references.transform.rotation = Quaternion.Euler(0f, references.transform.eulerAngles.y, 0f);

            // Controller first (it must be alive before locomotion moves it).
            if (references.Controller != null)
                references.Controller.enabled = true;
            if (references.Locomotion != null)
                references.Locomotion.enabled = true;
        }

        private void OnDestroy()
        {
            Exit(); // never strand a disabled player on a deconstructed boat
        }

        // ------------------------------------------------------------------
        // Water sampling (same column-probe idea as PlayerVoxelWaterSensor)
        // ------------------------------------------------------------------

        /// <summary>True when the waterline cell at the position is open water (liquid, not solid land/ice).</summary>
        private bool CanOccupy(Vector3 position)
        {
            if (world == null)
                return true;

            var cell = Vector3Int.FloorToInt(new Vector3(position.x, transform.position.y - draft - 0.1f, position.z));
            var block = world.GetBlock(cell);
            if (block == null)
                return true; // air (shallow edge) — allow, the bob keeps us afloat

            return block.HasBehavior(IslandGame.Data.Blocks.BlockBehaviorFlags.Liquid);
        }

        private bool TryFindWaterSurface(Vector3 position, out float surfaceY)
        {
            var cell = Vector3Int.FloorToInt(position);

            for (int dy = 1; dy >= -4; dy--)
            {
                var probe = new Vector3Int(cell.x, cell.y + dy, cell.z);
                if (probe.y < 0 || probe.y >= Chunk.SizeY || !IsLiquid(probe))
                    continue;

                int top = probe.y + 1;
                while (top < Chunk.SizeY && IsLiquid(new Vector3Int(cell.x, top, cell.z)))
                    top++;

                surfaceY = top;
                return true;
            }

            surfaceY = 0f;
            return false;
        }

        private bool IsLiquid(Vector3Int cell)
        {
            var block = world.GetBlock(cell);
            return block != null && block.HasBehavior(IslandGame.Data.Blocks.BlockBehaviorFlags.Liquid);
        }
    }
}
