using PristonTale.Simulation.Items;

namespace PristonTale.Simulation.Characters;

// The wardrobe: one costume worn over the armour. Wearing takes it out of its bag page, taking it off puts it back in the
// page shown. It changes the body drawn, adds its item's roll to the equipment totals once (EquipmentTotals.Sum) and takes
// no equipment slot: the armour under it stays worn.
public sealed partial class PlayerCharacter
{
    // The costume in the wardrobe, or null.
    public ItemInstance Costume { get; private set; }

    public Guid CostumeItemId => Costume?.InstanceId ?? Guid.Empty;

    public string CostumeKey => Costume?.Definition.CatalogKey ?? "";

    // The catalog's wear rule for this character's body: a costume for the other gender is refused.
    public bool MayWearCostume(string key) => CostumeCatalog.Shipped.Allows(key, ClassProfiles.Of(Class.Class).Female);

    // The bag costume goes into the wardrobe; one worn before goes back where it came from.
    public bool TryWearCostume(Guid itemId)
    {
        var bag = Inventory.Find(itemId) >= 0 ? Inventory : HiddenBag.Find(itemId) >= 0 ? HiddenBag : null;
        if (itemId == Guid.Empty || bag is null)
        {
            return false;
        }

        var anchor = bag.Find(itemId);
        var item = bag[anchor];
        if (!Wears(item))
        {
            return false;
        }

        bag.TakeAt(anchor);
        if (Costume is { } worn && !bag.TryPlace(worn, anchor) && !bag.TryPlaceFirstFree(worn))
        {
            bag.TryPlace(item, anchor);
            return false;
        }

        Costume = item;
        OnInventoryChanged();
        return true;
    }

    // The worn costume back into the first free place of the page shown; refused while that page is full.
    public bool TryClearCostume()
    {
        if (Costume is null || !Inventory.TryAdd(Costume))
        {
            return false;
        }

        Costume = null;
        OnInventoryChanged();
        return true;
    }

    // A saved costume into the wardrobe, as saved and without a change; false when this character may not wear it or one
    // is there already.
    public bool RestoreCostume(ItemInstance item)
    {
        if (Costume is not null || item is null || !Wears(item))
        {
            return false;
        }

        Costume = item;
        _derivedStale = true;
        return true;
    }

    private bool Wears(ItemInstance item) => CostumeCatalog.Shipped.Wears(item.Definition.CatalogKey, item.Count, ClassProfiles.Of(Class.Class).Female);
}
