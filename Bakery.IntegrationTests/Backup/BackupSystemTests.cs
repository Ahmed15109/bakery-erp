using System.IO.Compression;
using System.IO;
using System.Text.Json;
using Bakery.Application.DTOs;
using Bakery.Application.Interfaces;
using Bakery.Application.Security;
using Bakery.Domain.Entities;
using Bakery.Domain.Enums;
using Bakery.Infrastructure.Data;
using Bakery.Infrastructure.Services.Backup;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Bakery.IntegrationTests;

public sealed class VendorGoogleOAuthConfigurationTests
{
    [Fact]
    public void ShippedDefaults_DoNotContainDeploymentOAuthCredentials()
    {
        var configurationPath = Path.Combine(AppContext.BaseDirectory, "appsettings.defaults.json");
        File.Exists(configurationPath).Should().BeTrue();

        using var document = JsonDocument.Parse(File.ReadAllText(configurationPath));
        var googleDrive = document.RootElement.GetProperty("GoogleDrive");
        googleDrive.GetProperty("ClientId").GetString().Should().BeEmpty();
        googleDrive.GetProperty("ClientSecret").GetString().Should().BeEmpty();
    }

    [Fact]
    public void AuthorizationRequest_UsesConfiguredVendorClient_AndOnlyDriveFileScope()
    {
        const string configuredClientId = "test-desktop-client.apps.example.invalid";

        var authorizationUri = GoogleDriveCloudBackupService.BuildAuthorizationUri(
            configuredClientId,
            "http://127.0.0.1:54321/",
            "test-state",
            "test-pkce-challenge");
        var query = ParseQuery(authorizationUri);

        authorizationUri.Host.Should().Be("accounts.google.com");
        query["scope"].Should().Be(GoogleDriveCloudBackupService.DriveFileScope);
        query["scope"].Split(' ', StringSplitOptions.RemoveEmptyEntries).Should().ContainSingle();
        string.Equals(query["client_id"], configuredClientId, StringComparison.Ordinal).Should().BeTrue();
        query["redirect_uri"].Should().Be("http://127.0.0.1:54321/");
        query["response_type"].Should().Be("code");
        query["access_type"].Should().Be("offline");
        query["code_challenge_method"].Should().Be("S256");
        query.Should().NotContainKey("client_secret");
    }

    private static IReadOnlyDictionary<string, string> ParseQuery(Uri uri)
        => uri.Query.TrimStart('?')
            .Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Select(item => item.Split('=', 2))
            .ToDictionary(
                item => Uri.UnescapeDataString(item[0]),
                item => Uri.UnescapeDataString(item.Length > 1 ? item[1] : string.Empty),
                StringComparer.Ordinal);
}

public sealed class LocalBackupSystemTests : IClassFixture<DatabaseFixture>
{
    private readonly DatabaseFixture _fixture;

    public LocalBackupSystemTests(DatabaseFixture fixture)
    {
        _fixture = fixture;
        ResetState();
    }

    [Fact]
    public async Task ManualBackup_CreatesValidatedArchive_WithActualMetadata()
    {
        using var scope = _fixture.ServiceProvider.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IBackupService>();
        var validator = scope.ServiceProvider.GetRequiredService<IBackupValidationService>();
        var db = scope.ServiceProvider.GetRequiredService<BakeryDbContext>();

        var backup = await service.CreateBackupAsync(new BackupRequest(
            BackupType.Manual,
            DestinationDirectory: _fixture.BackupDirectory,
            CreatedByUser: "test-admin"));

        backup.Status.Should().Be(BackupStatus.Success);
        backup.LocalFileAvailable.Should().BeTrue();
        backup.FileName.Should().StartWith("Backup_").And.EndWith(".berpbackup");
        (await File.ReadAllBytesAsync(backup.FilePath)).Take(8)
            .Should().Equal("BKERPENC"u8.ToArray());
        var validation = await validator.ValidateAsync(backup.FilePath);
        validation.IsValid.Should().BeTrue();
        validation.Metadata.Should().NotBeNull();
        validation.Metadata!.BackupType.Should().Be(BackupType.Manual);
        validation.Metadata.CreatedByUser.Should().Be("test-admin");
        var latestAppliedMigration = (await db.Database.GetAppliedMigrationsAsync()).Last();
        validation.Metadata.DatabaseSchemaVersion.Should().Be(latestAppliedMigration);
    }

    [Fact]
    public async Task PasswordProtectedBackup_IsAuthenticated_AndLegacyZipRemainsReadable()
    {
        const string password = "Correct-Horse-Battery-Staple-2026";
        using var scope = _fixture.ServiceProvider.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IBackupService>();
        var validator = scope.ServiceProvider.GetRequiredService<IBackupValidationService>();
        var encryption = scope.ServiceProvider.GetRequiredService<BackupEncryptionService>();

        var backupPath = await service.CreateBackupAsync(_fixture.BackupDirectory, password);

        var bytes = await File.ReadAllBytesAsync(backupPath);
        bytes.Take(8).Should().Equal("BKERPENC"u8.ToArray());
        System.Text.Encoding.UTF8.GetString(bytes).Should().NotContain(password);
        Action openAsPlainZip = () =>
        {
            using var stream = File.OpenRead(backupPath);
            using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
            _ = archive.Entries.Count;
        };
        openAsPlainZip.Should().Throw<InvalidDataException>();

        (await validator.ValidateAsync(backupPath)).IsValid.Should().BeFalse();
        (await validator.ValidateAsync(backupPath, "Wrong-Password-For-Backup-2026")).IsValid.Should().BeFalse();
        (await validator.ValidateAsync(backupPath, password)).IsValid.Should().BeTrue();

        var tamperedPath = Path.Combine(_fixture.BackupDirectory, "tampered.berpbackup");
        await File.WriteAllBytesAsync(tamperedPath, bytes);
        await using (var tampered = new FileStream(tamperedPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            tampered.Position = tampered.Length / 2;
            var original = tampered.ReadByte();
            tampered.Position--;
            tampered.WriteByte((byte)(original ^ 0x01));
        }
        (await validator.ValidateAsync(tamperedPath, password)).IsValid.Should().BeFalse();

        var legacyPath = Path.Combine(_fixture.BackupDirectory, "legacy-valid.zip");
        using (var prepared = await encryption.PrepareReadAsync(backupPath, password))
            File.Copy(prepared.ArchivePath, legacyPath);
        (await validator.ValidateAsync(legacyPath)).IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task Validation_RejectsMissingAndCorruptArchives_WithoutTouchingDatabase()
    {
        using var scope = _fixture.ServiceProvider.CreateScope();
        var validator = scope.ServiceProvider.GetRequiredService<IBackupValidationService>();
        var missing = await validator.ValidateAsync(Path.Combine(_fixture.BackupDirectory, "missing.zip"));
        missing.IsValid.Should().BeFalse();

        Directory.CreateDirectory(_fixture.BackupDirectory);
        var corrupt = Path.Combine(_fixture.BackupDirectory, "corrupt.zip");
        await File.WriteAllTextAsync(corrupt, "not a zip archive");
        var result = await validator.ValidateAsync(corrupt);
        result.IsValid.Should().BeFalse();

        var db = scope.ServiceProvider.GetRequiredService<BakeryDbContext>();
        (await db.Database.CanConnectAsync()).Should().BeTrue();
    }

    [Fact]
    public async Task Retention_KeepsLatestFive_AndPreservesHistory()
    {
        using var scope = _fixture.ServiceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BakeryDbContext>();
        var records = new List<BackupRecord>();
        for (var index = 0; index < 7; index++)
        {
            var path = Path.Combine(_fixture.BackupDirectory, $"Backup_2026-01-0{index + 1}_00-00-00.zip");
            CreateMinimalReadableArchive(path);
            records.Add(new BackupRecord
            {
                FileName = Path.GetFileName(path),
                LocalPath = path,
                BackupCreatedAtUtc = new DateTime(2026, 1, index + 1, 0, 0, 0, DateTimeKind.Utc),
                BackupType = BackupType.Manual,
                Status = BackupStatus.Success,
                CloudStatus = CloudBackupStatus.Uploaded,
                ApplicationVersion = "1.0.0.0",
                DatabaseVersion = "test",
                DeviceName = "test",
                CreatedByUser = "test-admin",
                FileSizeBytes = new FileInfo(path).Length
            });
        }
        db.BackupRecords.AddRange(records);
        await db.SaveChangesAsync();

        await scope.ServiceProvider.GetRequiredService<IBackupRetentionService>().EnforceAsync(5);

        Directory.GetFiles(_fixture.BackupDirectory, "Backup_*.zip").Should().HaveCount(5);
        (await db.BackupRecords.CountAsync()).Should().Be(7);
        File.Exists(records[^1].LocalPath).Should().BeTrue();
        File.Exists(records[0].LocalPath).Should().BeFalse();
    }

    [Fact]
    public async Task FailedNewBackup_PreservesEveryExistingBackup()
    {
        Directory.CreateDirectory(_fixture.BackupDirectory);
        var existingOne = Path.Combine(_fixture.BackupDirectory, "Backup_existing_1.zip");
        var existingTwo = Path.Combine(_fixture.BackupDirectory, "Backup_existing_2.zip");
        CreateMinimalReadableArchive(existingOne);
        CreateMinimalReadableArchive(existingTwo);
        var invalidDestination = Path.Combine(_fixture.BackupDirectory, "not-a-directory");
        await File.WriteAllTextAsync(invalidDestination, "occupied");

        using var scope = _fixture.ServiceProvider.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IBackupService>();
        var action = () => service.CreateBackupAsync(new BackupRequest(
            BackupType.Manual,
            DestinationDirectory: invalidDestination,
            CreatedByUser: "test-admin"));

        await action.Should().ThrowAsync<Exception>();
        File.Exists(existingOne).Should().BeTrue();
        File.Exists(existingTwo).Should().BeTrue();
        var failure = await scope.ServiceProvider.GetRequiredService<BakeryDbContext>()
            .BackupRecords.AsNoTracking().SingleAsync();
        failure.Status.Should().Be(BackupStatus.Failed);
        failure.ErrorSummary.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task NewBackupValidationFailure_PreservesExistingBackups()
    {
        var existing = CreateExistingBackupPair();
        using var scope = _fixture.ServiceProvider.CreateScope();
        var realValidator = scope.ServiceProvider.GetRequiredService<IBackupValidationService>();
        var service = CreateBackupService(scope.ServiceProvider,
            new RejectingValidationService(realValidator));

        var action = () => service.CreateBackupAsync(new BackupRequest(
            BackupType.Manual,
            DestinationDirectory: _fixture.BackupDirectory,
            CreatedByUser: "test-admin"));

        await action.Should().ThrowAsync<InvalidOperationException>();
        existing.Should().OnlyContain(path => File.Exists(path));
        var failure = await scope.ServiceProvider.GetRequiredService<BakeryDbContext>()
            .BackupRecords.AsNoTracking().SingleAsync();
        failure.Status.Should().Be(BackupStatus.Failed);
    }

    [Fact]
    public async Task FinalMoveFailure_PreservesExistingBackups()
    {
        var existing = CreateExistingBackupPair();
        using var scope = _fixture.ServiceProvider.CreateScope();
        var realValidator = scope.ServiceProvider.GetRequiredService<IBackupValidationService>();
        var service = CreateBackupService(scope.ServiceProvider,
            new CreateMoveCollisionValidationService(realValidator, _fixture.BackupDirectory));

        var action = () => service.CreateBackupAsync(new BackupRequest(
            BackupType.Manual,
            DestinationDirectory: _fixture.BackupDirectory,
            CreatedByUser: "test-admin"));

        await action.Should().ThrowAsync<InvalidOperationException>();
        existing.Should().OnlyContain(path => File.Exists(path));
        var failure = await scope.ServiceProvider.GetRequiredService<BakeryDbContext>()
            .BackupRecords.AsNoTracking().SingleAsync();
        failure.Status.Should().Be(BackupStatus.Failed);
    }

    [Fact]
    public async Task FinalReopenFailure_RemovesRejectedFile_AndPreservesExistingBackups()
    {
        var existing = CreateExistingBackupPair();
        using var scope = _fixture.ServiceProvider.CreateScope();
        var realValidator = scope.ServiceProvider.GetRequiredService<IBackupValidationService>();
        var service = CreateBackupService(scope.ServiceProvider,
            new RejectingFinalReopenValidationService(realValidator));

        var action = () => service.CreateBackupAsync(new BackupRequest(
            BackupType.Manual,
            DestinationDirectory: _fixture.BackupDirectory,
            CreatedByUser: "test-admin"));

        await action.Should().ThrowAsync<InvalidOperationException>();
        existing.Should().OnlyContain(path => File.Exists(path));
        var failure = await scope.ServiceProvider.GetRequiredService<BakeryDbContext>()
            .BackupRecords.AsNoTracking().SingleAsync();
        failure.Status.Should().Be(BackupStatus.Failed);
        File.Exists(failure.LocalPath).Should().BeFalse();
    }

    [Fact]
    public async Task OfflineAndCloudFailure_NeverChangeLocalSuccess_AndDoNotDuplicateUploads()
    {
        _fixture.CloudBackup.Connected = true;
        _fixture.Connectivity.IsNetworkAvailable = false;
        using var scope = _fixture.ServiceProvider.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IBackupService>();
        var backup = await service.CreateBackupAsync(new BackupRequest(
            BackupType.Manual,
            DestinationDirectory: _fixture.BackupDirectory,
            CreatedByUser: "test-admin"));
        backup.Status.Should().Be(BackupStatus.Success);
        backup.CloudStatus.Should().Be(CloudBackupStatus.PendingUpload);

        var queue = _fixture.ServiceProvider.GetRequiredService<BackupQueueService>();
        await queue.ProcessPendingUploadsAsync();
        _fixture.CloudBackup.UploadCount.Should().Be(0);

        _fixture.CloudBackup.FailUploads = true;
        _fixture.Connectivity.IsNetworkAvailable = true;
        await queue.ProcessPendingUploadsAsync();
        var record = await scope.ServiceProvider.GetRequiredService<BakeryDbContext>()
            .BackupRecords.AsNoTracking().SingleAsync();
        record.Status.Should().Be(BackupStatus.Success);
        record.CloudStatus.Should().Be(CloudBackupStatus.UploadFailed);
        File.Exists(record.LocalPath).Should().BeTrue();

        _fixture.CloudBackup.FailUploads = false;
        await queue.QueueCloudRetryAsync(backup.Id);
        await queue.ProcessPendingUploadsAsync();
        await queue.ProcessPendingUploadsAsync();
        _fixture.CloudBackup.UploadCount.Should().Be(2); // one failure, one success
    }

    [Fact]
    public async Task OperationGate_PreventsConcurrentBackupAndRestoreOperations()
    {
        var gate = _fixture.ServiceProvider.GetRequiredService<BackupOperationGate>();
        using var first = await gate.TryEnterAsync(CancellationToken.None);
        first.Should().NotBeNull();
        var second = await gate.TryEnterAsync(CancellationToken.None);
        second.Should().BeNull();

        using var scope = _fixture.ServiceProvider.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IBackupService>();
        var backupAction = () => service.CreateBackupAsync(new BackupRequest(
            BackupType.Manual,
            DestinationDirectory: _fixture.BackupDirectory,
            CreatedByUser: "test-admin"));
        await backupAction.Should().ThrowAsync<InvalidOperationException>();
        var restore = await scope.ServiceProvider.GetRequiredService<IRestoreService>()
            .RestoreLocalAsync(Path.Combine(_fixture.BackupDirectory, "missing.zip"));
        restore.Succeeded.Should().BeFalse();
    }

    [Fact]
    public async Task ManualBackup_EnforcesApplicationPermission()
    {
        using var scope = _fixture.ServiceProvider.CreateScope();
        var session = scope.ServiceProvider.GetRequiredService<IUserSessionService>();
        session.SignIn(new AuthenticatedUserDto(1, "limited", "Limited", [], false));
        var action = () => scope.ServiceProvider.GetRequiredService<IBackupService>().CreateBackupAsync();
        await action.Should().ThrowAsync<UnauthorizedAccessException>();
        SignInAdmin(session);
    }

    [Fact]
    public async Task StartupRecovery_ChangesInterruptedStatesWithoutScanningValidHistory()
    {
        using (var scope = _fixture.ServiceProvider.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<BakeryDbContext>();
            db.BackupRecords.Add(new BackupRecord
            {
                FileName = "interrupted.zip",
                LocalPath = Path.Combine(_fixture.BackupDirectory, "interrupted.zip"),
                BackupCreatedAtUtc = DateTime.UtcNow,
                BackupType = BackupType.Automatic,
                Status = BackupStatus.Validating,
                CloudStatus = CloudBackupStatus.Uploading,
                ApplicationVersion = "1",
                DatabaseVersion = "1",
                DeviceName = "test",
                CreatedByUser = "test"
            });
            await db.SaveChangesAsync();
        }

        await _fixture.ServiceProvider.GetRequiredService<IBackupStartupService>()
            .RunLightweightStartupRecoveryAsync();
        using var verificationScope = _fixture.ServiceProvider.CreateScope();
        var record = await verificationScope.ServiceProvider.GetRequiredService<BakeryDbContext>()
            .BackupRecords.AsNoTracking().SingleAsync();
        record.Status.Should().Be(BackupStatus.Failed);
        record.CloudStatus.Should().Be(CloudBackupStatus.PendingUpload);
    }

    private void ResetState()
    {
        _fixture.CloudBackup.Connected = false;
        _fixture.CloudBackup.FailUploads = false;
        _fixture.Connectivity.IsNetworkAvailable = false;
        try { if (Directory.Exists(_fixture.BackupDirectory)) Directory.Delete(_fixture.BackupDirectory, true); } catch { }
        Directory.CreateDirectory(_fixture.BackupDirectory);
        using var scope = _fixture.ServiceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BakeryDbContext>();
        db.BackupRecords.ExecuteDelete();
        SignInAdmin(scope.ServiceProvider.GetRequiredService<IUserSessionService>());
    }

    private static void SignInAdmin(IUserSessionService session)
        => session.SignIn(new AuthenticatedUserDto(
            1, "test-admin", "Test Admin",
            PermissionCatalog.All.Select(item => item.Key).ToArray(), true));

    private string[] CreateExistingBackupPair()
    {
        var paths = new[]
        {
            Path.Combine(_fixture.BackupDirectory, "Backup_existing_1.zip"),
            Path.Combine(_fixture.BackupDirectory, "Backup_existing_2.zip")
        };
        foreach (var path in paths) CreateMinimalReadableArchive(path);
        return paths;
    }

    private static BackupService CreateBackupService(
        IServiceProvider services,
        IBackupValidationService validationService)
        => new(
            services.GetRequiredService<BakeryDbContext>(),
            services.GetRequiredService<IPermissionService>(),
            services.GetRequiredService<IUserSessionService>(),
            validationService,
            services.GetRequiredService<BackupEncryptionService>(),
            services.GetRequiredService<IBackupRetentionService>(),
            services.GetRequiredService<ICloudBackupService>(),
            services.GetRequiredService<IAuditService>(),
            services.GetRequiredService<BackupPathProvider>(),
            services.GetRequiredService<IApplicationPathService>(),
            services.GetRequiredService<BackupOperationGate>(),
            services.GetRequiredService<IBackupStatusNotifier>(),
            services,
            services.GetRequiredService<Microsoft.Extensions.Logging.ILogger<BackupService>>());

    private static void CreateMinimalReadableArchive(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var stream = File.Create(path);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Create);
        using (var metadata = new StreamWriter(archive.CreateEntry("metadata.json").Open()))
            metadata.Write("{}");
        using (var database = archive.CreateEntry("database/database.bak").Open())
            database.WriteByte(1);
    }
}

public sealed class AutomaticBackupWorkflowTests : IClassFixture<DatabaseFixture>
{
    private readonly DatabaseFixture _fixture;
    public AutomaticBackupWorkflowTests(DatabaseFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task CloseDay_CommitsBeforeAutomaticBackup_AndCloudFailureCannotChangeCloseResult()
    {
        _fixture.CloudBackup.Connected = true;
        _fixture.CloudBackup.FailUploads = true;
        _fixture.Connectivity.IsNetworkAvailable = true;
        Guid operationId = Guid.NewGuid();
        int closedDayId;
        var wasCommittedWhenQueued = false;
        _fixture.AutomaticBackupQueue.OnAutomaticQueued = async queued =>
        {
            using var checkScope = _fixture.ServiceProvider.CreateScope();
            var state = await checkScope.ServiceProvider.GetRequiredService<BakeryDbContext>()
                .WorkingDays.AsNoTracking()
                .Where(item => item.Id == queued.WorkingDayId)
                .Select(item => item.Status)
                .SingleAsync();
            wasCommittedWhenQueued = state == WorkingDayStatus.Closed;
        };
        using (var scope = _fixture.ServiceProvider.CreateScope())
        {
            var workingDays = scope.ServiceProvider.GetRequiredService<IWorkingDayService>();
            var opened = await workingDays.OpenDayAsync(new OpenWorkingDayRequest(
                new DateOnly(2026, 7, 20), 0, "backup workflow test"));
            opened.Succeeded.Should().BeTrue();
            closedDayId = opened.Summary!.WorkingDayId;
            var readiness = await workingDays.GetEndOfDayReadinessAsync();
            readiness.Blockers.Should().BeEmpty();
            var closed = await workingDays.CloseCurrentDayAsync(new CloseWorkingDayRequest(
                0,
                readiness.Summary!.DailySafeBalance,
                "backup workflow test",
                ExpectedWorkingDayId: closedDayId,
                OperationId: operationId));
            closed.Succeeded.Should().BeTrue();

            var committedState = await scope.ServiceProvider.GetRequiredService<BakeryDbContext>()
                .WorkingDays.AsNoTracking().SingleAsync(item => item.Id == closedDayId);
            committedState.Status.Should().Be(WorkingDayStatus.Closed);
        }

        wasCommittedWhenQueued.Should().BeTrue();
        var queued = _fixture.AutomaticBackupQueue.LastAutomaticBackup;
        queued.Should().NotBeNull();
        queued!.WorkingDayId.Should().Be(closedDayId);
        queued.SourceOperationId.Should().Be(operationId);

        BackupMetadata automaticBackup;
        using (var backupScope = _fixture.ServiceProvider.CreateScope())
        {
            automaticBackup = await backupScope.ServiceProvider.GetRequiredService<IBackupService>()
                .CreateBackupAsync(new BackupRequest(
                    BackupType.Automatic,
                    queued.WorkingDayDate,
                    queued.WorkingDayId,
                    queued.SourceOperationId,
                    DestinationDirectory: _fixture.BackupDirectory,
                    CreatedByUser: queued.CreatedByUser,
                    EnforceUserPermission: false));
        }
        automaticBackup.Status.Should().Be(BackupStatus.Success);
        automaticBackup.CloudStatus.Should().Be(CloudBackupStatus.PendingUpload);
        File.Exists(automaticBackup.FilePath).Should().BeTrue();

        await _fixture.ServiceProvider.GetRequiredService<BackupQueueService>()
            .ProcessPendingUploadsAsync();
        using (var recordScope = _fixture.ServiceProvider.CreateScope())
        {
            var record = await recordScope.ServiceProvider.GetRequiredService<BakeryDbContext>()
                .BackupRecords.AsNoTracking().SingleAsync(item => item.SourceOperationId == operationId);
            record.Status.Should().Be(BackupStatus.Success);
            record.CloudStatus.Should().Be(CloudBackupStatus.UploadFailed);
        }

        using var finalScope = _fixture.ServiceProvider.CreateScope();
        var finalDay = await finalScope.ServiceProvider.GetRequiredService<BakeryDbContext>()
            .WorkingDays.AsNoTracking().SingleAsync(item => item.Id == closedDayId);
        finalDay.Status.Should().Be(WorkingDayStatus.Closed);
    }
}

public sealed class BackupRestoreWorkflowTests : IClassFixture<DatabaseFixture>
{
    private readonly DatabaseFixture _fixture;
    public BackupRestoreWorkflowTests(DatabaseFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task Restore_PartialExternalFailure_RollsBackDatabaseFilesAndGridSettings()
    {
        var paths = _fixture.ServiceProvider.GetRequiredService<IApplicationPathService>();
        paths.EnsureDirectoriesExist();
        var token = Guid.NewGuid().ToString("N");
        var markerKey = $"RestoreRollbackMarker-{token}";
        var attachmentPath = Path.Combine(paths.AttachmentsDirectory, $"restore-{token}.txt");
        var sourceDirectory = Path.Combine(paths.RootDirectory, "RestoreFailureSources", token);
        var gridExisted = File.Exists(paths.GridSettingsFile);
        var originalGrid = gridExisted ? await File.ReadAllTextAsync(paths.GridSettingsFile) : null;

        try
        {
            await File.WriteAllTextAsync(attachmentPath, "selected-attachment");
            await File.WriteAllTextAsync(paths.GridSettingsFile, "selected-grid");

            BackupMetadata selected;
            using (var scope = _fixture.ServiceProvider.CreateScope())
            {
                selected = await scope.ServiceProvider.GetRequiredService<IBackupService>()
                    .CreateBackupAsync(new BackupRequest(
                        BackupType.Manual,
                        DestinationDirectory: sourceDirectory,
                        CreatedByUser: "test-admin"));
            }

            await File.WriteAllTextAsync(attachmentPath, "current-attachment");
            await File.WriteAllTextAsync(paths.GridSettingsFile, "current-grid");
            using (var scope = _fixture.ServiceProvider.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<BakeryDbContext>();
                db.AppSettings.Add(new AppSetting { Key = markerKey, Value = "current-database" });
                await db.SaveChangesAsync();
            }

            _fixture.RestoreFailureInjector.FailOnce(
                RestoreCheckpoint.AfterExternalContentItem,
                "Attachments");

            RestoreResult result;
            using (var scope = _fixture.ServiceProvider.CreateScope())
            {
                result = await scope.ServiceProvider.GetRequiredService<IRestoreService>()
                    .RestoreLocalAsync(selected.FilePath);
            }

            result.Succeeded.Should().BeFalse();
            result.ErrorSummary.Should().Contain("إعادة البيانات الحالية");
            result.RecoveryDirectory.Should().BeNull("successful automatic rollback must clean staging data");
            (await File.ReadAllTextAsync(attachmentPath)).Should().Be("current-attachment");
            (await File.ReadAllTextAsync(paths.GridSettingsFile)).Should().Be("current-grid");
            using var verificationScope = _fixture.ServiceProvider.CreateScope();
            (await verificationScope.ServiceProvider.GetRequiredService<BakeryDbContext>()
                .AppSettings.AnyAsync(item => item.Key == markerKey)).Should().BeTrue();
        }
        finally
        {
            _fixture.RestoreFailureInjector.Reset();
            if (File.Exists(attachmentPath)) File.Delete(attachmentPath);
            if (gridExisted)
                await File.WriteAllTextAsync(paths.GridSettingsFile, originalGrid!);
            else if (File.Exists(paths.GridSettingsFile))
                File.Delete(paths.GridSettingsFile);
            if (Directory.Exists(sourceDirectory)) Directory.Delete(sourceDirectory, recursive: true);
        }
    }

    [Fact]
    public async Task Restore_RollbackFailure_PreservesManifestAndSafetyArchiveForManualRecovery()
    {
        var paths = _fixture.ServiceProvider.GetRequiredService<IApplicationPathService>();
        paths.EnsureDirectoriesExist();
        var token = Guid.NewGuid().ToString("N");
        var markerKey = $"RestoreManualRecoveryMarker-{token}";
        var sourceDirectory = Path.Combine(paths.RootDirectory, "RestoreFailureSources", token);
        string? recoveryDirectory = null;
        string? safetyArchive = null;

        try
        {
            BackupMetadata selected;
            using (var scope = _fixture.ServiceProvider.CreateScope())
            {
                selected = await scope.ServiceProvider.GetRequiredService<IBackupService>()
                    .CreateBackupAsync(new BackupRequest(
                        BackupType.Manual,
                        DestinationDirectory: sourceDirectory,
                        CreatedByUser: "test-admin"));
            }

            using (var scope = _fixture.ServiceProvider.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<BakeryDbContext>();
                db.AppSettings.Add(new AppSetting { Key = markerKey, Value = "recover-me" });
                await db.SaveChangesAsync();
            }

            _fixture.RestoreFailureInjector.FailInOrder(
                RestoreCheckpoint.AfterSelectedDatabaseRestore,
                RestoreCheckpoint.BeforeRollbackDatabase);

            RestoreResult failed;
            using (var scope = _fixture.ServiceProvider.CreateScope())
            {
                failed = await scope.ServiceProvider.GetRequiredService<IRestoreService>()
                    .RestoreLocalAsync(selected.FilePath);
            }

            failed.Succeeded.Should().BeFalse();
            failed.RecoveryDirectory.Should().NotBeNullOrWhiteSpace();
            recoveryDirectory = failed.RecoveryDirectory;
            var manifestPath = Path.Combine(recoveryDirectory!, "recovery-required.json");
            File.Exists(manifestPath).Should().BeTrue();
            using (var manifest = JsonDocument.Parse(await File.ReadAllTextAsync(manifestPath)))
            {
                manifest.RootElement.GetProperty("Status").GetString().Should().Be("ManualRecoveryRequired");
                safetyArchive = manifest.RootElement.GetProperty("SafetyArchivePath").GetString();
            }
            File.Exists(safetyArchive).Should().BeTrue();
            Directory.Exists(Path.Combine(recoveryDirectory!, "selected")).Should().BeTrue();
            Directory.Exists(Path.Combine(recoveryDirectory!, "safety")).Should().BeTrue();

            _fixture.RestoreFailureInjector.Reset();
            RestoreResult recovered;
            using (var scope = _fixture.ServiceProvider.CreateScope())
            {
                recovered = await scope.ServiceProvider.GetRequiredService<IRestoreService>()
                    .RestoreLocalAsync(safetyArchive!);
            }
            recovered.Succeeded.Should().BeTrue(recovered.ErrorSummary);
            using var verificationScope = _fixture.ServiceProvider.CreateScope();
            (await verificationScope.ServiceProvider.GetRequiredService<BakeryDbContext>()
                .AppSettings.AnyAsync(item => item.Key == markerKey)).Should().BeTrue();
        }
        finally
        {
            _fixture.RestoreFailureInjector.Reset();
            if (recoveryDirectory is not null && Directory.Exists(recoveryDirectory))
                Directory.Delete(recoveryDirectory, recursive: true);
            if (Directory.Exists(sourceDirectory)) Directory.Delete(sourceDirectory, recursive: true);
        }
    }

    [Fact]
    public async Task Restore_RequiresValidatedSafetyBackup_ThenRestoresSelectedDatabase()
    {
        const string backupPassword = "Correct-Horse-Battery-Staple-2026";
        var sourceDirectory = Path.Combine(Path.GetDirectoryName(_fixture.BackupDirectory)!, "RestoreSource");
        Directory.CreateDirectory(sourceDirectory);
        BackupMetadata selected;
        using (var scope = _fixture.ServiceProvider.CreateScope())
        {
            selected = await scope.ServiceProvider.GetRequiredService<IBackupService>()
                .CreateBackupAsync(new BackupRequest(
                    BackupType.Manual,
                    DestinationDirectory: sourceDirectory,
                    CreatedByUser: "test-admin",
                    EncryptionPassword: backupPassword));
        }

        using (var mutationScope = _fixture.ServiceProvider.CreateScope())
        {
            var db = mutationScope.ServiceProvider.GetRequiredService<BakeryDbContext>();
            db.AppSettings.Add(new AppSetting { Key = "RestoreSafetyMarker", Value = "current-data" });
            await db.SaveChangesAsync();
        }

        if (Directory.Exists(_fixture.BackupDirectory)) Directory.Delete(_fixture.BackupDirectory, true);
        await File.WriteAllTextAsync(_fixture.BackupDirectory, "blocks safety backup directory");
        RestoreResult cancelled;
        using (var restoreScope = _fixture.ServiceProvider.CreateScope())
        {
            cancelled = await restoreScope.ServiceProvider.GetRequiredService<IRestoreService>()
                .RestoreLocalAsync(selected.FilePath, backupPassword);
        }
        cancelled.Succeeded.Should().BeFalse();
        using (var verificationScope = _fixture.ServiceProvider.CreateScope())
        {
            var markerStillExists = await verificationScope.ServiceProvider.GetRequiredService<BakeryDbContext>()
                .AppSettings.AnyAsync(item => item.Key == "RestoreSafetyMarker");
            markerStillExists.Should().BeTrue("a failed safety backup must cancel restore before replacement");
        }

        File.Delete(_fixture.BackupDirectory);
        Directory.CreateDirectory(_fixture.BackupDirectory);
        RestoreResult restored;
        using (var restoreScope = _fixture.ServiceProvider.CreateScope())
        {
            restored = await restoreScope.ServiceProvider.GetRequiredService<IRestoreService>()
                .RestoreLocalAsync(selected.FilePath, backupPassword);
        }
        restored.Succeeded.Should().BeTrue(restored.ErrorSummary);
        using var finalScope = _fixture.ServiceProvider.CreateScope();
        var markerAfterRestore = await finalScope.ServiceProvider.GetRequiredService<BakeryDbContext>()
            .AppSettings.AnyAsync(item => item.Key == "RestoreSafetyMarker");
        markerAfterRestore.Should().BeFalse();
        (await finalScope.ServiceProvider.GetRequiredService<BakeryDbContext>().Database.CanConnectAsync()).Should().BeTrue();
    }
}

public sealed class TestRestoreFailureInjector : IRestoreFailureInjector
{
    private readonly object _sync = new();
    private readonly Queue<(RestoreCheckpoint Checkpoint, string? ItemName)> _failures = new();

    public void FailOnce(RestoreCheckpoint checkpoint, string? itemName = null)
    {
        lock (_sync)
        {
            _failures.Clear();
            _failures.Enqueue((checkpoint, itemName));
        }
    }

    public void FailInOrder(params RestoreCheckpoint[] checkpoints)
    {
        lock (_sync)
        {
            _failures.Clear();
            foreach (var checkpoint in checkpoints)
                _failures.Enqueue((checkpoint, null));
        }
    }

    public void ThrowIfRequested(RestoreCheckpoint checkpoint, string? itemName = null)
    {
        lock (_sync)
        {
            if (!_failures.TryPeek(out var failure) ||
                failure.Checkpoint != checkpoint ||
                (failure.ItemName is not null && !string.Equals(failure.ItemName, itemName, StringComparison.Ordinal)))
                return;

            _failures.Dequeue();
            throw new IOException($"Injected restore failure at {checkpoint} ({itemName}).");
        }
    }

    public void Reset()
    {
        lock (_sync)
        {
            _failures.Clear();
        }
    }
}

public sealed class BackupQueueShutdownTests : IClassFixture<DatabaseFixture>
{
    private readonly DatabaseFixture _fixture;
    public BackupQueueShutdownTests(DatabaseFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task ImmediateShutdown_DrainsCommittedAutomaticBackupBeforeStopping()
    {
        _fixture.Connectivity.IsNetworkAvailable = false;
        var queue = _fixture.ServiceProvider.GetRequiredService<BackupQueueService>();
        await queue.StartAsync(CancellationToken.None);
        await _fixture.ServiceProvider.GetRequiredService<IBackupStartupService>()
            .RunLightweightStartupRecoveryAsync();

        var operationId = Guid.NewGuid();
        await queue.QueueAutomaticBackupAsync(
            new DateOnly(2026, 7, 20),
            workingDayId: 0,
            operationId,
            "test-admin");

        await queue.StopAsync(CancellationToken.None);

        using var scope = _fixture.ServiceProvider.CreateScope();
        var record = await scope.ServiceProvider.GetRequiredService<BakeryDbContext>()
            .BackupRecords.AsNoTracking()
            .SingleAsync(item => item.SourceOperationId == operationId);
        record.Status.Should().Be(BackupStatus.Success);
        File.Exists(record.LocalPath).Should().BeTrue();
    }
}

internal sealed class RejectingValidationService : IBackupValidationService
{
    private readonly IBackupValidationService _inner;
    public RejectingValidationService(IBackupValidationService inner) => _inner = inner;

    public Task<BackupValidationResult> ValidateAsync(
        string archivePath,
        CancellationToken cancellationToken = default)
        => Task.FromResult(new BackupValidationResult(false, ErrorSummary: "Injected validation failure."));

    public Task<bool> CanOpenArchiveAsync(
        string archivePath,
        CancellationToken cancellationToken = default)
        => _inner.CanOpenArchiveAsync(archivePath, cancellationToken);
}

internal sealed class CreateMoveCollisionValidationService : IBackupValidationService
{
    private readonly IBackupValidationService _inner;
    private readonly string _destinationDirectory;

    public CreateMoveCollisionValidationService(
        IBackupValidationService inner,
        string destinationDirectory)
    {
        _inner = inner;
        _destinationDirectory = destinationDirectory;
    }

    public async Task<BackupValidationResult> ValidateAsync(
        string archivePath,
        CancellationToken cancellationToken = default)
    {
        var result = await _inner.ValidateAsync(archivePath, cancellationToken);
        if (result.IsValid)
        {
            const string partialSuffix = ".partial";
            var fileName = Path.GetFileName(archivePath);
            if (fileName.EndsWith(partialSuffix, StringComparison.OrdinalIgnoreCase))
                fileName = fileName[..^partialSuffix.Length];
            await File.WriteAllTextAsync(
                Path.Combine(_destinationDirectory, fileName),
                "injected move collision",
                cancellationToken);
        }
        return result;
    }

    public Task<bool> CanOpenArchiveAsync(
        string archivePath,
        CancellationToken cancellationToken = default)
        => _inner.CanOpenArchiveAsync(archivePath, cancellationToken);
}

internal sealed class RejectingFinalReopenValidationService : IBackupValidationService
{
    private readonly IBackupValidationService _inner;
    public RejectingFinalReopenValidationService(IBackupValidationService inner) => _inner = inner;

    public Task<BackupValidationResult> ValidateAsync(
        string archivePath,
        CancellationToken cancellationToken = default)
        => _inner.ValidateAsync(archivePath, cancellationToken);

    public Task<bool> CanOpenArchiveAsync(
        string archivePath,
        CancellationToken cancellationToken = default)
        => Task.FromResult(false);
}
