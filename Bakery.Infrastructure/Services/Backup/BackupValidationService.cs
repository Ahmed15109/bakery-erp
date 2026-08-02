using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Serialization;
using Bakery.Application.DTOs;
using Bakery.Application.Interfaces;
using Bakery.Infrastructure.Data;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Bakery.Infrastructure.Services.Backup;

public sealed class BackupValidationService : IBackupValidationService
{
    internal const string MetadataEntryName = "metadata.json";
    internal const string DatabaseEntryName = "database/database.bak";
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly BakeryDbContext _dbContext;
    private readonly BackupEncryptionService _encryptionService;
    private readonly IApplicationPathService _applicationPaths;
    private readonly ILogger<BackupValidationService> _logger;

    public BackupValidationService(
        BakeryDbContext dbContext,
        BackupEncryptionService encryptionService,
        IApplicationPathService applicationPaths,
        ILogger<BackupValidationService> logger)
    {
        _dbContext = dbContext;
        _encryptionService = encryptionService;
        _applicationPaths = applicationPaths;
        _logger = logger;
    }

    public async Task<BackupValidationResult> ValidateAsync(
        string archivePath,
        CancellationToken cancellationToken = default)
        => await ValidateAsync(archivePath, null, cancellationToken);

    public async Task<BackupValidationResult> ValidateAsync(
        string archivePath,
        string? password,
        CancellationToken cancellationToken = default)
    {
        string? validationDirectory = null;
        try
        {
            if (!File.Exists(archivePath))
                return Invalid("ملف النسخة الاحتياطية غير موجود.");
            if (new FileInfo(archivePath).Length == 0)
                return Invalid("ملف النسخة الاحتياطية فارغ.");

            using var prepared = await _encryptionService.PrepareReadAsync(
                archivePath, password, cancellationToken);
            await using var stream = new FileStream(
                prepared.ArchivePath, FileMode.Open, FileAccess.Read, FileShare.Read,
                64 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
            using var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: false);
            var metadataEntry = archive.GetEntry(MetadataEntryName);
            var databaseEntry = archive.GetEntry(DatabaseEntryName);
            if (metadataEntry is null || metadataEntry.Length == 0)
                return Invalid("بيانات تعريف النسخة الاحتياطية مفقودة.");
            if (databaseEntry is null || databaseEntry.Length == 0)
                return Invalid("ملف قاعدة البيانات مفقود من النسخة الاحتياطية.");

            BackupArchiveMetadata? metadata;
            await using (var metadataStream = metadataEntry.Open())
            {
                metadata = await JsonSerializer.DeserializeAsync<BackupArchiveMetadata>(
                    metadataStream, JsonOptions, cancellationToken);
            }
            if (metadata is null || string.IsNullOrWhiteSpace(metadata.BackupVersion) ||
                string.IsNullOrWhiteSpace(metadata.DatabaseSchemaVersion))
            {
                return Invalid("بيانات تعريف النسخة الاحتياطية غير صالحة.");
            }

            validationDirectory = Path.Combine(
                _applicationPaths.RestoreWorkDirectory,
                "validation-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(validationDirectory);
            var databasePath = Path.Combine(validationDirectory, "database.bak");
            await using (var source = databaseEntry.Open())
            await using (var destination = new FileStream(
                databasePath, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                64 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan))
            {
                await source.CopyToAsync(destination, cancellationToken);
                await destination.FlushAsync(cancellationToken);
            }

            await VerifySqlServerBackupAsync(databasePath, cancellationToken);
            return new BackupValidationResult(true, metadata);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Backup validation failed for {ArchivePath}", archivePath);
            return Invalid(BackupError.Summarize(exception));
        }
        finally
        {
            if (validationDirectory is not null)
            {
                TryDeleteDirectory(validationDirectory);
            }
        }
    }

    public Task<bool> CanOpenArchiveAsync(
        string archivePath,
        CancellationToken cancellationToken = default)
        => CanOpenArchiveAsync(archivePath, null, cancellationToken);

    public async Task<bool> CanOpenArchiveAsync(
        string archivePath,
        string? password,
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(archivePath) || new FileInfo(archivePath).Length == 0) return false;
        try
        {
            using var prepared = await _encryptionService.PrepareReadAsync(
                archivePath, password, cancellationToken);
            await using var stream = new FileStream(
                prepared.ArchivePath, FileMode.Open, FileAccess.Read, FileShare.Read,
                32 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
            using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
            var metadata = archive.GetEntry(MetadataEntryName);
            var database = archive.GetEntry(DatabaseEntryName);
            if (metadata is null || metadata.Length == 0 || database is null || database.Length == 0) return false;
            var oneByte = new byte[1];
            await using var entryStream = database.Open();
            return await entryStream.ReadAsync(oneByte, cancellationToken) == 1;
        }
        catch
        {
            return false;
        }
    }

    private async Task VerifySqlServerBackupAsync(string databaseBackupPath, CancellationToken cancellationToken)
    {
        var activeConnectionString = _dbContext.Database.GetConnectionString()
            ?? throw new InvalidOperationException("Database connection is unavailable.");
        var builder = new SqlConnectionStringBuilder(activeConnectionString) { InitialCatalog = "master" };
        await using var connection = new SqlConnection(builder.ConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "RESTORE VERIFYONLY FROM DISK = @path WITH CHECKSUM;";
        command.CommandTimeout = 0;
        command.Parameters.AddWithValue("@path", databaseBackupPath);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static BackupValidationResult Invalid(string summary) => new(false, null, summary);

    internal static void TryDeleteDirectory(string directory)
    {
        try
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
        catch
        {
            // A startup cleanup pass handles stale work directories.
        }
    }
}
