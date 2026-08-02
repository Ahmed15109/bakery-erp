using System.Windows;
using Bakery.WPF.ViewModels;

namespace Bakery.WPF;

public partial class ReverseTransactionDialog : Window
{
    public ReverseTransactionDialog()
    {
        InitializeComponent();
    }

    protected override void OnActivated(EventArgs e)
    {
        base.OnActivated(e);
        if (DataContext is ReverseTransactionDialogViewModel viewModel)
        {
            viewModel.RequestClose -= ViewModel_RequestClose;
            viewModel.RequestClose += ViewModel_RequestClose;
        }
    }

    private void ViewModel_RequestClose(object? sender, bool result)
    {
        DialogResult = result;
        Close();
    }
}
