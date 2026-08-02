using Bakery.WPF.ViewModels;

namespace Bakery.WPF;

public partial class SaleInvoiceDialog
{
    public SaleInvoiceDialog(SaleInvoiceDialogViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        viewModel.RequestClose += (_, result) => { DialogResult = result; Close(); };
    }
}
