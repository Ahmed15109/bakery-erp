namespace Bakery.Application.Interfaces;

public interface ISystemResetService
{
    Task ResetTransactionalDataAsync(
        IOwnerResetAuthorization authorization,
        CancellationToken cancellationToken = default);
}
