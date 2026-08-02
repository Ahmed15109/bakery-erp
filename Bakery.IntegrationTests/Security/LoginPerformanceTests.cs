using System.Diagnostics;
using System.Data.Common;
using Bakery.Application.DTOs;
using Bakery.Application.Interfaces;
using Bakery.Application.Security;
using Bakery.Domain.Entities;
using Bakery.Infrastructure.Data;
using Bakery.Infrastructure.Seeders;
using Bakery.Infrastructure.Services;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Xunit.Abstractions;

namespace Bakery.IntegrationTests;

public sealed class LoginPerformanceTests : IClassFixture<DatabaseFixture>
{
    private readonly DatabaseFixture _fixture;
    private readonly ITestOutputHelper _output;

    public LoginPerformanceTests(DatabaseFixture fixture, ITestOutputHelper output)
    {
        _fixture = fixture;
        _output = output;
    }

    [Fact]
    public async Task OptimizedLoginTiming_StaysWithinResponsiveBounds()
    {
        using var scope = _fixture.ServiceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BakeryDbContext>();
        await db.Database.EnsureDeletedAsync();
        await db.Database.EnsureCreatedAsync();
        var seeder = new DefaultDataSeeder(
            db,
            scope.ServiceProvider.GetRequiredService<IPasswordHasher>(),
            scope.ServiceProvider.GetRequiredService<ISystemSafeService>());
        await seeder.SeedAsync();
        var auth = scope.ServiceProvider.GetRequiredService<IAuthService>();

        var validTimer = Stopwatch.StartNew();
        var valid = await auth.LoginAsync(new LoginRequest("admin", "admin123-test-only"));
        validTimer.Stop();
        valid.Succeeded.Should().BeTrue();
        await auth.LogoutAsync();

        var invalidTimer = Stopwatch.StartNew();
        var invalid = await auth.LoginAsync(new LoginRequest("admin", "not-the-password"));
        invalidTimer.Stop();
        invalid.Succeeded.Should().BeFalse();

        validTimer.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(1.5));
        invalidTimer.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(1));
        _output.WriteLine("Captured baseline: valid 404.7 ms; invalid 97.8 ms");
        _output.WriteLine($"Valid login: {validTimer.Elapsed.TotalMilliseconds:F1} ms");
        _output.WriteLine($"Invalid login: {invalidTimer.Elapsed.TotalMilliseconds:F1} ms");
    }

    [Fact]
    public async Task LoginQueries_VerifyPasswordBeforeAuthorization_AndAvoidCartesianGraph()
    {
        using var setupScope = _fixture.ServiceProvider.CreateScope();
        var setupDb = setupScope.ServiceProvider.GetRequiredService<BakeryDbContext>();
        await setupDb.Database.EnsureDeletedAsync();
        await setupDb.Database.EnsureCreatedAsync();
        var passwordHasher = setupScope.ServiceProvider.GetRequiredService<IPasswordHasher>();
        var seeder = new DefaultDataSeeder(
            setupDb,
            passwordHasher,
            setupScope.ServiceProvider.GetRequiredService<ISystemSafeService>());
        await seeder.SeedAsync();
        var branchId = await setupDb.Branches.Select(branch => branch.Id).FirstAsync();
        var connectionString = setupDb.Database.GetConnectionString()!;

        var commands = new RecordingCommandInterceptor();
        var session = new UserSessionService();
        var branchContext = new BranchContext();
        var safeContext = new SafeContext();
        var options = new DbContextOptionsBuilder<BakeryDbContext>()
            .UseSqlServer(connectionString)
            .AddInterceptors(commands)
            .Options;
        await using var db = new BakeryDbContext(options, session, branchContext);
        var auth = new AuthService(
            db,
            passwordHasher,
            session,
            branchContext,
            safeContext,
            new AuditService(db, session, branchContext),
            new NullLoginValidator());

        var invalid = await auth.LoginAsync(new LoginRequest("admin", "invalid-password", branchId));

        invalid.Succeeded.Should().BeFalse();
        commands.CommandTexts.Should().NotContain(command =>
            command.Contains("[UserPermissions]", StringComparison.OrdinalIgnoreCase) ||
            command.Contains("[UserRoles]", StringComparison.OrdinalIgnoreCase) ||
            command.Contains("[RolePermissions]", StringComparison.OrdinalIgnoreCase) ||
            command.Contains("[UserBranches]", StringComparison.OrdinalIgnoreCase));

        commands.Clear();
        var valid = await auth.LoginAsync(new LoginRequest("admin", "admin123-test-only", branchId));

        valid.Succeeded.Should().BeTrue();
        var authorizationCommands = commands.CommandTexts.Where(command =>
            command.Contains("[UserPermissions]", StringComparison.OrdinalIgnoreCase) &&
            command.Contains("[UserRoles]", StringComparison.OrdinalIgnoreCase) &&
            command.Contains("[RolePermissions]", StringComparison.OrdinalIgnoreCase)).ToArray();
        authorizationCommands.Should().ContainSingle();
        authorizationCommands[0].Should().Contain("UNION ALL");
        authorizationCommands[0].Should().NotContain("[UserBranches]");
        db.ChangeTracker.Entries<UserPermission>().Should().BeEmpty();
        db.ChangeTracker.Entries<UserRole>().Should().BeEmpty();
        db.ChangeTracker.Entries<UserBranch>().Should().BeEmpty();
    }

    private sealed class RecordingCommandInterceptor : DbCommandInterceptor
    {
        private readonly List<string> _commandTexts = [];
        public IReadOnlyList<string> CommandTexts => _commandTexts;
        public void Clear() => _commandTexts.Clear();

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            _commandTexts.Add(command.CommandText);
            return ValueTask.FromResult(result);
        }

        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            _commandTexts.Add(command.CommandText);
            return ValueTask.FromResult(result);
        }
    }
}
