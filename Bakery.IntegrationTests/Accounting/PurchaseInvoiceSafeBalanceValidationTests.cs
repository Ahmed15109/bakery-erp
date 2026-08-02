using Bakery.Application.DTOs;
using Bakery.Application.DTOs.Accounting;
using Bakery.Application.Interfaces;
using Bakery.Domain.Entities;
using Bakery.Domain.Enums;
using Bakery.Infrastructure.Data;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Bakery.IntegrationTests;

public sealed class PurchaseInvoiceSafeBalanceValidationTests : IClassFixture<DatabaseFixture>
{
    private readonly DatabaseFixture _fixture;

    public PurchaseInvoiceSafeBalanceValidationTests(DatabaseFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task SaveAndPost_ValidatesSafeBalanceBeforeCreatingDraftOrAllocatingNumber()
    {
        using var scope = _fixture.ServiceProvider.CreateScope();
        var workingDays = scope.ServiceProvider.GetRequiredService<IWorkingDayService>();
        var purchases = scope.ServiceProvider.GetRequiredService<IPurchaseInvoiceService>();
        var safeService = scope.ServiceProvider.GetRequiredService<ISystemSafeService>();
        var db = scope.ServiceProvider.GetRequiredService<BakeryDbContext>();
        var businessDate = new DateOnly(2037, 7, 25);

        var opened = await workingDays.OpenDayAsync(
            new OpenWorkingDayRequest(businessDate, 200m, "Purchase safe-balance validation"));
        opened.Succeeded.Should().BeTrue(opened.ErrorMessage);

        var supplierId = await db.Parties
            .Where(party => party.Type == PartyType.Supplier)
            .Select(party => party.Id)
            .FirstAsync();
        var item = await db.Items.SingleAsync(candidate => candidate.Code == "FLOUR");
        var safe = await safeService.GetDailySafeAsync();
        var counterPrefix = $"INVOICE:P:{businessDate:yyyyMMdd}";

        SavePurchaseInvoiceRequest Request(int? id, decimal paidAmount, decimal total) => new(
            id,
            supplierId,
            PaymentType.Cash,
            paidAmount,
            "Purchase safe-balance regression",
            [new InvoiceLineRequest(item.Id, item.BaseUnitId, 1m, total)],
            safe.Id);

        async Task<int> SaveAndPostAsync(decimal paidAmount, decimal total)
        {
            var saved = await purchases.SaveDraftAsync(Request(null, paidAmount, total));
            saved.Succeeded.Should().BeTrue(saved.ErrorMessage);
            var posted = await purchases.PostAsync(saved.InvoiceId!.Value);
            posted.Succeeded.Should().BeTrue(posted.ErrorMessage);
            return saved.InvoiceId.Value;
        }

        var belowBalanceId = await SaveAndPostAsync(50m, 100m);
        var equalBalanceId = await SaveAndPostAsync(150m, 150m);

        var validInvoices = await db.PurchaseInvoices
            .Where(invoice => invoice.Id == belowBalanceId || invoice.Id == equalBalanceId)
            .OrderBy(invoice => invoice.Id)
            .ToListAsync();
        validInvoices.Should().OnlyContain(invoice => invoice.Status == InvoiceStatus.Posted);
        validInvoices.Select(invoice => invoice.InvoiceNumber).Should().Equal(
            $"P-{businessDate:yyyyMMdd}-0001",
            $"P-{businessDate:yyyyMMdd}-0002");
        (await db.SafeMovements.Where(movement => movement.SafeId == safe.Id)
            .SumAsync(movement => (decimal?)movement.Amount) ?? 0m).Should().Be(0m);

        db.ChangeTracker.Clear();
        var invoiceCountBefore = await db.PurchaseInvoices.CountAsync();
        var lineCountBefore = await db.PurchaseInvoiceLines.CountAsync();
        var inventoryCountBefore = await db.InventoryMovements.CountAsync();
        var ledgerCountBefore = await db.PartyLedgerEntries.CountAsync();
        var treasuryCountBefore = await db.SafeMovements.CountAsync();
        var auditCountBefore = await db.AuditLogs.CountAsync();
        var counterBefore = await db.TransactionNumberCounters
            .Where(counter => counter.Prefix == counterPrefix)
            .Select(counter => counter.LastValue)
            .SingleAsync();

        var rejected = await purchases.SaveDraftAsync(Request(null, 1m, 1m));

        rejected.Succeeded.Should().BeFalse();
        rejected.InvoiceId.Should().BeNull();
        rejected.ErrorMessage.Should().Be(
            $"لا يمكن سداد هذا المبلغ.{Environment.NewLine}" +
            $"رصيد الخزنة الحالية هو:{Environment.NewLine}0.00{Environment.NewLine}{Environment.NewLine}" +
            $"والمبلغ المطلوب دفعه هو:{Environment.NewLine}1.00{Environment.NewLine}{Environment.NewLine}" +
            "يرجى تخفيض المبلغ المدفوع أو اختيار خزنة أخرى.");
        (await db.PurchaseInvoices.CountAsync()).Should().Be(invoiceCountBefore);
        (await db.PurchaseInvoiceLines.CountAsync()).Should().Be(lineCountBefore);
        (await db.InventoryMovements.CountAsync()).Should().Be(inventoryCountBefore);
        (await db.PartyLedgerEntries.CountAsync()).Should().Be(ledgerCountBefore);
        (await db.SafeMovements.CountAsync()).Should().Be(treasuryCountBefore);
        (await db.AuditLogs.CountAsync()).Should().Be(auditCountBefore);
        (await db.TransactionNumberCounters
            .Where(counter => counter.Prefix == counterPrefix)
            .Select(counter => counter.LastValue)
            .SingleAsync()).Should().Be(counterBefore);

        var nextValid = await purchases.SaveDraftAsync(Request(null, 0m, 1m));
        nextValid.Succeeded.Should().BeTrue(nextValid.ErrorMessage);
        var existingDraft = await db.PurchaseInvoices
            .Include(invoice => invoice.Lines)
            .SingleAsync(invoice => invoice.Id == nextValid.InvoiceId!.Value);
        existingDraft.InvoiceNumber.Should().Be($"P-{businessDate:yyyyMMdd}-0003",
            "the rejected attempt must not consume invoice number 0003");

        existingDraft.PaymentType = PaymentType.Cash;
        existingDraft.PaidAmount = 1m;
        existingDraft.RemainingAmount = 0m;
        await db.SaveChangesAsync();
        var existingNumber = existingDraft.InvoiceNumber;
        var invoiceCountWithExistingDraft = await db.PurchaseInvoices.CountAsync();
        var counterWithExistingDraft = await db.TransactionNumberCounters
            .Where(counter => counter.Prefix == counterPrefix)
            .Select(counter => counter.LastValue)
            .SingleAsync();

        var rejectedRetry = await purchases.SaveDraftAsync(Request(existingDraft.Id, 1m, 1m));

        rejectedRetry.Succeeded.Should().BeFalse();
        rejectedRetry.InvoiceId.Should().BeNull();
        (await db.PurchaseInvoices.CountAsync()).Should().Be(invoiceCountWithExistingDraft);
        (await db.PurchaseInvoices.CountAsync(invoice => invoice.InvoiceNumber == existingNumber))
            .Should().Be(1);
        (await db.TransactionNumberCounters
            .Where(counter => counter.Prefix == counterPrefix)
            .Select(counter => counter.LastValue)
            .SingleAsync()).Should().Be(counterWithExistingDraft);

        db.SafeMovements.Add(new SafeMovement
        {
            WorkingDayId = opened.Summary!.WorkingDayId,
            SafeId = safe.Id,
            Type = SafeMovementType.Adjustment,
            Amount = 1m,
            Description = "Fund existing draft retry"
        });
        await db.SaveChangesAsync();

        var successfulRetry = await purchases.SaveDraftAsync(Request(existingDraft.Id, 1m, 1m));
        successfulRetry.Succeeded.Should().BeTrue(successfulRetry.ErrorMessage);
        successfulRetry.InvoiceId.Should().Be(existingDraft.Id);
        (await purchases.PostAsync(existingDraft.Id)).Succeeded.Should().BeTrue();

        db.ChangeTracker.Clear();
        var persistedRetry = await db.PurchaseInvoices.SingleAsync(invoice => invoice.Id == existingDraft.Id);
        persistedRetry.Status.Should().Be(InvoiceStatus.Posted);
        persistedRetry.InvoiceNumber.Should().Be(existingNumber);
        (await db.PurchaseInvoices.CountAsync(invoice => invoice.InvoiceNumber == existingNumber))
            .Should().Be(1);
        (await db.TransactionNumberCounters
            .Where(counter => counter.Prefix == counterPrefix)
            .Select(counter => counter.LastValue)
            .SingleAsync()).Should().Be(counterWithExistingDraft);
    }
}
