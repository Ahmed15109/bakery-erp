using System.Windows;
using Bakery.WPF.ViewModels;

namespace Bakery.WPF;

public partial class SafeManagementDialog : Window
{
    public SafeManagementDialog(SafeManagementDialogViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        viewModel.RequestClose += (s, e) => { DialogResult = e; Close(); };
    }
}
