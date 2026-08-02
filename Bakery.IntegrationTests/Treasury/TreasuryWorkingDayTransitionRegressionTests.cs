using Bakery.Application.DTOs;
using Bakery.Application.DTOs.Accounting;
using Bakery.Application.Interfaces;
using Bakery.Domain.Enums;
using Bakery.Infrastructure.Data;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Bakery.IntegrationTests;

public sealed class TreasuryWorkingDayTransitionRegressionTests : IClassFixture<DatabaseFixture>
{
    private readonly DatabaseFixture _fixture;

    public TreasuryWorkingDayTransitionRegressionTests(DatabaseFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Deposit_AfterReopenAndReclose_ShouldUseRefreshedWorkingDayRowVersion()
    {
        using var sessionScope = _fixture.ServiceProvider.CreateScope();
        var workingDays = sessionScope.ServiceProvider.GetRequiredService<IWorkingDayService>();
        var safes = sessionScope.ServiceProvider.GetRequiredService<ISafeService>();
        var db = sessionScope.ServiceProvider.GetRequiredService<BakeryDbContext>();

        var opened = await workingDays.OpenDayAsync(new OpenWorkingDayRequest(
            new DateOnly(2038, 7, 23),
            0m,
            "Treasury transition regression"));
        opened.Succeeded.Should().BeTrue(opened.ErrorMessage);
        var reopenedDayId = opened.Summary!.WorkingDayId;

        var firstClose = await workingDays.EndCurrentDayAndOpenNextAsync(new CloseWorkingDayRequest(
            0m,
            0m,
            "Create the automatic successor",
            ExpectedWorkingDayId: reopenedDayId,
            OperationId: Guid.NewGuid()));
        firstClose.Succeeded.Should().BeTrue(firstClose.ErrorMessage);
        var successorId = firstClose.Summary!.WorkingDayId;

        // Match the signed-in WPF session: the successor is tracked before the
        // fresh-scope reopen command cancels it and later reactivates it.
        (await workingDays.GetCurrentOpenDayAsync())!.Id.Should().Be(successorId);

        var reopened = await workingDays.ReopenDayAsync(
            reopenedDayId,
            "تصحيح إغلاق يوم العمل لاختبار الخزينة");
        reopened.Succeeded.Should().BeTrue(reopened.ErrorMessage);

        var secondClose = await workingDays.EndCurrentDayAndOpenNextAsync(new CloseWorkingDayRequest(
            0m,
            0m,
            "Reactivate the cancelled successor",
            ExpectedWorkingDayId: reopenedDayId,
            OperationId: Guid.NewGuid()));
        secondClose.Succeeded.Should().BeTrue(secondClose.ErrorMessage);
        secondClose.Summary!.WorkingDayId.Should().Be(successorId);

        var safeId = await db.Safes.OrderBy(safe => safe.Id).Select(safe => safe.Id).FirstAsync();
        var succeeded = await safes.ManualDepositAsync(new ManualCashTransactionRequest(
            safeId,
            25m,
            ManualMovementReason.OwnerCapital,
            "Deposit after Working Day reopen/reclose",
            "TREASURY-WD-REGRESSION",
            null,
            Guid.NewGuid().ToString("N")));

        succeeded.Should().BeTrue();
    }
}
