using System.Windows;
using Bakery.WPF.ViewModels;

namespace Bakery.WPF.Views;

public partial class PartyPaymentDialog : Window
{
    public PartyPaymentDialog(PartyPaymentDialogViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;

        viewModel.RequestClose += (s, result) =>
        {
            DialogResult = result;
            Close();
        };
    }
}
