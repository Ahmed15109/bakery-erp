using Bakery.WPF.ViewModels;

namespace Bakery.WPF;

public partial class InventoryAdjustmentDialog
{
    public InventoryAdjustmentDialog(InventoryAdjustmentDialogViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        viewModel.RequestClose += (_, result) => { DialogResult = result; Close(); };
    }
}
