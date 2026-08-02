namespace Bakery.Domain.Enums;

public enum BackupType
{
    Automatic,
    Manual,
    SafetyBeforeRestore
}

public enum BackupStatus
{
    Creating,
    Validating,
    Success,
    Failed,
    Restoring
}

public enum CloudBackupStatus
{
    NotEnabled,
    PendingUpload,
    Uploading,
    Uploaded,
    UploadFailed
}

