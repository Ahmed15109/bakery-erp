using System.Windows;
using Bakery.WPF.ViewModels;

namespace Bakery.WPF;

public partial class TreasuryTransactionDialog : Window
{
    public TreasuryTransactionDialog(TreasuryTransactionDialogViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        viewModel.RequestClose += (s, e) => { DialogResult = e; Close(); };
    }
}
