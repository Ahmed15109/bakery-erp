namespace Bakery.Domain.Enums;

public enum InventoryMovementType
{
    OpeningBalance = 1,
    Purchase = 2,
    Sale = 3,
    ProductionConsume = 4,
    ProductionProduce = 5,
    Waste = 6,
    Adjustment = 7,
    Transfer = 8
}
