using System.IO.Compression;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using Bakery.Application.DTOs;
using Bakery.Application.Interfaces;
using Bakery.Application.Security;
using Bakery.Domain.Entities;
using Bakery.Domain.Enums;
using Bakery.Infrastructure.Data;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Bakery.Infrastructure.Services.Backup;

public sealed class BackupService : IBackupService
{
    private const string BackupFormatVersion = "1";
    private static readonly JsonSerializerOptions MetadataJsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly BakeryDbContext _dbContext;
    private readonly IPermissionService _permissionService;
    private readonly IUserSessionService _userSessionService;
    private readonly IBackupValidationService _validationService;
    private readonly BackupEncryptionService _encryptionService;
    private readonly IBackupRetentionService _retentionService;
    private readonly ICloudBackupService _cloudBackupService;
    private readonly IAuditService _auditService;
    private readonly BackupPathProvider _pathProvider;
    private readonly IApplicationPathService _applicationPaths;
    private readonly BackupOperationGate _operationGate;
    private readonly IBackupStatusNotifier _statusNotifier;
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<BackupService> _logger;

    public BackupService(
        BakeryDbContext dbContext,
        IPermissionService permissionService,
        IUserSessionService userSessionService,
        IBackupValidationService validationService,
        BackupEncryptionService encryptionService,
        IBackupRetentionService retentionService,
        ICloudBackupService cloudBackupService,
        IAuditService auditService,
        BackupPathProvider pathProvider,
        IApplicationPathService applicationPaths,
        BackupOperationGate operationGate,
        IBackupStatusNotifier statusNotifier,
        IServiceProvider serviceProvider,
        ILogger<BackupService> logger)
    {
        _dbContext = dbContext;
        _permissionService = permissionService;
        _userSessionService = userSessionService;
        _validationService = validationService;
        _encryptionService = encryptionService;
        _retentionService = retentionService;
        _cloudBackupService = cloudBackupService;
        _auditService = auditService;
        _pathProvider = pathProvider;
        _applicationPaths = applicationPaths;
        _operationGate = operationGate;
        _statusNotifier = statusNotifier;
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    public async Task<string> CreateBackupAsync(
        string? customPath = null,
        string? password = null,
        CancellationToken cancellationToken = default)
    {
        var result = await CreateBackupAsync(
            new BackupRequest(
                BackupType.Manual,
                DestinationDirectory: customPath,
                CreatedByUser: _userSessionService.Username,
                EnforceUserPermission: true,
                EncryptionPassword: password),
            cancellationToken);
        return result.FilePath;
    }

    public async Task<BackupMetadata> CreateBackupAsync(
        BackupRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request.EnforceUserPermission)
        {
            var permission = request.BackupType == BackupType.Manual
                ? PermissionKeys.BackupCreateManual
                : PermissionKeys.BackupManageSettings;
            _permissionService.EnsurePermission(permission);
        }

        using var lease = await _operationGate.TryEnterAsync(cancellationToken)
            ?? throw new InvalidOperationException("توجد عملية نسخ احتياطي أو استعادة قيد التنفيذ بالفعل.");

        if (request.SourceOperationId.HasValue)
        {
            var existing = await _dbContext.BackupRecords
                .AsNoTracking()
                .FirstOrDefaultAsync(item => item.SourceOperationId == request.SourceOperationId, cancellationToken);
            if (existing is not null) return Map(existing);
        }

        return await CreateBackupCoreAsync(request, persistHistoryWhenAvailable: true, cancellationToken, runRetention: true);
    }

    public async Task<string> CreateSafetySnapshotAsync(
        string operationName,
        CancellationToken cancellationToken = default)
    {
        using var lease = await _operationGate.TryEnterAsync(cancellationToken)
            ?? throw new InvalidOperationException("توجد عملية نسخ احتياطي أو استعادة قيد التنفيذ بالفعل.");
        var result = await CreateBackupCoreAsync(
            new BackupRequest(
                BackupType.SafetyBeforeRestore,
                CreatedByUser: _userSessionService.Username,
                EnforceUserPermission: false),
            persistHistoryWhenAvailable: true,
            cancellationToken,
            runRetention: true);
        return result.FilePath;
    }

    public async Task RestoreBackupAsync(string backupFilePath, CancellationToken cancellationToken = default)
        => await RestoreBackupAsync(backupFilePath, null, cancellationToken);

    public async Task RestoreBackupAsync(
        string backupFilePath,
        string? password,
        CancellationToken cancellationToken = default)
    {
        var restoreService = _serviceProvider.GetRequiredService<IRestoreService>();
        var result = await restoreService.RestoreLocalAsync(backupFilePath, password, cancellationToken);
        if (!result.Succeeded) throw new InvalidOperationException(result.ErrorSummary);
    }

    public async Task<IEnumerable<BackupMetadata>> GetBackupHistoryAsync(
        CancellationToken cancellationToken = default)
    {
        if (_userSessionService.IsAuthenticated)
            _permissionService.EnsurePermission(PermissionKeys.BackupViewStatus);

        if (!await HistoryTableExistsAsync(cancellationToken)) return [];
        var records = await _dbContext.BackupRecords
            .AsNoTracking()
            .OrderByDescending(item => item.BackupCreatedAtUtc)
            .Take(250)
            .ToListAsync(cancellationToken);
        return records.Select(Map).ToArray();
    }

    public async Task<BackupStatusSummary> GetStatusSummaryAsync(
        CancellationToken cancellationToken = default)
    {
        _permissionService.EnsurePermission(PermissionKeys.BackupViewStatus);
        var latest = await _dbContext.BackupRecords
            .AsNoTracking()
            .OrderByDescending(item => item.BackupCreatedAtUtc)
            .Select(item => new
            {
                item.Status,
                item.CloudStatus,
                item.ErrorSummary,
                item.BackupCreatedAtUtc
            })
            .FirstOrDefaultAsync(cancellationToken);
        var lastSuccess = await _dbContext.BackupRecords
            .AsNoTracking()
            .Where(item => item.Status == BackupStatus.Success)
            .OrderByDescending(item => item.BackupCreatedAtUtc)
            .Select(item => (DateTime?)item.BackupCreatedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);
        var pending = await _dbContext.BackupRecords
            .AsNoTracking()
            .CountAsync(item => item.Status == BackupStatus.Success &&
                (item.CloudStatus == CloudBackupStatus.PendingUpload ||
                 item.CloudStatus == CloudBackupStatus.UploadFailed), cancellationToken);
        var connected = await _cloudBackupService.IsConnectedAsync(cancellationToken);

        var health = latest?.Status == BackupStatus.Failed
            ? "Failed"
            : lastSuccess is null || DateTime.UtcNow - lastSuccess.Value > TimeSpan.FromDays(2)
                ? "Warning"
                : latest?.CloudStatus is CloudBackupStatus.PendingUpload or CloudBackupStatus.UploadFailed
                    ? "Pending"
                    : connected || latest?.CloudStatus == CloudBackupStatus.NotEnabled
                        ? "Healthy"
                        : "CloudAttention";
        return new BackupStatusSummary(
            lastSuccess, connected, pending, latest?.Status, latest?.CloudStatus, health, latest?.ErrorSummary);
    }

    public async Task<BackupSettingsDto> GetSettingsAsync(CancellationToken cancellationToken = default)
    {
        _permissionService.EnsurePermission(PermissionKeys.BackupManageSettings);
        var current = _pathProvider.GetBackupDirectory();
        var defaultDirectory = _pathProvider.DefaultBackupDirectory;
        return new BackupSettingsDto(
            current,
            defaultDirectory,
            string.Equals(current, defaultDirectory, StringComparison.OrdinalIgnoreCase),
            await _cloudBackupService.IsConnectedAsync(cancellationToken),
            await IsOnDatabaseDriveAsync(current, cancellationToken));
    }

    public async Task SetBackupDirectoryAsync(string? directory, CancellationToken cancellationToken = default)
    {
        _permissionService.EnsurePermission(PermissionKeys.BackupManageSettings);
        var oldDirectory = _pathProvider.GetBackupDirectory();
        _pathProvider.SetBackupDirectory(directory);
        var newDirectory = _pathProvider.GetBackupDirectory();
        await TryAuditAsync(AuditActionKeys.BackupSettingsChanged, null, new
        {
            Operation = "BackupSettingsChanged",
            Result = "Succeeded",
            OldDirectory = oldDirectory,
            NewDirectory = newDirectory
        }, cancellationToken);
        _statusNotifier.NotifyChanged();
    }

    public async Task DeleteLocalBackupAsync(int backupRecordId, CancellationToken cancellationToken = default)
    {
        _permissionService.EnsurePermission(PermissionKeys.BackupDelete);
        var record = await _dbContext.BackupRecords.SingleOrDefaultAsync(
            item => item.Id == backupRecordId, cancellationToken)
            ?? throw new InvalidOperationException("سجل النسخة الاحتياطية غير موجود.");
        if (record.Status is BackupStatus.Creating or BackupStatus.Validating or BackupStatus.Restoring ||
            record.CloudStatus == CloudBackupStatus.Uploading)
        {
            throw new InvalidOperationException("لا يمكن حذف نسخة احتياطية قيد الاستخدام.");
        }
        if (File.Exists(record.LocalPath)) File.Delete(record.LocalPath);
        await TryAuditAsync(AuditActionKeys.BackupManualDeleted, record.Id, new
        {
            Operation = "BackupManualDeletion",
            Result = "Succeeded",
            record.BackupType,
            BackupRecordId = record.Id,
            Destination = "Local"
        }, cancellationToken);
        _statusNotifier.NotifyChanged();
    }

    public Task EnforceRetentionPolicyAsync(int maxBackups = 5, CancellationToken cancellationToken = default)
        => _retentionService.EnforceAsync(maxBackups, cancellationToken);

    public Task CleanupStaleTemporaryFilesAsync(CancellationToken cancellationToken = default)
    {
        var roots = new[]
        {
            Path.Combine(_pathProvider.GetBackupDirectory(), ".working"),
            _applicationPaths.RestoreWorkDirectory
        };
        foreach (var root in roots)
        {
            if (!Directory.Exists(root)) continue;
            foreach (var directory in Directory.EnumerateDirectories(root))
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    if (File.Exists(Path.Combine(directory, "recovery-required.json"))) continue;
                    if (Directory.GetLastWriteTimeUtc(directory) < DateTime.UtcNow.AddHours(-4))
                        Directory.Delete(directory, recursive: true);
                }
                catch (Exception exception)
                {
                    _logger.LogDebug(exception, "Unable to remove stale backup work directory {Directory}", directory);
                }
            }
        }
        return Task.CompletedTask;
    }

    internal async Task<BackupMetadata> CreateBackupCoreAsync(
        BackupRequest request,
        bool persistHistoryWhenAvailable,
        CancellationToken cancellationToken,
        bool runRetention)
    {
        var backupDirectory = string.IsNullOrWhiteSpace(request.DestinationDirectory)
            ? _pathProvider.GetBackupDirectory()
            : Path.GetFullPath(request.DestinationDirectory);
        var createdAt = DateTime.UtcNow;
        var fileName = $"Backup_{createdAt.ToLocalTime():yyyy-MM-dd_HH-mm-ss_fff}.berpbackup";
        var finalPath = Path.Combine(backupDirectory, fileName);
        var applicationVersion = GetApplicationVersion();
        var databaseVersion = await GetDatabaseVersionAsync(cancellationToken);
        var createdBy = string.IsNullOrWhiteSpace(request.CreatedByUser)
            ? _userSessionService.Username
            : request.CreatedByUser.Trim();
        if (string.IsNullOrWhiteSpace(createdBy)) createdBy = "system";
        BackupRecord? record = null;
        string? workingDirectory = null;
        var finalFilePublished = false;

        try
        {
            if (persistHistoryWhenAvailable && await HistoryTableExistsAsync(cancellationToken))
            {
                record = new BackupRecord
                {
                    FileName = fileName,
                    LocalPath = finalPath,
                    BackupCreatedAtUtc = createdAt,
                    WorkingDayDate = request.WorkingDayDate,
                    WorkingDayId = request.WorkingDayId,
                    SourceOperationId = request.SourceOperationId,
                    BackupType = request.BackupType,
                    Status = BackupStatus.Creating,
                    CloudStatus = CloudBackupStatus.NotEnabled,
                    ApplicationVersion = applicationVersion,
                    DatabaseVersion = databaseVersion,
                    DeviceName = Environment.MachineName,
                    CreatedByUser = createdBy
                };
                _dbContext.BackupRecords.Add(record);
                await _dbContext.SaveChangesAsync(cancellationToken);
                await TryAuditAsync(
                    request.BackupType == BackupType.Automatic ? AuditActionKeys.BackupAutomaticStarted : AuditActionKeys.BackupManualStarted,
                    record.Id,
                    new
                    {
                        Operation = "BackupCreate",
                        Result = "Started",
                        request.BackupType,
                        BackupRecordId = record.Id,
                        Destination = "Local",
                        request.WorkingDayDate
                    },
                    cancellationToken);
            }

            Directory.CreateDirectory(backupDirectory);
            workingDirectory = _pathProvider.CreateWorkingDirectory(backupDirectory);
            var databaseBackupPath = Path.Combine(workingDirectory, "database.bak");
            var plainArchivePath = Path.Combine(workingDirectory, "archive.zip");
            var partialArchivePath = Path.Combine(workingDirectory, fileName + ".partial");
            await CreateSqlServerBackupAsync(databaseBackupPath, cancellationToken);
            var archiveMetadata = new BackupArchiveMetadata(
                BackupFormatVersion,
                applicationVersion,
                databaseVersion,
                createdAt,
                request.WorkingDayDate,
                request.BackupType,
                Environment.MachineName,
                createdBy);
            await CreateArchiveAsync(plainArchivePath, databaseBackupPath, archiveMetadata, cancellationToken);
            await _encryptionService.EncryptAsync(
                plainArchivePath,
                partialArchivePath,
                request.EncryptionPassword,
                cancellationToken);
            TryDeleteFile(plainArchivePath);

            if (record is not null)
            {
                record.Status = BackupStatus.Validating;
                await _dbContext.SaveChangesAsync(cancellationToken);
            }
            var validation = await _validationService.ValidateAsync(
                partialArchivePath, request.EncryptionPassword, cancellationToken);
            if (!validation.IsValid)
                throw new InvalidDataException(validation.ErrorSummary ?? "فشل التحقق من النسخة الاحتياطية.");

            File.Move(partialArchivePath, finalPath, overwrite: false);
            finalFilePublished = true;
            if (!await _validationService.CanOpenArchiveAsync(
                    finalPath, request.EncryptionPassword, cancellationToken))
                throw new InvalidDataException("تعذر إعادة فتح النسخة الاحتياطية بعد حفظها.");

            var fileSize = new FileInfo(finalPath).Length;
            var cloudConnected = await _cloudBackupService.IsConnectedAsync(cancellationToken);
            if (record is not null)
            {
                record.FileSizeBytes = fileSize;
                record.Status = BackupStatus.Success;
                record.CloudStatus = cloudConnected
                    ? CloudBackupStatus.PendingUpload
                    : CloudBackupStatus.NotEnabled;
                record.ErrorSummary = null;
                await _dbContext.SaveChangesAsync(cancellationToken);
            }

            await TryAuditAsync(
                request.BackupType == BackupType.Automatic ? AuditActionKeys.BackupAutomaticSucceeded : AuditActionKeys.BackupManualSucceeded,
                record?.Id,
                new
                {
                    Operation = "BackupCreate",
                    Result = "Succeeded",
                    request.BackupType,
                    BackupRecordId = record?.Id,
                    Destination = "Local",
                    request.WorkingDayDate
                },
                cancellationToken);

            try
            {
                if (record is not null && runRetention) await _retentionService.EnforceAsync(5, cancellationToken);
            }
            catch (Exception exception)
            {
                _logger.LogWarning(exception, "Backup {BackupRecordId} succeeded but retention failed", record?.Id);
            }
            _statusNotifier.NotifyChanged();
            return record is null
                ? new BackupMetadata
                {
                    FilePath = finalPath,
                    FileName = fileName,
                    CreatedAt = createdAt,
                    WorkingDayDate = request.WorkingDayDate,
                    BackupType = request.BackupType,
                    SizeBytes = fileSize,
                    Status = BackupStatus.Success,
                    CloudStatus = cloudConnected ? CloudBackupStatus.PendingUpload : CloudBackupStatus.NotEnabled,
                    ApplicationVersion = applicationVersion,
                    DatabaseVersion = databaseVersion,
                    DeviceName = Environment.MachineName,
                    CreatedByUser = createdBy,
                    LocalFileAvailable = true
                }
                : Map(record);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            if (finalFilePublished) TryDeleteFile(finalPath);
            if (record is not null) await MarkFailedAsync(record, "تم إلغاء العملية.", CancellationToken.None);
            throw;
        }
        catch (Exception exception)
        {
            if (finalFilePublished) TryDeleteFile(finalPath);
            var summary = BackupError.Summarize(exception);
            _logger.LogError(exception, "Backup creation failed for type {BackupType}", request.BackupType);
            if (record is not null) await MarkFailedAsync(record, summary, CancellationToken.None);
            await TryAuditAsync(AuditActionKeys.BackupFailed, record?.Id, new
            {
                Operation = "BackupCreate",
                Result = "Failed",
                request.BackupType,
                BackupRecordId = record?.Id,
                request.WorkingDayDate,
                ErrorSummary = summary
            }, CancellationToken.None);
            _statusNotifier.NotifyChanged();
            throw new InvalidOperationException(summary, exception);
        }
        finally
        {
            if (workingDirectory is not null)
                BackupValidationService.TryDeleteDirectory(workingDirectory);
        }
    }

    private static void TryDeleteFile(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { }
    }

    private async Task CreateSqlServerBackupAsync(string destinationPath, CancellationToken cancellationToken)
    {
        var active = _dbContext.Database.GetDbConnection();
        var databaseName = active.Database;
        if (string.IsNullOrWhiteSpace(databaseName))
            databaseName = new SqlConnectionStringBuilder(active.ConnectionString).InitialCatalog;
        if (string.IsNullOrWhiteSpace(databaseName))
            throw new InvalidOperationException("Database name is unavailable.");

        var builder = new SqlConnectionStringBuilder(active.ConnectionString) { InitialCatalog = "master" };
        await using var connection = new SqlConnection(builder.ConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = $"BACKUP DATABASE {QuoteIdentifier(databaseName)} TO DISK = @path WITH COPY_ONLY, INIT, CHECKSUM;";
        command.CommandTimeout = 0;
        command.Parameters.AddWithValue("@path", destinationPath);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private async Task CreateArchiveAsync(
        string archivePath,
        string databaseBackupPath,
        BackupArchiveMetadata metadata,
        CancellationToken cancellationToken)
    {
        await using var archiveStream = new FileStream(
            archivePath, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None,
            64 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
        using var archive = new ZipArchive(archiveStream, ZipArchiveMode.Create, leaveOpen: true);

        var metadataEntry = archive.CreateEntry(BackupValidationService.MetadataEntryName, CompressionLevel.Fastest);
        await using (var metadataStream = metadataEntry.Open())
        {
            await JsonSerializer.SerializeAsync(metadataStream, metadata, MetadataJsonOptions, cancellationToken);
        }

        await AddFileAsync(
            archive,
            databaseBackupPath,
            BackupValidationService.DatabaseEntryName,
            CompressionLevel.NoCompression,
            cancellationToken);

        foreach (var (directory, archiveRoot) in GetApplicationDataDirectories())
        {
            archive.CreateEntry($"content/{archiveRoot}/");
            if (!Directory.Exists(directory)) continue;
            foreach (var path in Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var relative = Path.GetRelativePath(directory, path).Replace('\\', '/');
                await AddFileAsync(archive, path, $"content/{archiveRoot}/{relative}", CompressionLevel.Fastest, cancellationToken);
            }
        }

        var gridSettings = _applicationPaths.GridSettingsFile;
        if (File.Exists(gridSettings))
            await AddFileAsync(archive, gridSettings, "settings/grid_settings.json", CompressionLevel.Fastest, cancellationToken);

        await archiveStream.FlushAsync(cancellationToken);
    }

    private static async Task AddFileAsync(
        ZipArchive archive,
        string sourcePath,
        string entryName,
        CompressionLevel compressionLevel,
        CancellationToken cancellationToken)
    {
        var entry = archive.CreateEntry(entryName, compressionLevel);
        await using var source = new FileStream(
            sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read,
            64 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
        await using var destination = entry.Open();
        await source.CopyToAsync(destination, cancellationToken);
    }

    private IEnumerable<(string Directory, string ArchiveRoot)> GetApplicationDataDirectories()
    {
        yield return (_applicationPaths.AttachmentsDirectory, "Attachments");
        yield return (_applicationPaths.DocumentsDirectory, "Documents");
        yield return (_applicationPaths.TemplatesDirectory, "Templates");
        yield return (_applicationPaths.LogosDirectory, "Logos");
    }

    private async Task<string> GetDatabaseVersionAsync(CancellationToken cancellationToken)
    {
        var migrations = await _dbContext.Database.GetAppliedMigrationsAsync(cancellationToken);
        return migrations.LastOrDefault() ?? "Initial";
    }

    private static string GetApplicationVersion()
        => Assembly.GetEntryAssembly()?.GetName().Version?.ToString()
           ?? Assembly.GetExecutingAssembly().GetName().Version?.ToString()
           ?? "0.0.0.0";

    private async Task<bool> HistoryTableExistsAsync(CancellationToken cancellationToken)
    {
        var connection = _dbContext.Database.GetDbConnection();
        var shouldClose = connection.State != System.Data.ConnectionState.Open;
        try
        {
            if (shouldClose) await connection.OpenAsync(cancellationToken);
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT CASE WHEN OBJECT_ID(N'[dbo].[BackupRecords]', N'U') IS NULL THEN 0 ELSE 1 END";
            return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken)) == 1;
        }
        finally
        {
            if (shouldClose) await connection.CloseAsync();
        }
    }

    private async Task<bool> IsOnDatabaseDriveAsync(string backupDirectory, CancellationToken cancellationToken)
    {
        try
        {
            var databasePath = await _dbContext.Database.SqlQueryRaw<string>(
                    "SELECT TOP (1) physical_name AS [Value] FROM sys.database_files WHERE type = 0")
                .FirstOrDefaultAsync(cancellationToken);
            var backupRoot = Path.GetPathRoot(Path.GetFullPath(backupDirectory));
            var databaseRoot = string.IsNullOrWhiteSpace(databasePath) ? null : Path.GetPathRoot(databasePath);
            return backupRoot is not null && databaseRoot is not null &&
                string.Equals(backupRoot, databaseRoot, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    private async Task MarkFailedAsync(BackupRecord record, string summary, CancellationToken cancellationToken)
    {
        try
        {
            record.Status = BackupStatus.Failed;
            record.ErrorSummary = summary;
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Unable to persist failure for backup record {BackupRecordId}", record.Id);
        }
    }

    private async Task TryAuditAsync(string action, int? recordId, object details, CancellationToken cancellationToken)
    {
        try
        {
            await _auditService.LogAsync(
                action,
                nameof(BackupRecord),
                recordId,
                null,
                JsonSerializer.Serialize(details),
                cancellationToken);
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Unable to write audit action {AuditAction}", action);
        }
    }

    private static string QuoteIdentifier(string value) => $"[{value.Replace("]", "]]", StringComparison.Ordinal)}]";

    private static BackupMetadata Map(BackupRecord record) => new()
    {
        Id = record.Id,
        FilePath = record.LocalPath,
        FileName = record.FileName,
        CreatedAt = record.BackupCreatedAtUtc,
        WorkingDayDate = record.WorkingDayDate,
        BackupType = record.BackupType,
        SizeBytes = record.FileSizeBytes,
        Status = record.Status,
        CloudStatus = record.CloudStatus,
        GoogleDriveFileId = record.GoogleDriveFileId,
        ApplicationVersion = record.ApplicationVersion,
        DatabaseVersion = record.DatabaseVersion,
        DeviceName = record.DeviceName,
        CreatedByUser = record.CreatedByUser,
        ErrorSummary = record.ErrorSummary,
        UploadRetryCount = record.UploadRetryCount,
        LocalFileAvailable = File.Exists(record.LocalPath)
    };
}
