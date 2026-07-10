using IslandGame.Data.Building;
using IslandGame.Data.Crafting;
using IslandGame.Data.Items;
using IslandGame.Inventory;
using IslandGame.Player;
using UnityEngine;

namespace IslandGame.Building
{
    /// <summary>
    /// Interact-to-remove for placed building pieces: the deconstruct button
    /// (middle mouse — Valheim's remove binding — or a "Deconstruct" input
    /// action) removes the aimed piece within reach and refunds part of its
    /// material cost to the inventory; whatever doesn't fit drops as
    /// WorldItems at the piece, the same overflow rule mining uses.
    ///
    /// REFUND is Refund Ratio × what the piece actually costs: the linked
    /// building recipe's ingredients (Phase 3 — the same recipe placement
    /// charges), or the definition's legacy MaterialCost lines for pieces
    /// without a recipe. Floored, but never below 1 while the line cost
    /// anything.
    ///
    /// Registry cleanup needs no code here: destroying the piece unregisters
    /// it via BuildingPiece.OnDestroy, the same path damage-destruction takes.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(PlayerReferences))]
    public sealed class BuildingDeconstructor : MonoBehaviour
    {
        [Tooltip("Maximum deconstruct distance from the camera, meters.")]
        [SerializeField] private float reach = 5f;

        [Tooltip("Fraction of each cost line (linked recipe ingredients, or legacy MaterialCost) returned on removal.")]
        [Range(0f, 1f)]
        [SerializeField] private float refundRatio = 0.5f;

        private PlayerReferences references;
        private InventorySystem inventory;
        private readonly RaycastHit[] rayBuffer = new RaycastHit[16];

        private void Awake()
        {
            references = GetComponent<PlayerReferences>();
            inventory = GetComponent<InventorySystem>();
        }

        private void OnEnable()
        {
            references.InputHandler.DeconstructPressed += TryDeconstruct;
        }

        private void OnDisable()
        {
            references.InputHandler.DeconstructPressed -= TryDeconstruct;
        }

        private void TryDeconstruct()
        {
            if (!PlayerAimRaycast.Raycast(references.CameraPivot, transform, reach, rayBuffer, out RaycastHit hit))
                return;

            var piece = hit.collider.GetComponentInParent<BuildingPiece>();
            if (piece == null)
                return;

            RefundMaterials(piece);
            Destroy(piece.gameObject); // OnDestroy unregisters from the registry
        }

        private void RefundMaterials(BuildingPiece piece)
        {
            BuildingPieceDefinition definition = piece.Definition;
            if (definition == null)
                return; // unresolved scene-authored piece: removable, nothing to refund

            Vector3 dropPoint = piece.transform.position + Vector3.up * 0.5f;

            // The linked building recipe is the cost authority (what placement
            // charged); MaterialCost is the fallback for recipe-less pieces.
            RecipeDatabase recipes = RecipeDatabase.Instance;
            RecipeDefinition recipe = recipes != null ? recipes.FindForPiece(definition) : null;

            if (recipe != null)
            {
                foreach (RecipeIngredient ingredient in recipe.Ingredients)
                {
                    if (ingredient != null && ingredient.Item != null)
                        RefundLine(ingredient.Item, ingredient.Count, dropPoint);
                }
            }
            else
            {
                for (int i = 0; i < definition.MaterialCost.Count; i++)
                {
                    BuildingMaterialCost line = definition.MaterialCost[i];
                    if (line != null && line.Item != null)
                        RefundLine(line.Item, line.Count, dropPoint);
                }
            }
        }

        private void RefundLine(ItemDefinition item, int cost, Vector3 dropPoint)
        {
            if (cost <= 0)
                return;

            int refund = Mathf.Max(1, Mathf.FloorToInt(cost * refundRatio));

            int added = inventory != null ? inventory.AddItem(item, refund) : 0;
            int leftover = refund - added;
            if (leftover > 0)
            {
                // Inventory full: the refund falls at the piece instead of vanishing.
                WorldItem.Spawn(item, leftover, 1f, dropPoint, Vector3.up * 1.5f);
            }
        }
    }
}
