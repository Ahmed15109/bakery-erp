using System.ComponentModel;
using System.Windows;
using Bakery.WPF.ViewModels;

namespace Bakery.WPF.Views;

public partial class BranchSelectionDialog : Window
{
    private readonly BranchSelectionDialogViewModel _viewModel;

    public BranchSelectionDialog(BranchSelectionDialogViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = viewModel;
        viewModel.PropertyChanged += ViewModelOnPropertyChanged;
    }

    private void ViewModelOnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(BranchSelectionDialogViewModel.DialogResult) && _viewModel.DialogResult == true)
        {
            DialogResult = true;
            Close();
        }
    }
}
