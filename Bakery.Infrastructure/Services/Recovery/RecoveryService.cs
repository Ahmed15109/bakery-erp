using System.Text.Json;
using Bakery.Application.Interfaces;
using Bakery.Shared.Security;

namespace Bakery.Infrastructure.Services.Recovery;

public sealed class RecoveryService : IRecoveryService
{
    private readonly string _recoveryPath;
    private readonly string _emergencyLogPath;

    public RecoveryService()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var basePath = Path.Combine(appData, "BakeryERP");
        _recoveryPath = Path.Combine(basePath, "Recovery");
        _emergencyLogPath = Path.Combine(basePath, "Logs", "Emergency");

        Directory.CreateDirectory(_recoveryPath);
        Directory.CreateDirectory(_emergencyLogPath);
    }

    public async Task SaveDraftAsync<T>(string key, T data, CancellationToken cancellationToken = default)
    {
        var filePath = Path.Combine(_recoveryPath, $"{key}.draft.json");
        var json = JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(filePath, json, cancellationToken);
    }

    public async Task<T?> LoadDraftAsync<T>(string key, CancellationToken cancellationToken = default)
    {
        var filePath = Path.Combine(_recoveryPath, $"{key}.draft.json");
        if (!File.Exists(filePath)) return default;

        var json = await File.ReadAllTextAsync(filePath, cancellationToken);
        return JsonSerializer.Deserialize<T>(json);
    }

    public Task DeleteDraftAsync(string key, CancellationToken cancellationToken = default)
    {
        var filePath = Path.Combine(_recoveryPath, $"{key}.draft.json");
        if (File.Exists(filePath))
            File.Delete(filePath);
        return Task.CompletedTask;
    }

    public Task<IEnumerable<string>> GetAvailableDraftKeysAsync(CancellationToken cancellationToken = default)
    {
        var files = Directory.GetFiles(_recoveryPath, "*.draft.json")
                             .Select(f => Path.GetFileNameWithoutExtension(f).Replace(".draft", ""));
        return Task.FromResult(files);
    }

    public async Task LogEmergencyAsync(Exception ex, string context, CancellationToken cancellationToken = default)
    {
        var filePath = Path.Combine(_emergencyLogPath, $"crash_{DateTime.Now:yyyyMMdd_HHmmss}.txt");
        var content = SensitiveDataRedactor.Redact(
            $"Context: {context}\nTime: {DateTime.Now}\nException: {ex.Message}\nStackTrace:\n{ex.StackTrace}");
        await File.WriteAllTextAsync(filePath, content, cancellationToken);
    }
}
