using Bakery.Application.DTOs.Inventory;
using Bakery.Application.Interfaces;
using Bakery.Infrastructure.Data;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Bakery.IntegrationTests;

public class UnitDeletionTests : IClassFixture<DatabaseFixture>
{
    private readonly DatabaseFixture _fixture;

    public UnitDeletionTests(DatabaseFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task DeleteAsync_ShouldSoftDeleteUnusedUnit()
    {
        using var scope = _fixture.ServiceProvider.CreateScope();
        var unitService = scope.ServiceProvider.GetRequiredService<IUnitService>();
        var db = scope.ServiceProvider.GetRequiredService<BakeryDbContext>();

        var saveResult = await unitService.SaveAsync(new SaveUnitRequest(null, "Unused Unit", "unused-unit", true));
        saveResult.Succeeded.Should().BeTrue(saveResult.ErrorMessage);

        var deleteResult = await unitService.DeleteAsync(saveResult.Unit!.Id);
        deleteResult.Succeeded.Should().BeTrue(deleteResult.ErrorMessage);

        var visibleUnits = await unitService.ListAsync();
        visibleUnits.Should().NotContain(unit => unit.Id == saveResult.Unit.Id);

        var deletedUnit = await db.Units.IgnoreQueryFilters().FirstAsync(unit => unit.Id == saveResult.Unit.Id);
        deletedUnit.IsDeleted.Should().BeTrue();
        deletedUnit.IsActive.Should().BeFalse();
    }

    [Fact]
    public async Task DeleteAsync_ShouldPreventDeletingUsedUnit()
    {
        using var scope = _fixture.ServiceProvider.CreateScope();
        var unitService = scope.ServiceProvider.GetRequiredService<IUnitService>();
        var db = scope.ServiceProvider.GetRequiredService<BakeryDbContext>();

        var usedUnit = await db.Units.FirstAsync(unit => unit.Symbol == "kg");
        var deleteResult = await unitService.DeleteAsync(usedUnit.Id);

        deleteResult.Succeeded.Should().BeFalse();
        deleteResult.ErrorMessage.Should().Be("لا يمكن حذف الوحدة لأنها مستخدمة في النظام");
    }
}
