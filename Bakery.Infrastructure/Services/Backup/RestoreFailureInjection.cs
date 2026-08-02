namespace Bakery.Infrastructure.Services.Backup;

public enum RestoreCheckpoint
{
    AfterSelectedDatabaseRestore,
    AfterExternalContentItem,
    BeforeRollbackDatabase
}

public interface IRestoreFailureInjector
{
    void ThrowIfRequested(RestoreCheckpoint checkpoint, string? itemName = null);
}

internal sealed class NoOpRestoreFailureInjector : IRestoreFailureInjector
{
    public void ThrowIfRequested(RestoreCheckpoint checkpoint, string? itemName = null)
    {
    }
}
