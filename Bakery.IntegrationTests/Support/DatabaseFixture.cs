using Bakery.Application.DTOs;
using Bakery.Application.DTOs.Accounting;
using Bakery.Application.DTOs.Inventory;
using Bakery.Application.Interfaces;
using Bakery.Infrastructure.Data;
using Bakery.Infrastructure.Services;
using Bakery.Domain.Entities;
using Bakery.Domain.Enums;
using Bakery.Application.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using FluentValidation;
using System.IO;
using System.Diagnostics;
using Bakery.Infrastructure.Services.Backup;

namespace Bakery.IntegrationTests;

public class DatabaseFixture : IDisposable
{
    public ServiceProvider ServiceProvider { get; private set; }
    private readonly string _connectionString;
    private readonly string _backupTestRoot;
    public TestCloudBackupService CloudBackup { get; } = new();
    public TestConnectivityService Connectivity { get; } = new();
    public RecordingBackupQueueService AutomaticBackupQueue { get; } = new();
    public TestRestoreFailureInjector RestoreFailureInjector { get; } = new();
    public TestBackupControl BackupControl { get; } = new();
    public TestSystemResetFailureInjector SystemResetFailureInjector { get; } = new();
    public string BackupDirectory => Path.Combine(_backupTestRoot, "Backups");

    public DatabaseFixture()
    {
        Environment.SetEnvironmentVariable("BAKERY_BOOTSTRAP_ADMIN_USERNAME", "admin");
        Environment.SetEnvironmentVariable("BAKERY_BOOTSTRAP_ADMIN_PASSWORD", "admin123-test-only");
        var dbName = $"BakeryERP_Test_{Guid.NewGuid():N}";
        _backupTestRoot = Path.Combine(Path.GetTempPath(), "BakeryERP", "Tests", Guid.NewGuid().ToString("N"));
        _connectionString = $"Server=(localdb)\\mssqllocaldb;Database={dbName};Trusted_Connection=True;MultipleActiveResultSets=true";

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:BakeryDatabase"] = _connectionString
            })
            .Build();

        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(config);
        services.AddInfrastructure(config);
        services.AddSingleton<ISystemResetFailureInjector>(SystemResetFailureInjector);
        services.AddSingleton(BackupControl);
        services.AddScoped<IBackupService>(provider => new DelegatingTestBackupService(
            provider.GetRequiredService<BackupService>(),
            provider.GetRequiredService<TestBackupControl>()));
        services.AddSingleton<IApplicationPathService>(new ApplicationPathService(_backupTestRoot));
        services.AddSingleton(new BackupPathProvider(_backupTestRoot));
        services.AddSingleton<IRestoreFailureInjector>(RestoreFailureInjector);
        services.AddSingleton<ICloudBackupService>(CloudBackup);
        services.AddSingleton<IConnectivityService>(Connectivity);
        services.AddSingleton<IBackupQueueService>(AutomaticBackupQueue);

        // Add validators (empty concrete implementations for tests)
        services.AddScoped<IValidator<SaveSaleInvoiceRequest>, NullSaleValidator>();
        services.AddScoped<IValidator<SavePurchaseInvoiceRequest>, NullPurchaseValidator>();
        services.AddScoped<IValidator<OpenWorkingDayRequest>, NullOpenDayValidator>();
        services.AddScoped<IValidator<CloseWorkingDayRequest>, NullCloseDayValidator>();
        services.AddScoped<IValidator<SaveUnitRequest>, NullUnitValidator>();
        services.AddScoped<IValidator<SavePartyRequest>, NullPartyValidator>();
        services.AddScoped<IValidator<InventoryAdjustmentRequest>, NullAdjustmentValidator>();
        services.AddScoped<IValidator<CompleteStockCountRequest>, NullStockCountValidator>();
        services.AddScoped<IValidator<LoginRequest>, NullLoginValidator>();
        services.AddScoped<IValidator<CreateBranchRequest>, NullCreateBranchValidator>();
        services.AddScoped<IValidator<UpdateBranchRequest>, NullUpdateBranchValidator>();

        // Logging nulled out for test runs
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        services.AddLogging();

        ServiceProvider = services.BuildServiceProvider();

        // Run migrations
        using var scope = ServiceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BakeryDbContext>();
        db.Database.Migrate();

        var branch = db.Branches.IgnoreQueryFilters().FirstOrDefault(b => b.Code == "MAIN");
        if (branch is null)
        {
            branch = new Branch { Code = "MAIN", Name = "الفرع الرئيسي", IsActive = true };
            db.Branches.Add(branch);
            db.SaveChanges();
        }

        var branchContext = (IInternalBranchContext)scope.ServiceProvider.GetRequiredService<IBranchContext>();
        branchContext.ConfigureBranch(new BranchDto(branch.Id, branch.Code, branch.Name, branch.IsActive, branch.Notes));

        SeedData(db).GetAwaiter().GetResult();
        var session = scope.ServiceProvider.GetRequiredService<IUserSessionService>();
        session.SignIn(new AuthenticatedUserDto(
            1,
            "test-admin",
            "Test Admin",
            PermissionCatalog.All.Select(permission => permission.Key).ToArray(),
            true));
    }

    private async Task SeedData(BakeryDbContext db)
    {
        if (!await db.Users.AnyAsync())
        {
            db.Users.Add(new User
            {
                Username = "test-admin",
                FullName = "Test Admin",
                PasswordHash = "test",
                IsSuperAdmin = true
            });
            await db.SaveChangesAsync();
        }

        // Only seed if empty
        if (await db.Units.AnyAsync()) return;

        var piece = new Unit { Name = "Piece", Symbol = "pcs" };
        var kg    = new Unit { Name = "Kilogram", Symbol = "kg" };
        db.Units.AddRange(piece, kg);
        db.Safes.AddRange(
            new Safe { Name = "الخزنة الرئيسية", IsActive = true },
            new Safe { Name = "خزنة اليوم", IsActive = true },
            new Safe { Name = "الخزنة خاصة", IsActive = true });
        await db.SaveChangesAsync();

        var pieceId = piece.Id;
        var kgId    = kg.Id;

        db.Items.Add(new Item { Code = "BREAD", Name = "Bread", Type = ItemType.FinishedProduct, BaseUnitId = pieceId, SalePrice = 5m, PurchasePrice = 0m });
        db.Items.Add(new Item { Code = "FLOUR", Name = "Flour", Type = ItemType.RawMaterial,     BaseUnitId = kgId,   SalePrice = 0m, PurchasePrice = 20m });

        db.Parties.Add(new Party { Name = "Customer A", Type = PartyType.Customer });
        db.Parties.Add(new Party { Name = "Supplier A", Type = PartyType.Supplier });

        var empParty = new Party { Name = "Employee A", Type = PartyType.Employee };
        db.Parties.Add(empParty);
        
        var bakerRole = new JobRole { Name = "Baker", WageType = WageType.Production, WageAmount = 2m, ProductionRate = 2m };
        db.JobRoles.Add(bakerRole);
        await db.SaveChangesAsync();

        db.Employees.Add(new Employee
        {
            Code          = "EMP-001",
            Name          = "Employee A",
            PartyId       = empParty.Id,
            JobRoleId     = bakerRole.Id,
            HireDate      = DateOnly.FromDateTime(DateTime.Today),
            WageType      = WageType.Production,
            ProductionRate = 2m,
            WageEffectiveFrom = DateOnly.FromDateTime(DateTime.Today)
        });
        await db.SaveChangesAsync();
    }

    public void Dispose()
    {
        using var scope = ServiceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BakeryDbContext>();
        db.Database.EnsureDeleted();
        ServiceProvider.Dispose();
        try { if (Directory.Exists(_backupTestRoot)) Directory.Delete(_backupTestRoot, recursive: true); } catch { }
    }
}

public sealed class TestSystemResetFailureInjector : ISystemResetFailureInjector
{
    public bool FailBeforeCommit { get; set; }

    public Task BeforeCommitAsync(CancellationToken cancellationToken)
    {
        if (FailBeforeCommit) throw new InvalidOperationException("Injected system-reset failure.");
        return Task.CompletedTask;
    }
}

public sealed class TestBackupControl
{
    public bool SkipSafetySnapshots { get; set; }
    private int _safetySnapshotCount;
    private long _lastSafetySnapshotElapsedTicks;

    public int SafetySnapshotCount => Volatile.Read(ref _safetySnapshotCount);
    public TimeSpan LastSafetySnapshotElapsed => TimeSpan.FromTicks(
        Interlocked.Read(ref _lastSafetySnapshotElapsedTicks));

    public void ResetSafetySnapshotMetrics()
    {
        Interlocked.Exchange(ref _safetySnapshotCount, 0);
        Interlocked.Exchange(ref _lastSafetySnapshotElapsedTicks, 0);
    }

    internal void RecordSafetySnapshotInvocation() =>
        Interlocked.Increment(ref _safetySnapshotCount);

    internal void RecordSafetySnapshotElapsed(TimeSpan elapsed) =>
        Interlocked.Exchange(ref _lastSafetySnapshotElapsedTicks, elapsed.Ticks);
}

public sealed class DelegatingTestBackupService : IBackupService
{
    private readonly BackupService _inner;
    private readonly TestBackupControl _control;

    public DelegatingTestBackupService(BackupService inner, TestBackupControl control)
    {
        _inner = inner;
        _control = control;
    }

    public Task<string> CreateBackupAsync(string? customPath = null, string? password = null, CancellationToken cancellationToken = default)
        => _inner.CreateBackupAsync(customPath, password, cancellationToken);

    public Task<BackupMetadata> CreateBackupAsync(BackupRequest request, CancellationToken cancellationToken = default)
        => _inner.CreateBackupAsync(request, cancellationToken);

    public async Task<string> CreateSafetySnapshotAsync(string operationName, CancellationToken cancellationToken = default)
    {
        _control.RecordSafetySnapshotInvocation();
        var stopwatch = Stopwatch.StartNew();
        try
        {
            return _control.SkipSafetySnapshots
                ? string.Empty
                : await _inner.CreateSafetySnapshotAsync(operationName, cancellationToken);
        }
        finally
        {
            _control.RecordSafetySnapshotElapsed(stopwatch.Elapsed);
        }
    }

    public Task RestoreBackupAsync(string backupFilePath, CancellationToken cancellationToken = default)
        => _inner.RestoreBackupAsync(backupFilePath, cancellationToken);

    public Task<IEnumerable<BackupMetadata>> GetBackupHistoryAsync(CancellationToken cancellationToken = default)
        => _inner.GetBackupHistoryAsync(cancellationToken);

    public Task<BackupStatusSummary> GetStatusSummaryAsync(CancellationToken cancellationToken = default)
        => _inner.GetStatusSummaryAsync(cancellationToken);

    public Task<BackupSettingsDto> GetSettingsAsync(CancellationToken cancellationToken = default)
        => _inner.GetSettingsAsync(cancellationToken);

    public Task SetBackupDirectoryAsync(string? directory, CancellationToken cancellationToken = default)
        => _inner.SetBackupDirectoryAsync(directory, cancellationToken);

    public Task DeleteLocalBackupAsync(int backupRecordId, CancellationToken cancellationToken = default)
        => _inner.DeleteLocalBackupAsync(backupRecordId, cancellationToken);

    public Task EnforceRetentionPolicyAsync(int maxBackups = 5, CancellationToken cancellationToken = default)
        => _inner.EnforceRetentionPolicyAsync(maxBackups, cancellationToken);

    public Task CleanupStaleTemporaryFilesAsync(CancellationToken cancellationToken = default)
        => _inner.CleanupStaleTemporaryFilesAsync(cancellationToken);
}

public sealed class TestCloudBackupService : ICloudBackupService
{
    private int _fileNumber;
    public bool Connected { get; set; }
    public bool FailUploads { get; set; }
    public int UploadCount { get; private set; }

    public Task<bool> IsConnectedAsync(CancellationToken cancellationToken = default) => Task.FromResult(Connected);
    public Task ConnectAsync(CancellationToken cancellationToken = default) { Connected = true; return Task.CompletedTask; }
    public Task DisconnectAsync(CancellationToken cancellationToken = default) { Connected = false; return Task.CompletedTask; }
    public Task<string> UploadAsync(string localArchivePath, string fileName, CancellationToken cancellationToken = default)
    {
        UploadCount++;
        if (FailUploads) throw new IOException("Simulated cloud failure.");
        return Task.FromResult($"test-cloud-{Interlocked.Increment(ref _fileNumber)}");
    }
    public Task DownloadAsync(string cloudFileId, string destinationPath, CancellationToken cancellationToken = default)
        => throw new NotSupportedException();
}

public sealed class TestConnectivityService : IConnectivityService
{
    public bool IsNetworkAvailable { get; set; }
    public event EventHandler? NetworkAvailable;
    public void SetAvailable(bool available)
    {
        IsNetworkAvailable = available;
        if (available) NetworkAvailable?.Invoke(this, EventArgs.Empty);
    }
}

public sealed record RecordedAutomaticBackup(
    DateOnly WorkingDayDate,
    int WorkingDayId,
    Guid? SourceOperationId,
    string CreatedByUser);

public sealed class RecordingBackupQueueService : IBackupQueueService
{
    public RecordedAutomaticBackup? LastAutomaticBackup { get; private set; }
    public Func<RecordedAutomaticBackup, Task>? OnAutomaticQueued { get; set; }

    public async ValueTask QueueAutomaticBackupAsync(
        DateOnly workingDayDate,
        int workingDayId,
        Guid? sourceOperationId,
        string createdByUser,
        CancellationToken cancellationToken = default)
    {
        LastAutomaticBackup = new RecordedAutomaticBackup(
            workingDayDate, workingDayId, sourceOperationId, createdByUser);
        if (OnAutomaticQueued is not null) await OnAutomaticQueued(LastAutomaticBackup);
    }

    public ValueTask QueueCloudRetryAsync(int backupRecordId, CancellationToken cancellationToken = default)
        => ValueTask.CompletedTask;
    public Task ProcessPendingUploadsAsync(CancellationToken cancellationToken = default)
        => Task.CompletedTask;
}

// Empty validators — no rules needed for integration tests
public class NullSaleValidator     : AbstractValidator<SaveSaleInvoiceRequest> { }
public class NullPurchaseValidator : AbstractValidator<SavePurchaseInvoiceRequest> { }
public class NullOpenDayValidator  : AbstractValidator<OpenWorkingDayRequest> { }
public class NullCloseDayValidator : AbstractValidator<CloseWorkingDayRequest> { }
public class NullUnitValidator     : AbstractValidator<SaveUnitRequest> { }
public class NullPartyValidator    : AbstractValidator<SavePartyRequest> { }
public class NullAdjustmentValidator : AbstractValidator<InventoryAdjustmentRequest> { }
public class NullStockCountValidator : AbstractValidator<CompleteStockCountRequest> { }
public class NullLoginValidator    : AbstractValidator<LoginRequest> { }
public class NullCreateBranchValidator : AbstractValidator<CreateBranchRequest> { }
public class NullUpdateBranchValidator : AbstractValidator<UpdateBranchRequest> { }
