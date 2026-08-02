using System.Windows;
using Bakery.WPF.ViewModels;

namespace Bakery.WPF;

public partial class FirstRunSetupWindow
{
    private readonly FirstRunSetupViewModel _viewModel;

    public FirstRunSetupWindow(FirstRunSetupViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = viewModel;
        viewModel.SetupCompleted += OnSetupCompleted;
        viewModel.PasswordResetRequested += OnPasswordResetRequested;
    }

    private void PasswordInput_OnPasswordChanged(object sender, RoutedEventArgs e)
        => _viewModel.Password = PasswordInput.Password;

    private void ConfirmPasswordInput_OnPasswordChanged(object sender, RoutedEventArgs e)
        => _viewModel.ConfirmPassword = ConfirmPasswordInput.Password;

    private void OnSetupCompleted(object? sender, EventArgs e)
        => DialogResult = true;

    private void OnPasswordResetRequested(object? sender, EventArgs e)
    {
        PasswordInput.Clear();
        ConfirmPasswordInput.Clear();
        PasswordInput.Focus();
    }

    protected override void OnClosed(EventArgs e)
    {
        _viewModel.SetupCompleted -= OnSetupCompleted;
        _viewModel.PasswordResetRequested -= OnPasswordResetRequested;
        base.OnClosed(e);
    }
}
