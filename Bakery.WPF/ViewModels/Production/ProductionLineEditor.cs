using CommunityToolkit.Mvvm.ComponentModel;

namespace Bakery.WPF.ViewModels;

public partial class ProductionLineEditor : ObservableObject
{
    [ObservableProperty] private int index;
    [ObservableProperty] private int itemId;
    [ObservableProperty] private string itemName = string.Empty;
    [ObservableProperty] private int unitId;
    [ObservableProperty] private string unitName = string.Empty;
    [ObservableProperty] private decimal quantity;
    [ObservableProperty] private decimal unitCost;

    public decimal TotalCost => Quantity * UnitCost;
}
