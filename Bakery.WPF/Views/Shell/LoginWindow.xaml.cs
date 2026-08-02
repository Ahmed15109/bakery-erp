using Bakery.WPF.ViewModels;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;

namespace Bakery.WPF;

public partial class LoginWindow
{
    private readonly LoginViewModel _viewModel;

    public LoginWindow(LoginViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = viewModel;
        viewModel.LoginSucceeded += (_, _) => LoginSucceeded?.Invoke(this, EventArgs.Empty);
        viewModel.LoginFailed += ViewModel_OnLoginFailed;
    }

    public event EventHandler? LoginSucceeded;
    public bool IsLoginFormUsable =>
        IsVisible &&
        LoginForm.IsEnabled &&
        LoginForm.IsHitTestVisible &&
        BranchInput.IsEnabled &&
        UserInput.IsEnabled &&
        PasswordInput.IsEnabled &&
        LoginButton.IsEnabled;
    public bool IsLoginFormFullyOpaque => MainContainer.Opacity == 1 && LoginForm.Opacity == 1;
    public bool IsPasswordInputCleared => string.IsNullOrEmpty(PasswordInput.Password);

    private void PasswordInput_OnPasswordChanged(object sender, System.Windows.RoutedEventArgs e)
    {
        _viewModel.Password = PasswordInput.Password;
    }

    private void ViewModel_OnLoginFailed(object? sender, EventArgs e)
    {
        Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, ResetFailedLoginUi);
    }

    private void ResetFailedLoginUi()
    {
        if (!IsVisible)
        {
            return;
        }

        PasswordInput.Clear();
        FocusManager.SetFocusedElement(this, PasswordInput);
        Keyboard.Focus(PasswordInput);
    }

    protected override void OnClosed(EventArgs e)
    {
        _viewModel.LoginFailed -= ViewModel_OnLoginFailed;
        base.OnClosed(e);
    }

    private void ExitButton_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        System.Windows.Application.Current.Shutdown();
    }

    private void Window_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (e.ChangedButton == System.Windows.Input.MouseButton.Left)
            DragMove();
    }
}
