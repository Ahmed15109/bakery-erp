using System.Windows;
using Bakery.WPF.ViewModels;

namespace Bakery.WPF;

public partial class SafeFormDialog : Window
{
    public SafeFormDialog(SafeFormDialogViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        viewModel.RequestClose += (s, e) => { DialogResult = e; Close(); };
    }
}
