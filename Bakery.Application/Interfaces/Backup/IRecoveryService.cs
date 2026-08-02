namespace Bakery.Application.Interfaces;

public interface IRecoveryService
{
    Task SaveDraftAsync<T>(string key, T data, CancellationToken cancellationToken = default);
    Task<T?> LoadDraftAsync<T>(string key, CancellationToken cancellationToken = default);
    Task DeleteDraftAsync(string key, CancellationToken cancellationToken = default);
    Task<IEnumerable<string>> GetAvailableDraftKeysAsync(CancellationToken cancellationToken = default);
    Task LogEmergencyAsync(Exception ex, string context, CancellationToken cancellationToken = default);
}
