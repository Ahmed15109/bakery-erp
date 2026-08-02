using System.Net.NetworkInformation;
using System.Text.Json;
using Bakery.Application.Interfaces;

namespace Bakery.Infrastructure.Services.Backup;

public sealed class BackupPathProvider
{
    private const string SettingsFileName = "backup-settings.json";
    private readonly object _sync = new();
    private readonly string _applicationDataDirectory;
    private readonly string _settingsPath;
    private BackupDeviceSettings? _cached;

    public BackupPathProvider(IApplicationPathService applicationPaths)
        : this(applicationPaths.RootDirectory)
    {
    }

    public BackupPathProvider(string applicationDataDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(applicationDataDirectory);
        _applicationDataDirectory = Path.GetFullPath(applicationDataDirectory);
        _settingsPath = Path.Combine(_applicationDataDirectory, SettingsFileName);
    }

    public string ApplicationDataDirectory => _applicationDataDirectory;

    public string DefaultBackupDirectory => Path.Combine(_applicationDataDirectory, "Backups");

    public string GetBackupDirectory()
    {
        lock (_sync)
        {
            _cached ??= Load();
            var path = string.IsNullOrWhiteSpace(_cached.BackupDirectory)
                ? DefaultBackupDirectory
                : Path.GetFullPath(_cached.BackupDirectory);
            Directory.CreateDirectory(path);
            return path;
        }
    }

    public void SetBackupDirectory(string? directory)
    {
        lock (_sync)
        {
            var normalized = string.IsNullOrWhiteSpace(directory)
                ? null
                : Path.GetFullPath(directory.Trim());
            if (normalized is not null)
            {
                Directory.CreateDirectory(normalized);
                var probe = Path.Combine(normalized, $".bakery-write-test-{Guid.NewGuid():N}.tmp");
                using (File.Create(probe)) { }
                File.Delete(probe);
            }

            Directory.CreateDirectory(_applicationDataDirectory);
            var settings = new BackupDeviceSettings { BackupDirectory = normalized };
            var temporaryPath = _settingsPath + ".tmp";
            File.WriteAllText(temporaryPath, JsonSerializer.Serialize(settings));
            File.Move(temporaryPath, _settingsPath, true);
            _cached = settings;
        }
    }

    public string CreateWorkingDirectory(string backupDirectory)
    {
        var root = Path.Combine(backupDirectory, ".working");
        Directory.CreateDirectory(root);
        var directory = Path.Combine(root, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return directory;
    }

    private BackupDeviceSettings Load()
    {
        try
        {
            if (File.Exists(_settingsPath))
            {
                return JsonSerializer.Deserialize<BackupDeviceSettings>(File.ReadAllText(_settingsPath))
                    ?? new BackupDeviceSettings();
            }
        }
        catch
        {
            // A corrupt optional device setting must not disable the safe default.
        }

        return new BackupDeviceSettings();
    }

    private sealed class BackupDeviceSettings
    {
        public string? BackupDirectory { get; set; }
    }
}

public sealed class BackupOperationGate
{
    private readonly SemaphoreSlim _gate = new(1, 1);

    public async Task<IDisposable?> TryEnterAsync(CancellationToken cancellationToken)
    {
        if (!await _gate.WaitAsync(0, cancellationToken)) return null;
        return new Releaser(_gate);
    }

    public async Task<IDisposable> EnterAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        return new Releaser(_gate);
    }

    private sealed class Releaser(SemaphoreSlim gate) : IDisposable
    {
        private SemaphoreSlim? _gate = gate;
        public void Dispose() => Interlocked.Exchange(ref _gate, null)?.Release();
    }
}

public sealed class BackupStatusNotifier : IBackupStatusNotifier
{
    public event EventHandler? StatusChanged;
    public void NotifyChanged() => StatusChanged?.Invoke(this, EventArgs.Empty);
}

public sealed class ConnectivityService : IConnectivityService, IDisposable
{
    public ConnectivityService()
    {
        NetworkChange.NetworkAvailabilityChanged += OnNetworkAvailabilityChanged;
    }

    public bool IsNetworkAvailable => NetworkInterface.GetIsNetworkAvailable();
    public event EventHandler? NetworkAvailable;

    private void OnNetworkAvailabilityChanged(object? sender, NetworkAvailabilityEventArgs args)
    {
        if (args.IsAvailable) NetworkAvailable?.Invoke(this, EventArgs.Empty);
    }

    public void Dispose() => NetworkChange.NetworkAvailabilityChanged -= OnNetworkAvailabilityChanged;
}

internal static class BackupError
{
    public static string Summarize(Exception exception)
    {
        var message = exception switch
        {
            BackupPasswordRequiredException => "هذه النسخة الاحتياطية محمية بكلمة مرور. أدخل كلمة المرور للمتابعة.",
            BackupAuthenticationException => "كلمة مرور النسخة الاحتياطية غير صحيحة أو الملف تالف.",
            UnauthorizedAccessException => "لا توجد صلاحية للوصول إلى مجلد النسخ الاحتياطي.",
            IOException => "تعذر قراءة أو كتابة ملف النسخة الاحتياطية.",
            OperationCanceledException => "تم إلغاء العملية.",
            _ => "تعذر إكمال عملية النسخ الاحتياطي. راجع سجل النظام للتفاصيل."
        };
        return message.Length <= 500 ? message : message[..500];
    }
}

internal sealed class BackupPasswordRequiredException : Exception;
internal sealed class BackupAuthenticationException : Exception;
