using LaserWorks.Domain.Common;
using LaserWorks.Domain.Enums;

namespace LaserWorks.Domain.Costing;

/// <summary>
/// Rules shared by request, estimate and job component lines: which cost component a line feeds,
/// which sources a line may use, and whether it moves stock.
/// </summary>
public static class ComponentRules
{
    public static ComponentCategory CategoryOf(MaterialKind kind) => kind switch
    {
        MaterialKind.RawMaterial => ComponentCategory.RawMaterial,
        MaterialKind.Consumable => ComponentCategory.Consumable,
        MaterialKind.Packaging => ComponentCategory.Packaging,
        MaterialKind.Service => ComponentCategory.ExternalService,
        _ => ComponentCategory.PurchasedComponent // purchased components and bought-in finished goods
    };

    public static CostComponent CostComponentOf(ComponentCategory category) => category switch
    {
        ComponentCategory.RawMaterial => CostComponent.Material,
        ComponentCategory.PurchasedComponent => CostComponent.PurchasedComponents,
        ComponentCategory.Consumable => CostComponent.Consumables,
        ComponentCategory.Packaging => CostComponent.Packaging,
        ComponentCategory.ExternalService => CostComponent.ExternalServices,
        _ => CostComponent.OtherDirect
    };

    public static CostComponent CostComponentOf(MaterialKind kind) => CostComponentOf(CategoryOf(kind));

    /// <summary>Default source for a new line using an item of this kind.</summary>
    public static ComponentSource DefaultSource(MaterialKind? kind) => kind switch
    {
        MaterialKind.Service => ComponentSource.ExternalService,
        null => ComponentSource.ManualCost,
        _ => ComponentSource.Inventory
    };

    /// <summary>Default source for a free-text line (no item master record): bought directly for the job, outsourced, or a manual cost.</summary>
    public static ComponentSource DefaultSource(ComponentCategory category) => category switch
    {
        ComponentCategory.ExternalService => ComponentSource.ExternalService,
        ComponentCategory.OtherDirect => ComponentSource.ManualCost,
        _ => ComponentSource.DirectPurchase
    };

    /// <summary>True when the line's cost comes from stock movements (issue from inventory or a remnant).</summary>
    public static bool IsStocked(ComponentSource source) => source is ComponentSource.Inventory or ComponentSource.Remnant;

    /// <summary>Stockable item kinds (services are never held in inventory).</summary>
    public static bool IsStockable(MaterialKind kind) => kind != MaterialKind.Service;

    /// <summary>
    /// Validates the combination of source, item and category so the same cost cannot be charged twice
    /// (a stocked line is costed only by stock issues, a direct line only by its direct cost document).
    /// </summary>
    public static void Validate(ComponentSource source, MaterialKind? itemKind, ComponentCategory category, string? description)
    {
        switch (source)
        {
            case ComponentSource.Inventory:
                if (itemKind == null) throw new DomainException("Err.Required", "Material");
                if (!IsStockable(itemKind.Value)) throw new DomainException("Err.ServiceNotStocked");
                break;
            case ComponentSource.Remnant:
                if (itemKind is not MaterialKind.RawMaterial) throw new DomainException("Err.RemnantNeedsRawMaterial");
                break;
            case ComponentSource.DirectPurchase:
                if (itemKind == null && string.IsNullOrWhiteSpace(description)) throw new DomainException("Err.Required", "Description");
                break;
            case ComponentSource.ExternalService:
            case ComponentSource.ManualCost:
                if (itemKind == null && string.IsNullOrWhiteSpace(description)) throw new DomainException("Err.Required", "Description");
                break;
        }
        if (itemKind != null && source != ComponentSource.ManualCost && category != CategoryOf(itemKind.Value))
            throw new DomainException("Err.ComponentCategoryMismatch");
    }
}
