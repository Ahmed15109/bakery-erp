using Bakery.Application.DTOs;

namespace Bakery.Application.Interfaces;

public sealed class BackupMetadata
{
    public int Id { get; set; }
    public string FilePath { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateOnly? WorkingDayDate { get; set; }
    public Bakery.Domain.Enums.BackupType BackupType { get; set; }
    public long SizeBytes { get; set; }
    public Bakery.Domain.Enums.BackupStatus Status { get; set; }
    public Bakery.Domain.Enums.CloudBackupStatus CloudStatus { get; set; }
    public string? GoogleDriveFileId { get; set; }
    public string ApplicationVersion { get; set; } = string.Empty;
    public string DatabaseVersion { get; set; } = string.Empty;
    public string DeviceName { get; set; } = string.Empty;
    public string CreatedByUser { get; set; } = string.Empty;
    public string? ErrorSummary { get; set; }
    public int UploadRetryCount { get; set; }
    public bool LocalFileAvailable { get; set; }
    public bool GoogleDriveAvailable => !string.IsNullOrWhiteSpace(GoogleDriveFileId);
}

public interface IBackupService
{
    // Compatibility entry point used by the existing settings/recovery surfaces.
    Task<string> CreateBackupAsync(string? customPath = null, string? password = null, CancellationToken cancellationToken = default);
    Task<BackupMetadata> CreateBackupAsync(BackupRequest request, CancellationToken cancellationToken = default)
        => throw new NotSupportedException();
    Task<string> CreateSafetySnapshotAsync(string operationName, CancellationToken cancellationToken = default);
    Task RestoreBackupAsync(string backupFilePath, CancellationToken cancellationToken = default);
    Task RestoreBackupAsync(
        string backupFilePath,
        string? password,
        CancellationToken cancellationToken = default)
        => password is null
            ? RestoreBackupAsync(backupFilePath, cancellationToken)
            : throw new NotSupportedException("Password-protected restore is not supported by this implementation.");
    Task<IEnumerable<BackupMetadata>> GetBackupHistoryAsync(CancellationToken cancellationToken = default);
    Task<BackupStatusSummary> GetStatusSummaryAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(new BackupStatusSummary(null, false, 0, null, null, "Unknown"));
    Task<BackupSettingsDto> GetSettingsAsync(CancellationToken cancellationToken = default)
        => throw new NotSupportedException();
    Task SetBackupDirectoryAsync(string? directory, CancellationToken cancellationToken = default)
        => throw new NotSupportedException();
    Task DeleteLocalBackupAsync(int backupRecordId, CancellationToken cancellationToken = default)
        => throw new NotSupportedException();
    Task EnforceRetentionPolicyAsync(int maxBackups = 5, CancellationToken cancellationToken = default);
    Task CleanupStaleTemporaryFilesAsync(CancellationToken cancellationToken = default)
        => Task.CompletedTask;
}

public interface IBackupValidationService
{
    Task<BackupValidationResult> ValidateAsync(string archivePath, CancellationToken cancellationToken = default);
    Task<BackupValidationResult> ValidateAsync(
        string archivePath,
        string? password,
        CancellationToken cancellationToken = default)
        => password is null
            ? ValidateAsync(archivePath, cancellationToken)
            : throw new NotSupportedException("Password-protected validation is not supported by this implementation.");
    Task<bool> CanOpenArchiveAsync(string archivePath, CancellationToken cancellationToken = default);
    Task<bool> CanOpenArchiveAsync(
        string archivePath,
        string? password,
        CancellationToken cancellationToken = default)
        => password is null
            ? CanOpenArchiveAsync(archivePath, cancellationToken)
            : Task.FromResult(false);
}

public interface IBackupRetentionService
{
    Task EnforceAsync(int maxSuccessfulBackups = 5, CancellationToken cancellationToken = default);
}

public interface IRestoreService
{
    Task<RestoreResult> RestoreLocalAsync(string archivePath, CancellationToken cancellationToken = default);
    Task<RestoreResult> RestoreLocalAsync(
        string archivePath,
        string? password,
        CancellationToken cancellationToken = default)
        => password is null
            ? RestoreLocalAsync(archivePath, cancellationToken)
            : throw new NotSupportedException("Password-protected restore is not supported by this implementation.");
    Task<RestoreResult> RestoreHistoryAsync(int backupRecordId, CancellationToken cancellationToken = default);
    Task<RestoreResult> RestoreHistoryAsync(
        int backupRecordId,
        string? password,
        CancellationToken cancellationToken = default)
        => password is null
            ? RestoreHistoryAsync(backupRecordId, cancellationToken)
            : throw new NotSupportedException("Password-protected restore is not supported by this implementation.");
    Task<RestoreResult> RestoreCloudAsync(int backupRecordId, CancellationToken cancellationToken = default);
    Task<RestoreResult> RestoreCloudAsync(
        int backupRecordId,
        string? password,
        CancellationToken cancellationToken = default)
        => password is null
            ? RestoreCloudAsync(backupRecordId, cancellationToken)
            : throw new NotSupportedException("Password-protected restore is not supported by this implementation.");
}

public interface ICloudBackupService
{
    Task<bool> IsConnectedAsync(CancellationToken cancellationToken = default);
    Task ConnectAsync(CancellationToken cancellationToken = default);
    Task DisconnectAsync(CancellationToken cancellationToken = default);
    Task<string> UploadAsync(string localArchivePath, string fileName, CancellationToken cancellationToken = default);
    Task DownloadAsync(string cloudFileId, string destinationPath, CancellationToken cancellationToken = default);
}

public interface IBackupQueueService
{
    ValueTask QueueAutomaticBackupAsync(
        DateOnly workingDayDate,
        int workingDayId,
        Guid? sourceOperationId,
        string createdByUser,
        CancellationToken cancellationToken = default);
    ValueTask QueueCloudRetryAsync(int backupRecordId, CancellationToken cancellationToken = default);
    Task ProcessPendingUploadsAsync(CancellationToken cancellationToken = default);
}

public interface IBackupStartupService
{
    Task RunLightweightStartupRecoveryAsync(CancellationToken cancellationToken = default);
}

public interface IConnectivityService
{
    bool IsNetworkAvailable { get; }
    event EventHandler? NetworkAvailable;
}

public interface IBackupStatusNotifier
{
    event EventHandler? StatusChanged;
    void NotifyChanged();
}
