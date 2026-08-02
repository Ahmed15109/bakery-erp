using System.ComponentModel;
using System.Windows;
using Bakery.WPF.ViewModels;

namespace Bakery.WPF.Views;

public partial class SafeSelectionDialog : Window
{
    private readonly SafeSelectionDialogViewModel _viewModel;

    public SafeSelectionDialog(SafeSelectionDialogViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = viewModel;
        viewModel.PropertyChanged += ViewModelOnPropertyChanged;
    }

    private void ViewModelOnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SafeSelectionDialogViewModel.DialogResult) && _viewModel.DialogResult == true)
        {
            DialogResult = true;
            Close();
        }
    }
}
