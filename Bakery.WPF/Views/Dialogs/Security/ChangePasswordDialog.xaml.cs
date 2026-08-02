using System.ComponentModel;
using System.Windows;
using Bakery.WPF.ViewModels;

namespace Bakery.WPF.Views;

public partial class ChangePasswordDialog : Window
{
    private readonly ChangePasswordDialogViewModel _viewModel;

    public ChangePasswordDialog(ChangePasswordDialogViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = viewModel;
        viewModel.PropertyChanged += ViewModelOnPropertyChanged;
    }

    private void NewPasswordInput_OnPasswordChanged(object sender, RoutedEventArgs e)
        => _viewModel.NewPassword = NewPasswordInput.Password;

    private void ConfirmPasswordInput_OnPasswordChanged(object sender, RoutedEventArgs e)
        => _viewModel.ConfirmPassword = ConfirmPasswordInput.Password;

    private void ViewModelOnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ChangePasswordDialogViewModel.DialogResult) && _viewModel.DialogResult == true)
        {
            DialogResult = true;
            Close();
        }
    }
}
