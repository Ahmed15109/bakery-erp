using System.IO.Compression;
using System.Reflection;
using System.Text.Json;
using Bakery.Application.DTOs;
using Bakery.Application.Interfaces;
using Bakery.Application.Security;
using Bakery.Domain.Entities;
using Bakery.Domain.Enums;
using Bakery.Infrastructure.Data;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Bakery.Infrastructure.Services.Backup;

public sealed class BackupRestoreService : IRestoreService
{
    private readonly BakeryDbContext _dbContext;
    private readonly BackupService _backupService;
    private readonly IBackupValidationService _validationService;
    private readonly BackupEncryptionService _encryptionService;
    private readonly ICloudBackupService _cloudBackupService;
    private readonly IPermissionService _permissionService;
    private readonly IUserSessionService _userSessionService;
    private readonly IAuditService _auditService;
    private readonly BackupOperationGate _operationGate;
    private readonly BackupPathProvider _pathProvider;
    private readonly IApplicationPathService _applicationPaths;
    private readonly IBackupStatusNotifier _statusNotifier;
    private readonly IRestoreFailureInjector _failureInjector;
    private readonly ILogger<BackupRestoreService> _logger;

    public BackupRestoreService(
        BakeryDbContext dbContext,
        BackupService backupService,
        IBackupValidationService validationService,
        BackupEncryptionService encryptionService,
        ICloudBackupService cloudBackupService,
        IPermissionService permissionService,
        IUserSessionService userSessionService,
        IAuditService auditService,
        BackupOperationGate operationGate,
        BackupPathProvider pathProvider,
        IApplicationPathService applicationPaths,
        IBackupStatusNotifier statusNotifier,
        IRestoreFailureInjector failureInjector,
        ILogger<BackupRestoreService> logger)
    {
        _dbContext = dbContext;
        _backupService = backupService;
        _validationService = validationService;
        _encryptionService = encryptionService;
        _cloudBackupService = cloudBackupService;
        _permissionService = permissionService;
        _userSessionService = userSessionService;
        _auditService = auditService;
        _operationGate = operationGate;
        _pathProvider = pathProvider;
        _applicationPaths = applicationPaths;
        _statusNotifier = statusNotifier;
        _failureInjector = failureInjector;
        _logger = logger;
    }

    public async Task<RestoreResult> RestoreLocalAsync(
        string archivePath,
        CancellationToken cancellationToken = default)
        => await RestoreLocalAsync(archivePath, null, cancellationToken);

    public async Task<RestoreResult> RestoreLocalAsync(
        string archivePath,
        string? password,
        CancellationToken cancellationToken = default)
    {
        EnsureRestorePermission();
        using var lease = await _operationGate.TryEnterAsync(cancellationToken);
        if (lease is null) return new RestoreResult(false, "توجد عملية نسخ احتياطي أو استعادة قيد التنفيذ بالفعل.");
        return await RestoreCoreAsync(Path.GetFullPath(archivePath), password, cancellationToken);
    }

    public async Task<RestoreResult> RestoreHistoryAsync(
        int backupRecordId,
        CancellationToken cancellationToken = default)
        => await RestoreHistoryAsync(backupRecordId, null, cancellationToken);

    public async Task<RestoreResult> RestoreHistoryAsync(
        int backupRecordId,
        string? password,
        CancellationToken cancellationToken = default)
    {
        EnsureRestorePermission();
        using var lease = await _operationGate.TryEnterAsync(cancellationToken);
        if (lease is null) return new RestoreResult(false, "توجد عملية نسخ احتياطي أو استعادة قيد التنفيذ بالفعل.");
        var path = await _dbContext.BackupRecords
            .AsNoTracking()
            .Where(item => item.Id == backupRecordId)
            .Select(item => item.LocalPath)
            .SingleOrDefaultAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            return new RestoreResult(false, "ملف النسخة الاحتياطية المحلية غير متوفر.");
        return await RestoreCoreAsync(path, password, cancellationToken);
    }

    public async Task<RestoreResult> RestoreCloudAsync(
        int backupRecordId,
        CancellationToken cancellationToken = default)
        => await RestoreCloudAsync(backupRecordId, null, cancellationToken);

    public async Task<RestoreResult> RestoreCloudAsync(
        int backupRecordId,
        string? password,
        CancellationToken cancellationToken = default)
    {
        EnsureRestorePermission();
        using var lease = await _operationGate.TryEnterAsync(cancellationToken);
        if (lease is null) return new RestoreResult(false, "توجد عملية نسخ احتياطي أو استعادة قيد التنفيذ بالفعل.");
        var record = await _dbContext.BackupRecords
            .AsNoTracking()
            .SingleOrDefaultAsync(item => item.Id == backupRecordId, cancellationToken);
        if (record is null || string.IsNullOrWhiteSpace(record.GoogleDriveFileId))
            return new RestoreResult(false, "النسخة الاحتياطية غير متوفرة على Google Drive.");

        var downloadDirectory = Path.Combine(
            _applicationPaths.BackupDownloadsDirectory, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(downloadDirectory);
        var downloadPath = Path.Combine(downloadDirectory, record.FileName);
        try
        {
            await _cloudBackupService.DownloadAsync(record.GoogleDriveFileId, downloadPath, cancellationToken);
            return await RestoreCoreAsync(downloadPath, password, cancellationToken);
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Cloud restore download failed for backup record {BackupRecordId}", backupRecordId);
            return new RestoreResult(false, BackupError.Summarize(exception));
        }
        finally
        {
            BackupValidationService.TryDeleteDirectory(downloadDirectory);
        }
    }

    private async Task<RestoreResult> RestoreCoreAsync(
        string archivePath,
        string? password,
        CancellationToken cancellationToken)
    {
        string? operationDirectory = null;
        string? restoreDirectory = null;
        string? safetyDirectory = null;
        BackupMetadata? safetyBackup = null;
        var preserveRecoveryState = false;
        await TryAuditAsync(AuditActionKeys.BackupRestoreAttempted, null, null, cancellationToken);
        try
        {
            var validation = await _validationService.ValidateAsync(
                archivePath, password, cancellationToken);
            if (!validation.IsValid || validation.Metadata is null)
                return await FailedAsync(validation.ErrorSummary ?? "النسخة الاحتياطية غير صالحة.", cancellationToken);
            var compatibilityError = await GetCompatibilityErrorAsync(validation.Metadata, cancellationToken);
            if (compatibilityError is not null) return await FailedAsync(compatibilityError, cancellationToken);

            safetyBackup = await _backupService.CreateBackupCoreAsync(
                new BackupRequest(
                    BackupType.SafetyBeforeRestore,
                    CreatedByUser: _userSessionService.Username,
                    EnforceUserPermission: false),
                persistHistoryWhenAvailable: true,
                cancellationToken,
                runRetention: false);
            var safetyValidation = await _validationService.ValidateAsync(safetyBackup.FilePath, cancellationToken);
            if (!safetyValidation.IsValid ||
                !await _validationService.CanOpenArchiveAsync(safetyBackup.FilePath, cancellationToken))
            {
                return await FailedAsync("تعذر إنشاء نسخة أمان صالحة؛ تم إلغاء الاستعادة دون تغيير البيانات.", cancellationToken);
            }

            operationDirectory = CreateRestoreOperationDirectory();
            restoreDirectory = Path.Combine(operationDirectory, "selected");
            safetyDirectory = Path.Combine(operationDirectory, "safety");
            Directory.CreateDirectory(restoreDirectory);
            Directory.CreateDirectory(safetyDirectory);
            using (var selectedArchive = await _encryptionService.PrepareReadAsync(
                       archivePath, password, cancellationToken))
            using (var safetyArchive = await _encryptionService.PrepareReadAsync(
                       safetyBackup.FilePath, null, cancellationToken))
            {
                await ExtractArchiveAsync(selectedArchive.ArchivePath, restoreDirectory, cancellationToken);
                await ExtractArchiveAsync(safetyArchive.ArchivePath, safetyDirectory, cancellationToken);
            }

            var selectedDatabase = Path.Combine(restoreDirectory, "database", "database.bak");
            var safetyDatabase = Path.Combine(safetyDirectory, "database", "database.bak");
            try
            {
                await RestoreDatabaseAsync(selectedDatabase, cancellationToken);
                _failureInjector.ThrowIfRequested(RestoreCheckpoint.AfterSelectedDatabaseRestore);
                RestoreExternalContent(restoreDirectory, allowFailureInjection: true);
            }
            catch (Exception replacementException)
            {
                _logger.LogError(replacementException, "Restore replacement failed; rolling back from safety backup {SafetyPath}", safetyBackup.FilePath);
                try
                {
                    _failureInjector.ThrowIfRequested(RestoreCheckpoint.BeforeRollbackDatabase);
                    await RestoreDatabaseAsync(safetyDatabase, CancellationToken.None);
                    RestoreExternalContent(safetyDirectory, allowFailureInjection: false);
                }
                catch (Exception rollbackException)
                {
                    _logger.LogCritical(rollbackException, "Restore rollback failed after replacement failure");
                    preserveRecoveryState = true;
                    var recoveryDirectory = operationDirectory!;
                    WriteRecoveryManifest(
                        recoveryDirectory,
                        archivePath,
                        safetyBackup.FilePath,
                        replacementException,
                        rollbackException);
                    return await FailedAsync(
                        "فشلت الاستعادة وتعذر إكمال الرجوع التلقائي. أوقف الاستخدام فوراً؛ تم حفظ ملفات التعافي للمسؤول الفني.",
                        CancellationToken.None,
                        recoveryDirectory);
                }
                return await FailedAsync("فشلت الاستعادة وتمت إعادة البيانات الحالية من نسخة الأمان.", CancellationToken.None);
            }

            await TryAuditAsync(AuditActionKeys.BackupRestoreSucceeded, null, null, CancellationToken.None);
            _statusNotifier.NotifyChanged();
            return new RestoreResult(true);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Restore failed before data replacement");
            return await FailedAsync(BackupError.Summarize(exception), CancellationToken.None);
        }
        finally
        {
            if (!preserveRecoveryState && operationDirectory is not null)
                BackupValidationService.TryDeleteDirectory(operationDirectory);
        }
    }

    private async Task<string?> GetCompatibilityErrorAsync(
        BackupArchiveMetadata metadata,
        CancellationToken cancellationToken)
    {
        if (!string.Equals(metadata.BackupVersion, "1", StringComparison.Ordinal))
            return "إصدار ملف النسخة الاحتياطية غير مدعوم.";
        var currentDatabaseVersion = (await _dbContext.Database.GetAppliedMigrationsAsync(cancellationToken)).LastOrDefault() ?? "Initial";
        if (!string.Equals(metadata.DatabaseSchemaVersion, currentDatabaseVersion, StringComparison.Ordinal))
            return "إصدار قاعدة البيانات في النسخة الاحتياطية لا يطابق إصدار التطبيق الحالي.";

        var currentVersion = Assembly.GetEntryAssembly()?.GetName().Version;
        if (currentVersion is not null && Version.TryParse(metadata.ApplicationVersion, out var backupVersion) &&
            backupVersion.Major > currentVersion.Major)
        {
            return "تم إنشاء النسخة الاحتياطية بواسطة إصدار أحدث وغير متوافق من التطبيق.";
        }
        return null;
    }

    private async Task RestoreDatabaseAsync(string databaseBackupPath, CancellationToken cancellationToken)
    {
        if (!File.Exists(databaseBackupPath)) throw new FileNotFoundException("Database backup is missing.", databaseBackupPath);
        var activeConnectionString = _dbContext.Database.GetConnectionString()
            ?? throw new InvalidOperationException("Database connection is unavailable.");
        var activeBuilder = new SqlConnectionStringBuilder(activeConnectionString);
        var databaseName = activeBuilder.InitialCatalog;
        if (string.IsNullOrWhiteSpace(databaseName)) throw new InvalidOperationException("Database name is unavailable.");
        await _dbContext.Database.CloseConnectionAsync();
        _dbContext.ChangeTracker.Clear();
        SqlConnection.ClearAllPools();
        activeBuilder.InitialCatalog = "master";
        await using var connection = new SqlConnection(activeBuilder.ConnectionString);
        await connection.OpenAsync(cancellationToken);
        try
        {
            await using var command = connection.CreateCommand();
            command.CommandTimeout = 0;
            command.CommandText = $"ALTER DATABASE {QuoteIdentifier(databaseName)} SET SINGLE_USER WITH ROLLBACK IMMEDIATE; " +
                                  $"RESTORE DATABASE {QuoteIdentifier(databaseName)} FROM DISK = @path WITH REPLACE, CHECKSUM; " +
                                  $"ALTER DATABASE {QuoteIdentifier(databaseName)} SET MULTI_USER;";
            command.Parameters.AddWithValue("@path", databaseBackupPath);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        catch
        {
            try
            {
                await using var repair = connection.CreateCommand();
                repair.CommandText = $"IF DB_ID(@databaseName) IS NOT NULL ALTER DATABASE {QuoteIdentifier(databaseName)} SET MULTI_USER WITH ROLLBACK IMMEDIATE;";
                repair.Parameters.AddWithValue("@databaseName", databaseName);
                await repair.ExecuteNonQueryAsync(CancellationToken.None);
            }
            catch { }
            throw;
        }
        finally
        {
            SqlConnection.ClearAllPools();
        }
    }

    private static async Task ExtractArchiveAsync(
        string archivePath,
        string destinationDirectory,
        CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(
            archivePath, FileMode.Open, FileAccess.Read, FileShare.Read,
            64 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
        var root = Path.GetFullPath(destinationDirectory) + Path.DirectorySeparatorChar;
        foreach (var entry in archive.Entries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var path = Path.GetFullPath(Path.Combine(destinationDirectory, entry.FullName.Replace('/', Path.DirectorySeparatorChar)));
            if (!path.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Backup archive contains an unsafe path.");
            if (string.IsNullOrEmpty(entry.Name))
            {
                Directory.CreateDirectory(path);
                continue;
            }
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await using var source = entry.Open();
            await using var destination = new FileStream(
                path, FileMode.Create, FileAccess.Write, FileShare.None,
                64 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
            await source.CopyToAsync(destination, cancellationToken);
        }
    }

    private void RestoreExternalContent(string extractedDirectory, bool allowFailureInjection)
    {
        foreach (var (name, destination) in GetExternalContentDestinations())
        {
            var source = Path.Combine(extractedDirectory, "content", name);
            Directory.CreateDirectory(source);
            ReplaceDirectory(source, destination);
            if (allowFailureInjection)
                _failureInjector.ThrowIfRequested(RestoreCheckpoint.AfterExternalContentItem, name);
        }
        var gridSettings = Path.Combine(extractedDirectory, "settings", "grid_settings.json");
        ReplaceOptionalFile(File.Exists(gridSettings) ? gridSettings : null, _applicationPaths.GridSettingsFile);
        if (allowFailureInjection)
            _failureInjector.ThrowIfRequested(RestoreCheckpoint.AfterExternalContentItem, "grid_settings.json");
    }

    private static void ReplaceDirectory(string source, string destination)
    {
        var old = destination + ".restore-old-" + Guid.NewGuid().ToString("N");
        try
        {
            if (Directory.Exists(destination)) Directory.Move(destination, old);
            CopyDirectory(source, destination);
            if (Directory.Exists(old)) Directory.Delete(old, recursive: true);
        }
        catch
        {
            try
            {
                if (Directory.Exists(destination)) Directory.Delete(destination, recursive: true);
                if (Directory.Exists(old)) Directory.Move(old, destination);
            }
            catch { }
            throw;
        }
    }

    private static void CopyDirectory(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (var directory in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories))
            Directory.CreateDirectory(Path.Combine(destination, Path.GetRelativePath(source, directory)));
        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            var target = Path.Combine(destination, Path.GetRelativePath(source, file));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target, overwrite: true);
        }
    }

    private static void ReplaceOptionalFile(string? source, string destination)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        var old = destination + ".restore-old-" + Guid.NewGuid().ToString("N");
        var staged = destination + ".restore-new-" + Guid.NewGuid().ToString("N");
        try
        {
            if (File.Exists(destination)) File.Move(destination, old);
            if (source is not null)
            {
                File.Copy(source, staged, overwrite: false);
                File.Move(staged, destination);
            }
            if (File.Exists(old)) File.Delete(old);
        }
        catch
        {
            try
            {
                if (File.Exists(staged)) File.Delete(staged);
                if (File.Exists(destination)) File.Delete(destination);
                if (File.Exists(old)) File.Move(old, destination);
            }
            catch { }
            throw;
        }
    }

    private void EnsureRestorePermission()
    {
        if (_userSessionService.IsAuthenticated)
            _permissionService.EnsurePermission(PermissionKeys.BackupRestore);
    }

    private async Task<RestoreResult> FailedAsync(
        string summary,
        CancellationToken cancellationToken,
        string? recoveryDirectory = null)
    {
        await TryAuditAsync(AuditActionKeys.BackupRestoreFailed, summary, null, cancellationToken);
        _statusNotifier.NotifyChanged();
        return new RestoreResult(false, summary, recoveryDirectory);
    }

    private async Task TryAuditAsync(
        string action,
        string? errorSummary,
        int? recordId,
        CancellationToken cancellationToken)
    {
        try
        {
            _dbContext.ChangeTracker.Clear();
            await _auditService.LogAsync(action, nameof(BackupRecord), recordId, null,
                JsonSerializer.Serialize(new
                {
                    Operation = action,
                    Result = errorSummary is null ? "Succeeded" : "Failed",
                    BackupRecordId = recordId,
                    Destination = "Local",
                    ErrorSummary = errorSummary
                }), cancellationToken);
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Unable to write restore audit action {Action}", action);
        }
    }

    private string CreateRestoreOperationDirectory()
    {
        var directory = Path.Combine(
            _applicationPaths.RestoreWorkDirectory,
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return directory;
    }

    private void WriteRecoveryManifest(
        string recoveryDirectory,
        string selectedArchivePath,
        string safetyArchivePath,
        Exception replacementException,
        Exception rollbackException)
    {
        try
        {
            var manifest = new
            {
                Status = "ManualRecoveryRequired",
                OccurredAtUtc = DateTimeOffset.UtcNow,
                SelectedArchivePath = selectedArchivePath,
                SafetyArchivePath = safetyArchivePath,
                SelectedStagingDirectory = Path.Combine(recoveryDirectory, "selected"),
                SafetyStagingDirectory = Path.Combine(recoveryDirectory, "safety"),
                ReplacementErrorType = replacementException.GetType().FullName,
                RollbackErrorType = rollbackException.GetType().FullName
            };
            File.WriteAllText(
                Path.Combine(recoveryDirectory, "recovery-required.json"),
                JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception manifestException)
        {
            _logger.LogCritical(manifestException, "Unable to write restore recovery manifest in {RecoveryDirectory}", recoveryDirectory);
        }
    }

    private IEnumerable<(string Name, string Destination)> GetExternalContentDestinations()
    {
        yield return ("Attachments", _applicationPaths.AttachmentsDirectory);
        yield return ("Documents", _applicationPaths.DocumentsDirectory);
        yield return ("Templates", _applicationPaths.TemplatesDirectory);
        yield return ("Logos", _applicationPaths.LogosDirectory);
    }

    private static string QuoteIdentifier(string value) => $"[{value.Replace("]", "]]", StringComparison.Ordinal)}]";
}
