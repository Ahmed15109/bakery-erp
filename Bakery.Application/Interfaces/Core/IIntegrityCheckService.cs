namespace Bakery.Application.Interfaces;

public interface IIntegrityCheckService
{
    Task<bool> RunFullCheckAsync(CancellationToken cancellationToken = default);
}
