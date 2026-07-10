# Held Items — Phase 9 (Sockets, Upper-Body Layer, Two-Hand IK, Use)

## Setup (two steps, in order)

1. **Island Game → Player → Create Hold Sockets** — resolves the hand/chest bones
   through the humanoid avatar and creates:
   - `Socket_RightHand` under the right-hand bone (local +0.08 X ≈ palm center)
   - `Socket_LeftHand` under the left-hand bone (mirrored)
   - `Socket_TwoHanded` under the chest (0, 0.1, 0.35 — centered two-handed
     reference point)
   …then adds `ItemHoldController` + `PlayerWeaponAttack` to the player and wires the
   socket references. Idempotent; tune socket local offsets in the scene if items sit
   oddly in the palm (per-item fit stays on the items, authored in the Item Editor's
   Hold Offset preview — the numbers transfer 1:1 by design).
2. **Tools → Island Game → Build Player Animations & Controller** — regenerates the
   Animator (the Animations folder is fully generated; this is the established
   workflow). Save the scene.

Runtime safety net: missing sockets are auto-created at runtime with the same
defaults, so the system degrades gracefully — but the builder route persists them for
scene tuning.

## What the Animator gained (upper-body layer only — base layers untouched)

- **Parameters**: `HoldPose` (Int: 0 empty, 1 one-handed/off-hand, 2 two-handed),
  `Use` (Trigger).
- **Layer "UpperBody"** (arms + fingers mask, Override, weight driven by code):
  - States: `Empty` (no motion, default), `OneHandedHold`, `TwoHandedHold`,
    `Use` (0.45 s swing clip).
  - Transitions: Empty↔One↔Two on `HoldPose` equals-conditions (0.15–0.2 s
    crossfades); AnyState→Use on the trigger (0.08 s, self-transition allowed so
    rapid swings restart); Use→matching hold pose at 85 % exit time.
- **New generated clips**: `HoldOneHanded`, `HoldTwoHanded`, `UseSwing` — arm-muscle
  curves only (no RootT/legs/spine), because the layer mask owns only the arms and
  the base layer must keep owning body height and stance.
- The **layer weight** blends toward 1 only while an item is held — empty-handed
  play keeps the base layer's walk/sprint arm swing pixel-identical to before.

## Runtime behavior

- `ItemHoldController` subscribes to `HotbarSelector.EquippedItemChanged`: equips
  instantiate the world model under the right socket (right hand for
  OneHanded/TwoHanded, left hand for OffHand or LeftHand-authored items) with the
  authored local offset; unequips destroy it. Held instances are stripped of
  colliders/rigidbodies immediately so they can't shove the CharacterController.
- **Two-hand IK** is bone-based in LateUpdate (`TwoBoneIK`, same technique as
  PlayerFootGrounder — this project deliberately avoids Mecanim IK goals): the left
  hand grips the model's **`OffHandGrip`** child (name it exactly that on two-handed
  world-model prefabs, e.g. lower on the axe handle), or the configurable fallback
  offset below the item origin when absent. An elbow hint keeps the bend natural.
- **Movement respect**: Swimming holsters the item (hidden + layer fades out — arms
  stroke); Prone fades the layer (arms crawl); crouch/sprint/jump keep the hold.
  Equipping never changes inventory contents, so carry weight is untouched.
- **Use wiring** (`PlayerWeaponAttack`): holding the use button swings at the item's
  `AttacksPerSecond` (visuals for both mining and combat). Damage applies only for
  `IsWeapon` items when the crosshair is NOT on voxel terrain — a sphere cast
  (radius 0.25, range `AttackRange`) hits the first `IDamageable` with
  `WeaponDamage`/`DamageType`. Mining stays 100 % owned by Phase 6's
  PlayerBlockInteraction, which already enforces Phase 3 tier/efficiency data.
- `IDamageable` + `DamageInfo` (Scripts/Combat) is the minimal hook future
  enemies/destructibles implement. `DamageableTestDummy` (health + logs) exists for
  testing.

## Test checklist

1. **Equip one-handed**: select the Stone Pickaxe (1–9). Arms crossfade into the
   one-handed pose, the pickaxe model appears in the right palm at its authored
   offset. Deselect (empty slot) — pose blends out, arms swing normally again.
2. **Equip two-handed**: set some item's Hold Type to TwoHanded in the Item Editor
   (give its prefab an `OffHandGrip` child to see exact gripping). Equip — both
   arms rise and the left hand locks onto the grip point, elbow bending naturally;
   walk/sprint and watch the lower body animate independently.
3. **Mine permitted**: pickaxe on stone — swings play at the tool's cadence while
   the block breaks at the Phase 6 speed. Bare hands on plank markers still work.
4. **Mine unpermitted**: bare hands (or a tier-0 item) on stone — swings play but
   the block never breaks (tier-blocked, exactly as Phase 6 documents).
5. **Attack**: cube + `DamageableTestDummy` on the beach; equip a weapon-flagged
   item (Item Editor: Is Weapon on the pickaxe works), hold LMB aiming at the cube —
   console logs damage at the attack rate; the cube deactivates at 0 HP. Aim at
   terrain instead — no damage calls (mining owns terrain).
6. **Rapid switching**: scroll wildly across pickaxe/blocks/empty slots — poses
   crossfade cleanly, exactly one held model ever exists, no stuck Use state
   (trigger resets via the pose transitions), console clean.
7. **Movement integration**: with the item held — crouch (pose persists over
   crouched legs), sprint, jump; go prone (arms fade to crawl, item stays in hand),
   swim (item holsters, stroke animation unaffected, re-appears on exit). No camera
   clipping or transition breaks; carry-weight speed identical before/after equip.
