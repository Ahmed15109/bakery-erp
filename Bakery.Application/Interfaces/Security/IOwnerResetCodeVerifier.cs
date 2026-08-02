namespace Bakery.Application.Interfaces;

/// <summary>
/// Opaque, short-lived proof that the fixed owner reset code was verified.
/// Implementations must make authorizations single-use.
/// </summary>
public interface IOwnerResetAuthorization;

public interface IOwnerResetCodeVerifier
{
    bool Verify(string? enteredCode);
    IOwnerResetAuthorization? Authorize(string? enteredCode);
    bool TryConsumeAuthorization(IOwnerResetAuthorization authorization);
}
