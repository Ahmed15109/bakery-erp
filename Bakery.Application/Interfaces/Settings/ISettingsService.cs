namespace Bakery.Application.Interfaces;

public interface ISettingsService
{
    Task<string?> GetSettingAsync(string key, CancellationToken cancellationToken = default);
    Task SetSettingAsync(string key, string value, CancellationToken cancellationToken = default);
    Task<bool> IsDarkModeAsync(CancellationToken cancellationToken = default);
    Task SetDarkModeAsync(bool isDark, CancellationToken cancellationToken = default);
    Task<bool> IsRtlAsync(CancellationToken cancellationToken = default);
    Task SetRtlAsync(bool isRtl, CancellationToken cancellationToken = default);
}
