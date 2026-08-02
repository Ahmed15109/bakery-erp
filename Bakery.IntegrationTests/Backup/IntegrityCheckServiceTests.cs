using Bakery.Application.Interfaces;
using Bakery.Domain.Entities;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Bakery.IntegrationTests;

public class IntegrityCheckServiceTests : IClassFixture<DatabaseFixture>
{
    private readonly DatabaseFixture _fixture;

    public IntegrityCheckServiceTests(DatabaseFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task RunFullCheckAsync_OnCleanDatabase_ShouldPass()
    {
        using var scope = _fixture.ServiceProvider.CreateScope();
        var integrityService = scope.ServiceProvider.GetRequiredService<IIntegrityCheckService>();

        var isHealthy = await integrityService.RunFullCheckAsync();

        isHealthy.Should().BeTrue("The fresh database should not have any orphan reversals.");
    }
}
