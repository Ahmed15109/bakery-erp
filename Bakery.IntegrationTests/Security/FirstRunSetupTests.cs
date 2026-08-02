using Bakery.Application.DTOs;
using Bakery.Application.Interfaces;
using Bakery.Domain.Entities;
using Bakery.Infrastructure.Data;
using Bakery.Infrastructure.Seeders;
using Bakery.Infrastructure.Services;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Bakery.IntegrationTests;

public sealed class FirstRunSetupTests
{
    [Fact]
    public async Task FreshDatabase_RequiresChosenValidPassword_AndCreatesExactlyOneAdministrator()
    {
        await using var database = await IsolatedSetupDatabase.CreateAsync();
        using var scope = database.Services.CreateScope();
        var setupService = scope.ServiceProvider.GetRequiredService<IFirstRunSetupService>();
        var db = scope.ServiceProvider.GetRequiredService<BakeryDbContext>();
        var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();

        (await setupService.IsSetupRequiredAsync()).Should().BeTrue();

        var weakPassword = await setupService.CreateInitialAdministratorAsync(
            new FirstRunAdminRequest("owner", "مالك المخبز", "short", "short"));
        weakPassword.Succeeded.Should().BeFalse();
        weakPassword.ErrorMessage.Should().Contain("12");
        (await db.Users.IgnoreQueryFilters().CountAsync()).Should().Be(0);

        const string chosenPassword = "Bakery-Owner-2042!";
        var created = await setupService.CreateInitialAdministratorAsync(
            new FirstRunAdminRequest("owner", "مالك المخبز", chosenPassword, chosenPassword));

        created.Succeeded.Should().BeTrue(created.ErrorMessage);
        created.UserId.Should().NotBeNull();
        (await setupService.IsSetupRequiredAsync()).Should().BeFalse();

        var user = await db.Users.IgnoreQueryFilters()
            .Include(item => item.UserRoles).ThenInclude(item => item.Role)
            .Include(item => item.UserBranches).ThenInclude(item => item.Branch)
            .SingleAsync();
        user.Username.Should().Be("owner");
        user.FullName.Should().Be("مالك المخبز");
        user.IsActive.Should().BeTrue();
        user.IsSuperAdmin.Should().BeTrue();
        user.UserRoles.Should().ContainSingle(item => item.Role.Name == "مسؤول النظام");
        user.UserBranches.Should().ContainSingle(item => item.Branch.Code == "MAIN");
        hasher.VerifyPassword(chosenPassword, user.PasswordHash).Should().BeTrue();

        var audit = await db.AuditLogs.IgnoreQueryFilters()
            .SingleAsync(item => item.Action == "FirstRunAdministratorCreated");
        audit.EntityId.Should().Be(user.Id);
        audit.NewValues.Should().NotContain(chosenPassword);

        var duplicate = await setupService.CreateInitialAdministratorAsync(
            new FirstRunAdminRequest("secondowner", "مالك ثان", "Another-Owner-2042!", "Another-Owner-2042!"));
        duplicate.Succeeded.Should().BeFalse();
        duplicate.ErrorMessage.Should().Contain("بالفعل");
        (await db.Users.IgnoreQueryFilters().CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task ConcurrentFirstRunRequests_CreateOnlyOneAdministrator()
    {
        await using var database = await IsolatedSetupDatabase.CreateAsync();
        using var firstScope = database.Services.CreateScope();
        using var secondScope = database.Services.CreateScope();
        var firstService = firstScope.ServiceProvider.GetRequiredService<IFirstRunSetupService>();
        var secondService = secondScope.ServiceProvider.GetRequiredService<IFirstRunSetupService>();

        var requests = await Task.WhenAll(
            firstService.CreateInitialAdministratorAsync(
                new FirstRunAdminRequest("ownerone", "المالك الأول", "First-Owner-2042!", "First-Owner-2042!")),
            secondService.CreateInitialAdministratorAsync(
                new FirstRunAdminRequest("ownertwo", "المالك الثاني", "Second-Owner-2042!", "Second-Owner-2042!")));

        requests.Count(result => result.Succeeded).Should().Be(1);
        requests.Count(result => !result.Succeeded).Should().Be(1);

        using var verificationScope = database.Services.CreateScope();
        var db = verificationScope.ServiceProvider.GetRequiredService<BakeryDbContext>();
        (await db.Users.IgnoreQueryFilters().CountAsync()).Should().Be(1);
        (await db.Users.IgnoreQueryFilters().CountAsync(item => item.IsSuperAdmin)).Should().Be(1);
        (await db.AuditLogs.IgnoreQueryFilters()
            .CountAsync(item => item.Action == "FirstRunAdministratorCreated")).Should().Be(1);
    }

    private sealed class IsolatedSetupDatabase : IAsyncDisposable
    {
        public ServiceProvider Services { get; }

        private IsolatedSetupDatabase(ServiceProvider services)
        {
            Services = services;
        }

        public static async Task<IsolatedSetupDatabase> CreateAsync()
        {
            var databaseName = $"BakeryERP_FirstRun_{Guid.NewGuid():N}";
            var connectionString =
                $"Server=(localdb)\\MSSQLLocalDB;Database={databaseName};Trusted_Connection=True;MultipleActiveResultSets=true;TrustServerCertificate=True";
            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["ConnectionStrings:BakeryDatabase"] = connectionString
                })
                .Build();
            var services = new ServiceCollection();
            services.AddSingleton<IConfiguration>(configuration);
            services.AddLogging();
            services.AddInfrastructure(configuration);
            var provider = services.BuildServiceProvider();

            using (var scope = provider.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<BakeryDbContext>();
                await db.Database.MigrateAsync();
                await scope.ServiceProvider.GetRequiredService<DefaultDataSeeder>().SeedAsync();

                // The existing environment-based bootstrap remains available for
                // explicit unattended setups and legacy tests. Remove any such user
                // so this database represents the interactive first-run state.
                await db.Users.IgnoreQueryFilters().ExecuteDeleteAsync();
                db.ChangeTracker.Clear();
            }

            return new IsolatedSetupDatabase(provider);
        }

        public async ValueTask DisposeAsync()
        {
            try
            {
                using var scope = Services.CreateScope();
                await scope.ServiceProvider.GetRequiredService<BakeryDbContext>().Database.EnsureDeletedAsync();
            }
            finally
            {
                await Services.DisposeAsync();
            }
        }
    }
}
