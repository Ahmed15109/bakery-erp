using Bakery.Application.DTOs;
using Bakery.Application.DTOs.Accounting;
using Bakery.Application.Interfaces;
using Bakery.Domain.Constants;
using Bakery.Domain.Enums;
using Bakery.Infrastructure.Data;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Bakery.IntegrationTests;

public sealed class WorkingDayPurchaseInvoiceBlockerTests : IClassFixture<DatabaseFixture>
{
    private readonly DatabaseFixture _fixture;

    public WorkingDayPurchaseInvoiceBlockerTests(DatabaseFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Readiness_BlocksOnlyDraftPurchase_AndPreservesPostedSupplierAccounting()
    {
        using var scope = _fixture.ServiceProvider.CreateScope();
        var workingDays = scope.ServiceProvider.GetRequiredService<IWorkingDayService>();
        var purchases = scope.ServiceProvider.GetRequiredService<IPurchaseInvoiceService>();
        var parties = scope.ServiceProvider.GetRequiredService<IPartyService>();
        var systemSafes = scope.ServiceProvider.GetRequiredService<ISystemSafeService>();
        var db = scope.ServiceProvider.GetRequiredService<BakeryDbContext>();

        var opened = await workingDays.OpenDayAsync(
            new OpenWorkingDayRequest(new DateOnly(2036, 7, 25), 1_000m, "Purchase blocker regression"));
        opened.Succeeded.Should().BeTrue(opened.ErrorMessage);

        var supplierId = await db.Parties
            .Where(party => party.Type == PartyType.Supplier)
            .Select(party => party.Id)
            .FirstAsync();
        var item = await db.Items.SingleAsync(candidate => candidate.Code == "FLOUR");
        var dailySafe = await systemSafes.GetDailySafeAsync();

        async Task<int> SaveAndPostAsync(PaymentType paymentType, decimal paidAmount, string notes)
        {
            var saved = await purchases.SaveDraftAsync(new SavePurchaseInvoiceRequest(
                null,
                supplierId,
                paymentType,
                paidAmount,
                notes,
                [new InvoiceLineRequest(item.Id, item.BaseUnitId, 1m, 100m)],
                dailySafe.Id));
            saved.Succeeded.Should().BeTrue(saved.ErrorMessage);

            var posted = await purchases.PostAsync(saved.InvoiceId!.Value);
            posted.Succeeded.Should().BeTrue(posted.ErrorMessage);
            return saved.InvoiceId.Value;
        }

        var cashInvoiceId = await SaveAndPostAsync(PaymentType.Cash, 100m, "Posted cash purchase");
        var partialInvoiceId = await SaveAndPostAsync(PaymentType.Mixed, 40m, "Posted partially paid purchase");
        var creditInvoiceId = await SaveAndPostAsync(PaymentType.Credit, 0m, "Posted unpaid credit purchase");

        db.ChangeTracker.Clear();
        var postedInvoices = await db.PurchaseInvoices
            .Where(invoice => invoice.Id == cashInvoiceId ||
                invoice.Id == partialInvoiceId || invoice.Id == creditInvoiceId)
            .OrderBy(invoice => invoice.Id)
            .ToListAsync();
        postedInvoices.Should().OnlyContain(invoice => invoice.Status == InvoiceStatus.Posted);
        postedInvoices.Single(invoice => invoice.Id == cashInvoiceId).RemainingAmount.Should().Be(0m);
        postedInvoices.Single(invoice => invoice.Id == partialInvoiceId).RemainingAmount.Should().Be(60m);
        postedInvoices.Single(invoice => invoice.Id == creditInvoiceId).PaidAmount.Should().Be(0m);
        postedInvoices.Single(invoice => invoice.Id == creditInvoiceId).RemainingAmount.Should().Be(100m);

        var postedOnlyReadiness = await workingDays.GetEndOfDayReadinessAsync();
        postedOnlyReadiness.Blockers.Should().BeEmpty(
            "posted purchases must not block close regardless of payment or outstanding balance");

        var savedDraft = await purchases.SaveDraftAsync(new SavePurchaseInvoiceRequest(
            null,
            supplierId,
            PaymentType.Credit,
            0m,
            "Blocking draft purchase",
            [new InvoiceLineRequest(item.Id, item.BaseUnitId, 1m, 100m)],
            dailySafe.Id));
        savedDraft.Succeeded.Should().BeTrue(savedDraft.ErrorMessage);

        db.ChangeTracker.Clear();
        var draft = await db.PurchaseInvoices.SingleAsync(invoice => invoice.Id == savedDraft.InvoiceId!.Value);
        draft.Status.Should().Be(InvoiceStatus.Draft);

        var supplierSummaryBefore = await parties.GetPartySummaryAsync(supplierId);
        var ledgerBefore = await db.PartyLedgerEntries
            .Where(entry => entry.PartyId == supplierId &&
                entry.ReferenceType == LedgerReferenceTypes.PurchaseInvoice)
            .OrderBy(entry => entry.Id)
            .Select(entry => new { entry.ReferenceId, entry.Debit, entry.Credit, entry.Amount })
            .ToListAsync();

        var readiness = await workingDays.GetEndOfDayReadinessAsync();

        var blocker = readiness.Blockers.Should().ContainSingle(candidate =>
            candidate.Kind == WorkingDayBlockerKind.PurchaseInvoice).Which;
        blocker.EntityId.Should().Be(draft.Id);
        blocker.ReferenceNumber.Should().Be(draft.InvoiceNumber);
        blocker.Code.Should().Be($"PURCHASE_INVOICE_{draft.Id}");
        blocker.ActionLabel.Should().Be("عرض فواتير المشتريات المسودة");
        blocker.Message.Should().Be(
            $"توجد فاتورة مشتريات مسودة لم يتم ترحيلها:{Environment.NewLine}" +
            $"رقم الفاتورة: {draft.InvoiceNumber}{Environment.NewLine}{Environment.NewLine}" +
            "يرجى ترحيل الفاتورة أو حذف المسودة قبل إنهاء يوم العمل.");
        postedInvoices.Should().OnlyContain(invoice => !blocker.Message.Contains(invoice.InvoiceNumber));

        db.ChangeTracker.Clear();
        var supplierSummaryAfter = await parties.GetPartySummaryAsync(supplierId);
        var ledgerAfter = await db.PartyLedgerEntries
            .Where(entry => entry.PartyId == supplierId &&
                entry.ReferenceType == LedgerReferenceTypes.PurchaseInvoice)
            .OrderBy(entry => entry.Id)
            .Select(entry => new { entry.ReferenceId, entry.Debit, entry.Credit, entry.Amount })
            .ToListAsync();

        supplierSummaryAfter.Should().Be(supplierSummaryBefore);
        supplierSummaryAfter.CurrentBalance.Should().Be(160m);
        ledgerAfter.Should().Equal(ledgerBefore);
        ledgerAfter.Should().HaveCount(3, "draft invoices must not create supplier ledger entries");
    }
}
