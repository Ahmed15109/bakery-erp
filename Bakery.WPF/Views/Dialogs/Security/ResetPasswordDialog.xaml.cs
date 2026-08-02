using System.ComponentModel;
using System.Windows;
using Bakery.WPF.ViewModels;

namespace Bakery.WPF.Views;

public partial class ResetPasswordDialog : Window
{
    private readonly ResetPasswordDialogViewModel _viewModel;

    public ResetPasswordDialog(ResetPasswordDialogViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = viewModel;
        viewModel.PropertyChanged += ViewModelOnPropertyChanged;
    }

    private void PasswordInput_OnPasswordChanged(object sender, RoutedEventArgs e)
    {
        _viewModel.Password = PasswordInput.Password;
    }

    private void ConfirmPasswordInput_OnPasswordChanged(object sender, RoutedEventArgs e)
    {
        _viewModel.ConfirmPassword = ConfirmPasswordInput.Password;
    }

    private void ViewModelOnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ResetPasswordDialogViewModel.DialogResult) && _viewModel.DialogResult == true)
        {
            DialogResult = true;
            Close();
        }
    }
}
