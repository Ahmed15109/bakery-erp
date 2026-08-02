using Bakery.Domain.Enums;

namespace Bakery.Application.DTOs;

public sealed record BackupRequest(
    BackupType BackupType,
    DateOnly? WorkingDayDate = null,
    int? WorkingDayId = null,
    Guid? SourceOperationId = null,
    string? DestinationDirectory = null,
    string? CreatedByUser = null,
    bool EnforceUserPermission = true,
    string? EncryptionPassword = null);

public sealed record BackupArchiveMetadata(
    string BackupVersion,
    string ApplicationVersion,
    string DatabaseSchemaVersion,
    DateTime CreationDateUtc,
    DateOnly? WorkingDayDate,
    BackupType BackupType,
    string DeviceName,
    string CreatedByUser);

public sealed record BackupValidationResult(
    bool IsValid,
    BackupArchiveMetadata? Metadata = null,
    string? ErrorSummary = null);

public sealed record BackupStatusSummary(
    DateTime? LastSuccessfulLocalBackupUtc,
    bool GoogleDriveConnected,
    int PendingUploadCount,
    BackupStatus? LatestLocalStatus,
    CloudBackupStatus? LatestCloudStatus,
    string Health,
    string? ErrorSummary = null);

public sealed record BackupSettingsDto(
    string BackupDirectory,
    string DefaultBackupDirectory,
    bool IsDefaultDirectory,
    bool GoogleDriveConnected,
    bool IsSameDriveAsDatabase);

public sealed record RestoreResult(
    bool Succeeded,
    string? ErrorSummary = null,
    string? RecoveryDirectory = null);
