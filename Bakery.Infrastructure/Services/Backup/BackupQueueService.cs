using System.Text.Json;
using System.Threading.Channels;
using Bakery.Application.DTOs;
using Bakery.Application.Interfaces;
using Bakery.Domain.Entities;
using Bakery.Domain.Enums;
using Bakery.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Bakery.Infrastructure.Services.Backup;

public sealed class BackupQueueService : BackgroundService, IBackupQueueService
{
    private readonly Channel<BackupQueueItem> _queue = Channel.CreateBounded<BackupQueueItem>(
        new BoundedChannelOptions(32)
        {
            SingleReader = true,
            SingleWriter = false,
            FullMode = BoundedChannelFullMode.Wait
        });
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IConnectivityService _connectivityService;
    private readonly IBackupStatusNotifier _statusNotifier;
    private readonly ILogger<BackupQueueService> _logger;
    private readonly SemaphoreSlim _uploadGate = new(1, 1);
    private readonly TaskCompletionSource _applicationReady = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly CancellationTokenSource _cloudWorkCancellation = new();
    private int _pendingAutomaticBackups;
    private volatile bool _shutdownRequested;

    public BackupQueueService(
        IServiceScopeFactory scopeFactory,
        IConnectivityService connectivityService,
        IBackupStatusNotifier statusNotifier,
        ILogger<BackupQueueService> logger)
    {
        _scopeFactory = scopeFactory;
        _connectivityService = connectivityService;
        _statusNotifier = statusNotifier;
        _logger = logger;
        _connectivityService.NetworkAvailable += OnNetworkAvailable;
    }

    internal void MarkApplicationReady() => _applicationReady.TrySetResult();

    public async ValueTask QueueAutomaticBackupAsync(
        DateOnly workingDayDate,
        int workingDayId,
        Guid? sourceOperationId,
        string createdByUser,
        CancellationToken cancellationToken = default)
    {
        if (_shutdownRequested)
            throw new InvalidOperationException("The application is shutting down and cannot accept another automatic backup.");

        Interlocked.Increment(ref _pendingAutomaticBackups);
        try
        {
            if (_shutdownRequested)
                throw new InvalidOperationException("The application is shutting down and cannot accept another automatic backup.");
            await _queue.Writer.WriteAsync(
                new AutomaticBackupQueueItem(
                    workingDayDate, workingDayId, sourceOperationId, createdByUser),
                cancellationToken);
        }
        catch
        {
            Interlocked.Decrement(ref _pendingAutomaticBackups);
            throw;
        }
    }

    public async ValueTask QueueCloudRetryAsync(
        int backupRecordId,
        CancellationToken cancellationToken = default)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<BakeryDbContext>();
        var permissions = scope.ServiceProvider.GetRequiredService<IPermissionService>();
        permissions.EnsurePermission(Bakery.Application.Security.PermissionKeys.BackupConnectGoogleDrive);
        var record = await db.BackupRecords.SingleOrDefaultAsync(item => item.Id == backupRecordId, cancellationToken)
            ?? throw new InvalidOperationException("سجل النسخة الاحتياطية غير موجود.");
        if (record.Status != BackupStatus.Success || !File.Exists(record.LocalPath))
            throw new InvalidOperationException("النسخة المحلية غير متوفرة لإعادة الرفع.");
        if (!string.IsNullOrWhiteSpace(record.GoogleDriveFileId)) return;
        record.CloudStatus = CloudBackupStatus.PendingUpload;
        record.LastUploadAttemptAtUtc = null;
        record.ErrorSummary = null;
        await db.SaveChangesAsync(cancellationToken);
        await TryAuditAsync(scope.ServiceProvider, AuditActionKeys.BackupManualUploadRetried, record, null, cancellationToken);
        await _queue.Writer.WriteAsync(new ProcessUploadsQueueItem(), cancellationToken);
        _statusNotifier.NotifyChanged();
    }

    public async Task ProcessPendingUploadsAsync(CancellationToken cancellationToken = default)
    {
        if (!_connectivityService.IsNetworkAvailable ||
            !await _uploadGate.WaitAsync(0, cancellationToken)) return;
        try
        {
            while (!cancellationToken.IsCancellationRequested && _connectivityService.IsNetworkAvailable)
            {
                await using var scope = _scopeFactory.CreateAsyncScope();
                var db = scope.ServiceProvider.GetRequiredService<BakeryDbContext>();
                var cloud = scope.ServiceProvider.GetRequiredService<ICloudBackupService>();
                if (!await cloud.IsConnectedAsync(cancellationToken)) return;
                var now = DateTime.UtcNow;
                var candidates = await db.BackupRecords
                    .Where(item => item.Status == BackupStatus.Success &&
                        (item.CloudStatus == CloudBackupStatus.PendingUpload ||
                         item.CloudStatus == CloudBackupStatus.UploadFailed) &&
                        item.GoogleDriveFileId == null)
                    .OrderBy(item => item.BackupCreatedAtUtc)
                    .Take(25)
                    .ToListAsync(cancellationToken);
                var record = candidates.FirstOrDefault(item => IsRetryDue(item, now));
                if (record is null) return;
                if (!File.Exists(record.LocalPath))
                {
                    record.CloudStatus = CloudBackupStatus.UploadFailed;
                    record.ErrorSummary = "ملف النسخة المحلية لم يعد موجوداً، لذلك تعذر رفعه.";
                    record.UploadRetryCount++;
                    record.LastUploadAttemptAtUtc = now;
                    await db.SaveChangesAsync(cancellationToken);
                    _statusNotifier.NotifyChanged();
                    continue;
                }

                record.CloudStatus = CloudBackupStatus.Uploading;
                record.LastUploadAttemptAtUtc = now;
                await db.SaveChangesAsync(cancellationToken);
                _statusNotifier.NotifyChanged();
                try
                {
                    var fileId = await cloud.UploadAsync(record.LocalPath, record.FileName, cancellationToken);
                    record.GoogleDriveFileId = fileId;
                    record.CloudStatus = CloudBackupStatus.Uploaded;
                    record.ErrorSummary = null;
                    await db.SaveChangesAsync(cancellationToken);
                    await TryAuditAsync(scope.ServiceProvider, AuditActionKeys.BackupUploadSucceeded, record, null, cancellationToken);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    record.CloudStatus = CloudBackupStatus.PendingUpload;
                    await db.SaveChangesAsync(CancellationToken.None);
                    throw;
                }
                catch (Exception exception)
                {
                    var summary = BackupError.Summarize(exception);
                    record.CloudStatus = _connectivityService.IsNetworkAvailable
                        ? CloudBackupStatus.UploadFailed
                        : CloudBackupStatus.PendingUpload;
                    record.UploadRetryCount++;
                    record.ErrorSummary = summary;
                    await db.SaveChangesAsync(cancellationToken);
                    _logger.LogWarning(exception, "Google Drive upload failed for backup record {BackupRecordId}", record.Id);
                    await TryAuditAsync(scope.ServiceProvider, AuditActionKeys.BackupUploadFailed, record, summary, cancellationToken);
                    return;
                }
                finally
                {
                    _statusNotifier.NotifyChanged();
                }
            }
        }
        finally
        {
            _uploadGate.Release();
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await _applicationReady.Task.WaitAsync(stoppingToken);
        await _queue.Writer.WriteAsync(new ProcessUploadsQueueItem(), stoppingToken);
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(30));
        using var cloudWorkTokenSource = CancellationTokenSource.CreateLinkedTokenSource(
            stoppingToken,
            _cloudWorkCancellation.Token);
        var timerTask = RunTimerAsync(timer, stoppingToken);
        try
        {
            await foreach (var item in _queue.Reader.ReadAllAsync(stoppingToken))
            {
                try
                {
                    switch (item)
                    {
                        case AutomaticBackupQueueItem automatic:
                            // Once a committed close-day backup starts, allow the local
                            // archive to finish even while the host is shutting down.
                            try
                            {
                                await CreateAutomaticBackupAsync(automatic, CancellationToken.None);
                            }
                            finally
                            {
                                Interlocked.Decrement(ref _pendingAutomaticBackups);
                            }
                            if (!_shutdownRequested && !stoppingToken.IsCancellationRequested)
                                await ProcessPendingUploadsAsync(cloudWorkTokenSource.Token);
                            break;
                        case ProcessUploadsQueueItem:
                            if (!_shutdownRequested)
                                await ProcessPendingUploadsAsync(cloudWorkTokenSource.Token);
                            break;
                    }
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (OperationCanceledException) when (_shutdownRequested)
                {
                    // Cloud work is intentionally cancelled during shutdown. Local
                    // automatic backups remain uncancelled and continue to drain.
                }
                catch (Exception exception)
                {
                    _logger.LogError(exception, "Background backup queue item failed");
                }
            }
        }
        finally
        {
            try { await timerTask; } catch (OperationCanceledException) { }
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        _shutdownRequested = true;
        _applicationReady.TrySetResult();
        _cloudWorkCancellation.Cancel();

        // Closing a working day is already committed before it reaches this queue.
        // Do not let the host's normal shutdown timeout abandon its local backup.
        while (Volatile.Read(ref _pendingAutomaticBackups) > 0)
            await Task.Delay(50, CancellationToken.None);

        await base.StopAsync(CancellationToken.None);
    }

    public override void Dispose()
    {
        _connectivityService.NetworkAvailable -= OnNetworkAvailable;
        _cloudWorkCancellation.Dispose();
        _uploadGate.Dispose();
        base.Dispose();
    }

    private async Task CreateAutomaticBackupAsync(
        AutomaticBackupQueueItem item,
        CancellationToken cancellationToken)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<IBackupService>();
        await service.CreateBackupAsync(
            new BackupRequest(
                BackupType.Automatic,
                item.WorkingDayDate,
                item.WorkingDayId,
                item.SourceOperationId,
                CreatedByUser: item.CreatedByUser,
                EnforceUserPermission: false),
            cancellationToken);
    }

    private async Task RunTimerAsync(PeriodicTimer timer, CancellationToken cancellationToken)
    {
        while (await timer.WaitForNextTickAsync(cancellationToken))
            await _queue.Writer.WriteAsync(new ProcessUploadsQueueItem(), cancellationToken);
    }

    private void OnNetworkAvailable(object? sender, EventArgs e)
    {
        if (!_shutdownRequested)
            _queue.Writer.TryWrite(new ProcessUploadsQueueItem());
    }

    private static bool IsRetryDue(BackupRecord record, DateTime now)
    {
        if (record.LastUploadAttemptAtUtc is null) return true;
        var delayMinutes = Math.Min(360, Math.Pow(2, Math.Min(record.UploadRetryCount, 8)));
        return record.LastUploadAttemptAtUtc.Value.AddMinutes(delayMinutes) <= now;
    }

    private static async Task TryAuditAsync(
        IServiceProvider services,
        string action,
        BackupRecord record,
        string? errorSummary,
        CancellationToken cancellationToken)
    {
        try
        {
            await services.GetRequiredService<IAuditService>().LogAsync(
                action,
                nameof(BackupRecord),
                record.Id,
                null,
                JsonSerializer.Serialize(new
                {
                    Operation = action,
                    Result = errorSummary is null ? "Succeeded" : "Failed",
                    record.BackupType,
                    BackupRecordId = record.Id,
                    Destination = "GoogleDrive",
                    record.WorkingDayDate,
                    ErrorSummary = errorSummary
                }),
                cancellationToken);
        }
        catch
        {
            // The upload result is authoritative even if the audit writer is unavailable.
        }
    }

    private abstract record BackupQueueItem;
    private sealed record AutomaticBackupQueueItem(
        DateOnly WorkingDayDate,
        int WorkingDayId,
        Guid? SourceOperationId,
        string CreatedByUser) : BackupQueueItem;
    private sealed record ProcessUploadsQueueItem : BackupQueueItem;
}

public sealed class BackupStartupService : IBackupStartupService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly BackupQueueService _queueService;
    private readonly ILogger<BackupStartupService> _logger;

    public BackupStartupService(
        IServiceScopeFactory scopeFactory,
        BackupQueueService queueService,
        ILogger<BackupStartupService> logger)
    {
        _scopeFactory = scopeFactory;
        _queueService = queueService;
        _logger = logger;
    }

    public async Task RunLightweightStartupRecoveryAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var backupService = scope.ServiceProvider.GetRequiredService<IBackupService>();
            await backupService.CleanupStaleTemporaryFilesAsync(cancellationToken);
            var db = scope.ServiceProvider.GetRequiredService<BakeryDbContext>();
            var interrupted = await db.BackupRecords
                .Where(item => item.Status == BackupStatus.Creating ||
                    item.Status == BackupStatus.Validating ||
                    item.Status == BackupStatus.Restoring ||
                    item.CloudStatus == CloudBackupStatus.Uploading)
                .ToListAsync(cancellationToken);
            foreach (var record in interrupted)
            {
                if (record.Status is BackupStatus.Creating or BackupStatus.Validating or BackupStatus.Restoring)
                {
                    record.Status = BackupStatus.Failed;
                    record.ErrorSummary = "توقفت العملية قبل اكتمالها بسبب إغلاق سابق للتطبيق.";
                }
                if (record.CloudStatus == CloudBackupStatus.Uploading)
                    record.CloudStatus = CloudBackupStatus.PendingUpload;
            }
            if (interrupted.Count > 0) await db.SaveChangesAsync(cancellationToken);
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Lightweight backup startup recovery failed; application startup will continue");
        }
        finally
        {
            _queueService.MarkApplicationReady();
        }
    }
}
