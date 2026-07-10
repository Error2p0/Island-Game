# Block Editor — Usage Guide (Phase 2)

Open with **Island Game → World → Block Editor**. (Framework: IMGUI — chosen because the
window is built around SerializedObject property fields, PreviewRenderUtility and
AdvancedDropdown, all IMGUI-native, and it matches the project's existing editor tools.)

## Layout

- **Left**: search box (matches display name, ID or asset name), **New Block** button,
  and the list of every block in the BlockDatabase. Right-click a row for
  *Ping Asset* / *Delete…*. A ● marker means unsaved changes.
- **Right**: live rotating 3D cube preview (drag to rotate manually — this pauses
  Auto-Rotate; toolbar has Auto-Rotate toggle and Reset View), then the inspector
  fields, then the Save / Revert / Delete row.

## Editing model — read this once

Edits happen in an **unapplied buffer**, not on the asset. Nothing touches disk until
you press **Save** (or Unity's close-window prompt, or the auto-save that runs before
recompile/play mode, since edit buffers don't survive domain reloads). **Revert**
discards the buffer back to the on-disk state. Switching blocks or creating/deleting
with unsaved changes prompts Save / Discard / Cancel.

## Creating a block

1. **New Block** → asset is created in `Assets/_Game/Content/Blocks` (the project
   content convention) with a unique name; the databases resync automatically.
2. Set Display Name and ID. The ID warns live if it's empty or collides with another
   block's ID. IDs are immutable once terrain saves use them — get them right early.
3. Textures: leave **Same Texture On All Faces** on and assign one texture, or turn it
   off for per-face slots. Empty per-face slots fall back to the Fallback Texture;
   **Copy Fallback To All Face Slots** quick-fills them when you want to vary only some
   faces. Every face shows a thumbnail, and the cube preview updates live with unsaved
   edits. Magenta/black checker = missing texture (or not Read/Write enabled).
4. Drop Item: use the searchable dropdown (sourced from ItemDatabase) or drag an
   ItemDefinition into the object field beside it.
5. **Save**.

## Deleting

Delete via the row's context menu or the **Delete Block…** button. If any item's
Placed Block points at this block, the confirmation lists those references before you
commit (later phases extend this check to recipes etc.). Deletion resyncs the databases.

## Troubleshooting

- "BlockDatabase asset not found" → press **Sync Databases Now** (or
  Island Game → Data → Sync Databases).
- Preview shows checker on all faces → no texture assigned, or texture importer lacks
  Read/Write. The atlas has the same requirement, so fix it in the importer.
