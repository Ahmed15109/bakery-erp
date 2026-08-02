using System.Text.Json;
using Bakery.Application.Interfaces;
using Bakery.Domain.Entities;
using Bakery.Domain.Enums;
using Bakery.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Bakery.Infrastructure.Services.Backup;

public sealed class BackupRetentionService : IBackupRetentionService
{
    private readonly BakeryDbContext _dbContext;
    private readonly IAuditService _auditService;
    private readonly IBackupValidationService _validationService;
    private readonly ILogger<BackupRetentionService> _logger;

    public BackupRetentionService(
        BakeryDbContext dbContext,
        IAuditService auditService,
        IBackupValidationService validationService,
        ILogger<BackupRetentionService> logger)
    {
        _dbContext = dbContext;
        _auditService = auditService;
        _validationService = validationService;
        _logger = logger;
    }

    public async Task EnforceAsync(int maxSuccessfulBackups = 5, CancellationToken cancellationToken = default)
    {
        if (maxSuccessfulBackups < 1) throw new ArgumentOutOfRangeException(nameof(maxSuccessfulBackups));
        var successful = await _dbContext.BackupRecords
            .AsNoTracking()
            .Where(item => item.Status == BackupStatus.Success)
            .OrderByDescending(item => item.BackupCreatedAtUtc)
            .ThenByDescending(item => item.Id)
            .ToListAsync(cancellationToken);
        if (successful.Count <= maxSuccessfulBackups) return;

        // If history and physical files disagree, preservation is safer than deletion.
        var representedPaths = successful
            .Select(item => Path.GetFullPath(item.LocalPath))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var directory in successful.Select(item => Path.GetDirectoryName(item.LocalPath))
                     .Where(path => !string.IsNullOrWhiteSpace(path)).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (!Directory.Exists(directory)) continue;
            var untracked = Directory.EnumerateFiles(directory!, "Backup_*.*")
                .Where(path => path.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) ||
                               path.EndsWith(".berpbackup", StringComparison.OrdinalIgnoreCase))
                .Select(Path.GetFullPath)
                .Any(path => !representedPaths.Contains(path));
            if (untracked)
            {
                _logger.LogWarning("Retention skipped because backup files and history do not agree in {Directory}", directory);
                return;
            }
        }

        foreach (var record in successful.Skip(maxSuccessfulBackups))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (record.CloudStatus == CloudBackupStatus.Uploading) continue;
            if (!File.Exists(record.LocalPath)) continue;
            if (!await _validationService.CanOpenArchiveAsync(record.LocalPath, cancellationToken))
            {
                _logger.LogWarning("Retention preserved unreadable or unconfirmed file {BackupPath}", record.LocalPath);
                continue;
            }
            try
            {
                File.Delete(record.LocalPath);
                await _auditService.LogAsync(
                    AuditActionKeys.BackupAutomaticDeleted,
                    nameof(BackupRecord),
                    record.Id,
                    null,
                    JsonSerializer.Serialize(new
                    {
                        Operation = "BackupAutomaticDeletion",
                        Result = "Succeeded",
                        record.BackupType,
                        BackupRecordId = record.Id,
                        Destination = "Local"
                    }),
                    cancellationToken);
            }
            catch (Exception exception)
            {
                _logger.LogWarning(exception, "Retention could not delete backup {BackupRecordId} at {BackupPath}", record.Id, record.LocalPath);
            }
        }
    }
}
