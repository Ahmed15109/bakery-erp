using Bakery.Application.Interfaces;
using Bakery.Infrastructure.Seeders;
using Microsoft.EntityFrameworkCore;
using Serilog;

namespace Bakery.Infrastructure.Data;

public sealed class DatabaseInitializer : IDatabaseInitializer
{
    private readonly BakeryDbContext _dbContext;
    private readonly DefaultDataSeeder _defaultDataSeeder;
    private readonly IBackupService _backupService;

    public DatabaseInitializer(BakeryDbContext dbContext, DefaultDataSeeder defaultDataSeeder, IBackupService backupService)
    {
        _dbContext = dbContext;
        _defaultDataSeeder = defaultDataSeeder;
        _backupService = backupService;
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        Log.Information("Initializing local bakery database");

        var pendingMigrations = (await _dbContext.Database.GetPendingMigrationsAsync(cancellationToken)).ToList();
        if (pendingMigrations.Count > 0)
        {
            Log.Information("Applying {MigrationCount} pending EF Core migrations: {Migrations}", pendingMigrations.Count, string.Join(", ", pendingMigrations));

            // MIGRATION SAFETY MODE: Auto-backup before migration (only if database already exists)
            if (await _dbContext.Database.CanConnectAsync(cancellationToken))
            {
                try
                {
                    await _backupService.CreateSafetySnapshotAsync("PreMigration", cancellationToken);
                    Log.Information("Safety backup created successfully before migration.");
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "FAILED to create safety backup before migration. Aborting migration for safety.");
                    throw new InvalidOperationException("Migration safety backup failed. Startup blocked to prevent data loss.", ex);
                }
            }
            else
            {
                Log.Information("Database does not exist yet. Skipping safety backup before initial creation/migration.");
            }
        }
        else
        {
            Log.Information("No pending EF Core migrations found");
        }

        try
        {
            await _dbContext.Database.MigrateAsync(cancellationToken);
            await _defaultDataSeeder.SeedAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "DATABASE MIGRATION FAILED. System may be in an inconsistent state.");
            throw; // Will be caught by global exception handler or startup health check
        }
    }
}
