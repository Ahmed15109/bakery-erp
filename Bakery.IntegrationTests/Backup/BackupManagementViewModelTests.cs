using System.Diagnostics;
using System.Windows.Input;
using Bakery.Application.DTOs;
using Bakery.Application.Interfaces;
using Bakery.Application.Security;
using Bakery.Domain.Enums;
using Bakery.WPF.Services;
using Bakery.WPF.ViewModels;
using FluentAssertions;
using Xunit;

namespace Bakery.IntegrationTests;

public sealed class BackupManagementViewModelTests
{
    [Fact]
    public async Task CreateManualCommand_IsDisabledWhileLoading_AndNotifiesWhenLoadCompletes()
    {
        var backupService = new ControlledBackupService();
        var initialLoad = backupService.EnqueueHistoryLoad();
        using var viewModel = CreateViewModel(backupService, PermissionKeys.BackupCreateManual);
        var canExecuteTransitions = new List<bool>();
        viewModel.CreateManualCommand.CanExecuteChanged += (_, _) =>
            canExecuteTransitions.Add(viewModel.CreateManualCommand.CanExecute(null));

        viewModel.IsBusy.Should().BeTrue();
        viewModel.CreateManualCommand.CanExecute(null).Should().BeFalse();

        initialLoad.SetResult([]);
        await WaitUntilAsync(() => !viewModel.IsBusy);

        viewModel.CreateManualCommand.CanExecute(null).Should().BeTrue();
        canExecuteTransitions.Should().Contain(true,
            "the command must notify WPF when the inherited IsBusy property returns to false");
    }

    [Fact]
    public async Task CreateManualCommand_AfterLoading_RemainsDisabledWithoutPermission()
    {
        var backupService = new ControlledBackupService();
        var initialLoad = backupService.EnqueueHistoryLoad();
        using var viewModel = CreateViewModel(backupService);

        initialLoad.SetResult([]);
        await WaitUntilAsync(() => !viewModel.IsBusy);

        viewModel.CreateManualCommand.CanExecute(null).Should().BeFalse();
    }

    [Fact]
    public async Task ConfigurationDependentCommands_RefreshWhenConfigurationChanges()
    {
        var backupService = new ControlledBackupService
        {
            Settings = CreateSettings(googleDriveConnected: false)
        };
        var initialLoad = backupService.EnqueueHistoryLoad();
        using var viewModel = CreateViewModel(
            backupService,
            PermissionKeys.BackupManageSettings,
            PermissionKeys.BackupConnectGoogleDrive,
            PermissionKeys.BackupDisconnectGoogleDrive);

        initialLoad.SetResult([]);
        await WaitUntilAsync(() => !viewModel.IsBusy);
        viewModel.SelectedBackup = new BackupMetadata
        {
            Id = 42,
            FileName = "backup.berpbackup",
            LocalFileAvailable = true
        };

        var retryNotifications = 0;
        var openFolderNotifications = 0;
        viewModel.RetryUploadCommand.CanExecuteChanged += (_, _) => retryNotifications++;
        viewModel.OpenFolderCommand.CanExecuteChanged += (_, _) => openFolderNotifications++;

        viewModel.RetryUploadCommand.CanExecute(null).Should().BeFalse(
            "a cloud retry requires an active Google Drive configuration");
        viewModel.ConnectGoogleDriveCommand.CanExecute(null).Should().BeTrue();
        viewModel.DisconnectGoogleDriveCommand.CanExecute(null).Should().BeFalse();

        viewModel.GoogleDriveConnected = true;

        viewModel.RetryUploadCommand.CanExecute(null).Should().BeTrue();
        viewModel.ConnectGoogleDriveCommand.CanExecute(null).Should().BeFalse();
        viewModel.DisconnectGoogleDriveCommand.CanExecute(null).Should().BeTrue();
        retryNotifications.Should().BePositive();

        viewModel.BackupDirectory = string.Empty;
        viewModel.OpenFolderCommand.CanExecute(null).Should().BeFalse();
        openFolderNotifications.Should().BePositive();
    }

    [Fact]
    public async Task CreateManualCommand_RepeatedLoadCycles_DoNotLeaveCanExecuteStale()
    {
        var backupService = new ControlledBackupService();
        var initialLoad = backupService.EnqueueHistoryLoad();
        using var viewModel = CreateViewModel(backupService, PermissionKeys.BackupCreateManual);
        var canExecuteTransitions = new List<bool>();
        viewModel.CreateManualCommand.CanExecuteChanged += (_, _) =>
            canExecuteTransitions.Add(viewModel.CreateManualCommand.CanExecute(null));

        initialLoad.SetResult([]);
        await WaitUntilAsync(() => !viewModel.IsBusy);

        for (var cycle = 0; cycle < 3; cycle++)
        {
            var load = backupService.EnqueueHistoryLoad();
            var execution = viewModel.LoadCommand.ExecuteAsync(null);

            await WaitUntilAsync(() => viewModel.IsBusy);
            viewModel.CreateManualCommand.CanExecute(null).Should().BeFalse();

            load.SetResult([]);
            await execution;

            viewModel.IsBusy.Should().BeFalse();
            viewModel.CreateManualCommand.CanExecute(null).Should().BeTrue();
        }

        canExecuteTransitions.Should().ContainInOrder(false, true, false, true, false, true);
    }

    [Fact]
    public async Task IsBusyChanges_NotifyEveryBusyDependentBackupCommand()
    {
        var backupService = new ControlledBackupService
        {
            Settings = CreateSettings(googleDriveConnected: true)
        };
        var initialLoad = backupService.EnqueueHistoryLoad();
        using var viewModel = CreateViewModel(
            backupService,
            PermissionKeys.BackupCreateManual,
            PermissionKeys.BackupRestore,
            PermissionKeys.BackupDelete,
            PermissionKeys.BackupManageSettings,
            PermissionKeys.BackupConnectGoogleDrive,
            PermissionKeys.BackupDisconnectGoogleDrive);

        initialLoad.SetResult([]);
        await WaitUntilAsync(() => !viewModel.IsBusy);
        viewModel.SelectedBackup = new BackupMetadata
        {
            Id = 43,
            FileName = "cloud-backup.berpbackup",
            LocalFileAvailable = true,
            GoogleDriveFileId = "cloud-file-id"
        };

        ICommand[] commands =
        [
            viewModel.LoadCommand,
            viewModel.CreateManualCommand,
            viewModel.RestoreCommand,
            viewModel.RestoreFromFileCommand,
            viewModel.DeleteCommand,
            viewModel.RetryUploadCommand,
            viewModel.DownloadCommand,
            viewModel.OpenFolderCommand,
            viewModel.SelectFolderCommand,
            viewModel.ResetFolderCommand,
            viewModel.ConnectGoogleDriveCommand,
            viewModel.DisconnectGoogleDriveCommand
        ];
        var notifications = commands.ToDictionary(command => command, _ => 0);
        foreach (var command in commands)
        {
            command.CanExecuteChanged += (_, _) => notifications[command]++;
        }

        viewModel.IsBusy = true;
        commands.Should().OnlyContain(command => !command.CanExecute(null));
        viewModel.IsBusy = false;

        notifications.Values.Should().OnlyContain(count => count >= 2,
            "every backup-page command with a busy-state CanExecute condition must be invalidated on both transitions");
        viewModel.CreateManualCommand.CanExecute(null).Should().BeTrue();
        viewModel.RestoreCommand.CanExecute(null).Should().BeTrue();
        viewModel.DownloadCommand.CanExecute(null).Should().BeTrue();
    }

    private static BackupManagementViewModel CreateViewModel(
        ControlledBackupService backupService,
        params string[] permissions)
        => new(
            backupService,
            new StubRestoreService(),
            new StubCloudBackupService(),
            new StubBackupQueueService(),
            new StubBackupStatusNotifier(),
            new StubPermissionService(permissions),
            new StubMessageService(),
            new StubFileLauncherService());

    private static BackupSettingsDto CreateSettings(bool googleDriveConnected)
        => new(
            @"C:\BakeryBackups",
            @"C:\BakeryBackups",
            true,
            googleDriveConnected,
            false);

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        var timeout = Stopwatch.StartNew();
        while (!condition())
        {
            if (timeout.Elapsed > TimeSpan.FromSeconds(5))
                throw new TimeoutException("Timed out waiting for the backup ViewModel state transition.");
            await Task.Delay(10);
        }
    }

    private sealed class ControlledBackupService : IBackupService
    {
        private readonly Queue<TaskCompletionSource<IEnumerable<BackupMetadata>>> _historyLoads = [];

        public BackupSettingsDto Settings { get; set; } = CreateSettings(googleDriveConnected: false);

        public TaskCompletionSource<IEnumerable<BackupMetadata>> EnqueueHistoryLoad()
        {
            var load = new TaskCompletionSource<IEnumerable<BackupMetadata>>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            _historyLoads.Enqueue(load);
            return load;
        }

        public Task<IEnumerable<BackupMetadata>> GetBackupHistoryAsync(CancellationToken cancellationToken = default)
        {
            _historyLoads.Should().NotBeEmpty("each expected refresh must have a controlled result");
            return _historyLoads.Dequeue().Task;
        }

        public Task<BackupSettingsDto> GetSettingsAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(Settings);

        public Task<string> CreateBackupAsync(
            string? customPath = null,
            string? password = null,
            CancellationToken cancellationToken = default)
            => Task.FromResult(@"C:\BakeryBackups\backup.berpbackup");

        public Task<string> CreateSafetySnapshotAsync(string operationName, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
        public Task RestoreBackupAsync(string backupFilePath, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
        public Task SetBackupDirectoryAsync(string? directory, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
        public Task DeleteLocalBackupAsync(int backupRecordId, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
        public Task EnforceRetentionPolicyAsync(int maxBackups = 5, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }

    private sealed class StubRestoreService : IRestoreService
    {
        public Task<RestoreResult> RestoreLocalAsync(string archivePath, CancellationToken cancellationToken = default)
            => Task.FromResult(new RestoreResult(true));
        public Task<RestoreResult> RestoreHistoryAsync(int backupRecordId, CancellationToken cancellationToken = default)
            => Task.FromResult(new RestoreResult(true));
        public Task<RestoreResult> RestoreCloudAsync(int backupRecordId, CancellationToken cancellationToken = default)
            => Task.FromResult(new RestoreResult(true));
    }

    private sealed class StubCloudBackupService : ICloudBackupService
    {
        public Task<bool> IsConnectedAsync(CancellationToken cancellationToken = default) => Task.FromResult(false);
        public Task ConnectAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task DisconnectAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<string> UploadAsync(string localArchivePath, string fileName, CancellationToken cancellationToken = default)
            => Task.FromResult("cloud-file-id");
        public Task DownloadAsync(string cloudFileId, string destinationPath, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }

    private sealed class StubBackupQueueService : IBackupQueueService
    {
        public ValueTask QueueAutomaticBackupAsync(
            DateOnly workingDayDate,
            int workingDayId,
            Guid? sourceOperationId,
            string createdByUser,
            CancellationToken cancellationToken = default)
            => ValueTask.CompletedTask;
        public ValueTask QueueCloudRetryAsync(int backupRecordId, CancellationToken cancellationToken = default)
            => ValueTask.CompletedTask;
        public Task ProcessPendingUploadsAsync(CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }

    private sealed class StubBackupStatusNotifier : IBackupStatusNotifier
    {
        public event EventHandler? StatusChanged
        {
            add { }
            remove { }
        }

        public void NotifyChanged()
        {
        }
    }

    private sealed class StubPermissionService(params string[] permissions) : IPermissionService
    {
        private readonly HashSet<string> _permissions = permissions.ToHashSet(StringComparer.OrdinalIgnoreCase);

        public bool HasPermission(string permissionKey) => _permissions.Contains(permissionKey);
        public void EnsurePermission(string permissionKey)
        {
            if (!HasPermission(permissionKey)) throw new UnauthorizedAccessException();
        }

        public bool IsAdmin() => false;
    }

    private sealed class StubMessageService : IMessageService
    {
        public void ShowInfo(string message) { }
        public void ShowError(string message) { }
        public bool Confirm(string message) => true;
        public Task<string?> ShowInputAsync(string title, string prompt, string defaultValue = "")
            => Task.FromResult<string?>(null);
    }

    private sealed class StubFileLauncherService : IFileLauncherService
    {
        public void OpenFile(string filePath)
        {
        }
    }
}
