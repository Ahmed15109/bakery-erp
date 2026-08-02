using System.Windows;
using Bakery.WPF.ViewModels;

namespace Bakery.WPF.Views;

public partial class ReopenWorkingDayDialog : Window
{
    public ReopenWorkingDayDialog(ReopenWorkingDayDialogViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        viewModel.RequestClose += (_, result) =>
        {
            DialogResult = result;
            Close();
        };
    }
}
