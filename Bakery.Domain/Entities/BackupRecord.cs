using Bakery.Domain.Enums;

namespace Bakery.Domain.Entities;

/// <summary>
/// Lightweight operational history for backup files. Archive bytes are never stored
/// in the application database and records remain after their local file is removed.
/// </summary>
public sealed class BackupRecord : BaseEntity
{
    public string FileName { get; set; } = string.Empty;
    public string LocalPath { get; set; } = string.Empty;
    public DateTime BackupCreatedAtUtc { get; set; }
    public DateOnly? WorkingDayDate { get; set; }
    public int? WorkingDayId { get; set; }
    public Guid? SourceOperationId { get; set; }
    public BackupType BackupType { get; set; }
    public long FileSizeBytes { get; set; }
    public BackupStatus Status { get; set; }
    public CloudBackupStatus CloudStatus { get; set; }
    public string? GoogleDriveFileId { get; set; }
    public string ApplicationVersion { get; set; } = string.Empty;
    public string DatabaseVersion { get; set; } = string.Empty;
    public string DeviceName { get; set; } = string.Empty;
    public string CreatedByUser { get; set; } = string.Empty;
    public string? ErrorSummary { get; set; }
    public int UploadRetryCount { get; set; }
    public DateTime? LastUploadAttemptAtUtc { get; set; }
}

