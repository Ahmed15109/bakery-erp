using System.ComponentModel;
using System.Windows;
using Bakery.WPF.ViewModels;

namespace Bakery.WPF.Views;

public partial class RoleFormDialog : Window
{
    private readonly RoleFormDialogViewModel _viewModel;

    public RoleFormDialog(RoleFormDialogViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = viewModel;
        viewModel.PropertyChanged += ViewModelOnPropertyChanged;
    }

    private void ViewModelOnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(RoleFormDialogViewModel.DialogResult) && _viewModel.DialogResult == true)
        {
            DialogResult = true;
            Close();
        }
    }
}
