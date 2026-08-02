using System.Windows;
using Bakery.Application.Interfaces;
using Bakery.WPF.Views;

namespace Bakery.WPF.Services;

public interface IOwnerResetAuthorizationPrompt
{
    Task<IOwnerResetAuthorization?> RequestAuthorizationAsync();
}

public sealed class OwnerResetAuthorizationPrompt : IOwnerResetAuthorizationPrompt
{
    private readonly IOwnerResetCodeVerifier _verifier;

    public OwnerResetAuthorizationPrompt(IOwnerResetCodeVerifier verifier)
    {
        _verifier = verifier;
    }

    public async Task<IOwnerResetAuthorization?> RequestAuthorizationAsync()
    {
        return await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
        {
            var dialog = new OwnerResetCodeDialog(_verifier)
            {
                Owner = System.Windows.Application.Current.MainWindow
            };
            return dialog.ShowDialog() == true ? dialog.Authorization : null;
        });
    }
}

public sealed class OwnerResetCodeAttemptSession
{
    public const int MaximumFailedAttempts = 5;
    private readonly IOwnerResetCodeVerifier _verifier;

    public OwnerResetCodeAttemptSession(IOwnerResetCodeVerifier verifier)
    {
        _verifier = verifier;
    }

    public int FailedAttempts { get; private set; }
    public int RemainingAttempts => Math.Max(0, MaximumFailedAttempts - FailedAttempts);
    public bool IsLocked => FailedAttempts >= MaximumFailedAttempts;

    public IOwnerResetAuthorization? TryAuthorize(string? enteredCode)
    {
        if (IsLocked) return null;

        var authorization = _verifier.Authorize(enteredCode);
        if (authorization is null) FailedAttempts++;
        return authorization;
    }
}
