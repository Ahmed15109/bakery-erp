using System.Windows;
using Bakery.WPF.ViewModels;

namespace Bakery.WPF;

public partial class TreasuryTransferDialog : Window
{
    public TreasuryTransferDialog(TreasuryTransferDialogViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        viewModel.RequestClose += (s, e) => { DialogResult = e; Close(); };
    }
}
