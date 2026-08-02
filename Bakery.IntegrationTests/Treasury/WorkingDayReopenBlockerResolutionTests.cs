using Bakery.Application.DTOs;
using Bakery.Application.DTOs.Accounting;
using Bakery.Application.Interfaces;
using Bakery.Application.Security;
using Bakery.Domain.Constants;
using Bakery.Domain.Entities;
using Bakery.Domain.Enums;
using Bakery.Infrastructure.Data;
using Bakery.Shared.Auditing;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Bakery.IntegrationTests;

public sealed class WorkingDayReopenBlockerResolutionTests : IClassFixture<DatabaseFixture>
{
    private readonly DatabaseFixture _fixture;

    public WorkingDayReopenBlockerResolutionTests(DatabaseFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task ReopenWorkflow_ResolvesSupportedBlockers_PreservesOriginals_AndReopensExactlyOneDay()
    {
        int targetDayId;
        int currentDayId;
        int saleId;
        int purchaseId;
        int draftId;
        int productionId;
        int manualMovementId;
        decimal initialBreadStock;
        decimal initialFlourStock;

        using (var setupScope = _fixture.ServiceProvider.CreateScope())
        {
            var db = setupScope.ServiceProvider.GetRequiredService<BakeryDbContext>();
            var days = setupScope.ServiceProvider.GetRequiredService<IWorkingDayService>();
            var date21 = DateOnly.FromDateTime(DateTime.Today.AddDays(-260));
            var opened21 = await days.OpenDayAsync(new OpenWorkingDayRequest(date21, 0, "تهيئة اختبار معالجة الموانع"));
            opened21.Succeeded.Should().BeTrue(opened21.ErrorMessage);

            var bread = await db.Items.SingleAsync(item => item.Code == "BREAD");
            var flour = await db.Items.SingleAsync(item => item.Code == "FLOUR");
            db.InventoryMovements.AddRange(
                new InventoryMovement
                {
                    WorkingDayId = opened21.Summary!.WorkingDayId,
                    ItemId = bread.Id,
                    UnitId = bread.BaseUnitId,
                    Type = InventoryMovementType.Adjustment,
                    Quantity = 10,
                    UnitCost = 5,
                    ReferenceType = "TestOpeningStock"
                },
                new InventoryMovement
                {
                    WorkingDayId = opened21.Summary.WorkingDayId,
                    ItemId = flour.Id,
                    UnitId = flour.BaseUnitId,
                    Type = InventoryMovementType.Adjustment,
                    Quantity = 100,
                    UnitCost = 20,
                    ReferenceType = "TestOpeningStock"
                });
            await db.SaveChangesAsync();
            initialBreadStock = 10;
            initialFlourStock = 100;

            var close21 = await days.EndCurrentDayAndOpenNextAsync(new CloseWorkingDayRequest(
                0, 0, "إغلاق يوم التهيئة", true, "تجاوز مخزون التهيئة للاختبار",
                opened21.Summary.WorkingDayId, Guid.NewGuid()));
            close21.Succeeded.Should().BeTrue(close21.ErrorMessage);
            targetDayId = close21.Summary!.WorkingDayId;

            var close22 = await days.EndCurrentDayAndOpenNextAsync(new CloseWorkingDayRequest(
                0, 0, "إغلاق اليوم المستهدف وفتح اليوم التالي", false, null,
                targetDayId, Guid.NewGuid()));
            close22.Succeeded.Should().BeTrue(close22.ErrorMessage);
            currentDayId = close22.Summary!.WorkingDayId;

            var customer = await db.Parties.SingleAsync(item => item.Name == "Customer A");
            var supplier = await db.Parties.SingleAsync(item => item.Name == "Supplier A");
            var safe = await db.Safes.FirstAsync(item => item.IsActive);
            var user = await db.Users.SingleAsync(item => item.Username == "test-admin");
            if (!await db.UserSafePermissions.AnyAsync(item => item.UserId == user.Id && item.SafeId == safe.Id))
            {
                db.UserSafePermissions.Add(new UserSafePermission
                {
                    UserId = user.Id,
                    SafeId = safe.Id,
                    CanAccess = true,
                    CanViewBalance = true,
                    CanViewLedger = true,
                    CanCashIn = true,
                    CanCashOut = true
                });
            }

            var sale = new SaleInvoice
            {
                InvoiceNumber = $"SALE-REOPEN-{Guid.NewGuid():N}", InvoiceDate = DateTime.UtcNow,
                WorkingDayId = currentDayId, PartyId = customer.Id, Status = InvoiceStatus.Posted,
                PaymentType = PaymentType.Credit, TotalAmount = 20, RemainingAmount = 20,
                Lines = [new SaleInvoiceLine { ItemId = bread.Id, UnitId = bread.BaseUnitId, Quantity = 2, UnitPrice = 10, LineTotal = 20 }]
            };
            var purchase = new PurchaseInvoice
            {
                InvoiceNumber = $"PUR-REOPEN-{Guid.NewGuid():N}", InvoiceDate = DateTime.UtcNow,
                WorkingDayId = currentDayId, PartyId = supplier.Id, Status = InvoiceStatus.Posted,
                PaymentType = PaymentType.Credit, TotalAmount = 20, RemainingAmount = 20,
                Lines = [new PurchaseInvoiceLine { ItemId = flour.Id, UnitId = flour.BaseUnitId, Quantity = 1, UnitPrice = 20, LineTotal = 20 }]
            };
            var draft = new PurchaseInvoice
            {
                InvoiceNumber = $"DRAFT-REOPEN-{Guid.NewGuid():N}", InvoiceDate = DateTime.UtcNow,
                WorkingDayId = currentDayId, PartyId = supplier.Id, Status = InvoiceStatus.Draft,
                PaymentType = PaymentType.Credit, TotalAmount = 10, RemainingAmount = 10,
                Lines = [new PurchaseInvoiceLine { ItemId = flour.Id, UnitId = flour.BaseUnitId, Quantity = 1, UnitPrice = 10, LineTotal = 10 }]
            };
            var production = new ProductionOrder
            {
                ProductionNumber = $"PROD-REOPEN-{Guid.NewGuid():N}", StartedAt = DateTime.UtcNow,
                CompletedAt = DateTime.UtcNow, WorkingDayId = currentDayId, Status = ProductionStatus.Completed,
                ConsumedItems = [new ProductionConsumedItem { ItemId = flour.Id, UnitId = flour.BaseUnitId, Quantity = 2, UnitCost = 20 }],
                ProducedItems = [new ProductionProducedItem { ItemId = bread.Id, UnitId = bread.BaseUnitId, ExpectedProducedQty = 3, ActualProducedQty = 3, UnitCost = 10 }]
            };
            db.AddRange(sale, purchase, draft, production);
            await db.SaveChangesAsync();
            saleId = sale.Id;
            purchaseId = purchase.Id;
            draftId = draft.Id;
            productionId = production.Id;

            db.InventoryMovements.AddRange(
                new InventoryMovement { WorkingDayId = currentDayId, ItemId = bread.Id, UnitId = bread.BaseUnitId, Type = InventoryMovementType.Sale, Quantity = -2, UnitCost = 5, ReferenceType = LedgerReferenceTypes.SaleInvoice, ReferenceId = sale.Id },
                new InventoryMovement { WorkingDayId = currentDayId, ItemId = flour.Id, UnitId = flour.BaseUnitId, Type = InventoryMovementType.Purchase, Quantity = 1, UnitCost = 20, ReferenceType = LedgerReferenceTypes.PurchaseInvoice, ReferenceId = purchase.Id },
                new InventoryMovement { WorkingDayId = currentDayId, ItemId = flour.Id, UnitId = flour.BaseUnitId, Type = InventoryMovementType.ProductionConsume, Quantity = -2, UnitCost = 20, ReferenceType = "ProductionOrder", ReferenceId = production.Id },
                new InventoryMovement { WorkingDayId = currentDayId, ItemId = bread.Id, UnitId = bread.BaseUnitId, Type = InventoryMovementType.ProductionProduce, Quantity = 3, UnitCost = 10, ReferenceType = "ProductionOrder", ReferenceId = production.Id });
            db.PartyLedgerEntries.AddRange(
                new PartyLedgerEntry { WorkingDayId = currentDayId, PartyId = customer.Id, Debit = 20, Amount = 20, Description = "فاتورة بيع اختبار", ReferenceType = LedgerReferenceTypes.SaleInvoice, ReferenceId = sale.Id },
                new PartyLedgerEntry { WorkingDayId = currentDayId, PartyId = supplier.Id, Credit = 20, Amount = 20, Description = "فاتورة شراء اختبار", ReferenceType = LedgerReferenceTypes.PurchaseInvoice, ReferenceId = purchase.Id });
            var manualMovement = new SafeMovement
            {
                WorkingDayId = currentDayId, SafeId = safe.Id, Type = SafeMovementType.Adjustment,
                Amount = 30, Description = "إيداع يدوي لاختبار إعادة الفتح", Origin = CashMovementOrigin.Manual,
                TransactionNumber = $"DEP-{Guid.NewGuid():N}", CreatedByUserId = user.Id, CreatedByUserName = user.Username
            };
            db.SafeMovements.Add(manualMovement);
            await db.SaveChangesAsync();
            manualMovementId = manualMovement.Id;
        }

        using (var resolveScope = _fixture.ServiceProvider.CreateScope())
        {
            var days = resolveScope.ServiceProvider.GetRequiredService<IWorkingDayService>();
            var resolver = resolveScope.ServiceProvider.GetRequiredService<IWorkingDayReopenResolutionService>();
            var session = resolveScope.ServiceProvider.GetRequiredService<IUserSessionService>();
            var eligibility = await days.GetReopenEligibilityAsync();
            eligibility.CanReopen.Should().BeFalse();
            eligibility.Blockers.Should().NotBeNull();
            eligibility.Blockers!.Select(item => item.Code).Should().Contain([
                $"SALE:{saleId}", $"PURCHASE:{purchaseId}", $"PURCHASE:{draftId}",
                $"PRODUCTION:{productionId}", $"SAFE:{manualMovementId}"]);
            eligibility.Blockers.Single(item => item.Code == $"PURCHASE:{draftId}").ActionLabel.Should().Be("حذف المسودة");

            var missingReason = await resolver.ResolveAsync(new($"SALE:{saleId}", "english only", Guid.NewGuid()));
            missingReason.Succeeded.Should().BeFalse();

            var admin = session.CurrentUser!;
            session.SignIn(new AuthenticatedUserDto(admin.UserId, admin.Username, admin.FullName,
                [PermissionKeys.WorkingDayView, PermissionKeys.WorkingDayReopen], false));
            var denied = await resolver.ResolveAsync(new($"SALE:{saleId}", "اختبار منع تجاوز الصلاحيات", Guid.NewGuid()));
            denied.Succeeded.Should().BeFalse();
            denied.ErrorMessage.Should().Contain("الصلاحية");
            session.SignIn(admin);

            var duplicateResults = await Task.WhenAll(
                resolver.ResolveAsync(new($"SALE:{saleId}", "إلغاء البيع لتصحيح يوم العمل", Guid.NewGuid())),
                resolver.ResolveAsync(new($"SALE:{saleId}", "إلغاء البيع لتصحيح يوم العمل", Guid.NewGuid())));
            duplicateResults.Should().OnlyContain(result => result.Succeeded);
            duplicateResults.Count(result => !result.WasAlreadyResolved).Should().Be(1);

            (await resolver.ResolveAsync(new($"PRODUCTION:{productionId}", "عكس الإنتاج لتصحيح يوم العمل", Guid.NewGuid()))).Succeeded.Should().BeTrue();
            (await resolver.ResolveAsync(new($"PURCHASE:{purchaseId}", "إلغاء الشراء لتصحيح يوم العمل", Guid.NewGuid()))).Succeeded.Should().BeTrue();
            (await resolver.ResolveAsync(new($"PURCHASE:{draftId}", "حذف المسودة لتصحيح يوم العمل", Guid.NewGuid()))).Succeeded.Should().BeTrue();
            (await resolver.ResolveAsync(new($"SAFE:{manualMovementId}", "عكس الإيداع لتصحيح يوم العمل", Guid.NewGuid()))).Succeeded.Should().BeTrue();

            eligibility = await days.GetReopenEligibilityAsync();
            eligibility.Blockers.Should().BeEmpty();
            eligibility.CanReopen.Should().BeTrue(eligibility.StatusMessage);

            var reopened = await days.ReopenDayAsync(targetDayId, "إعادة فتح اليوم السابق بعد معالجة جميع العمليات");
            reopened.Succeeded.Should().BeTrue(reopened.ErrorMessage);
        }

        using (var verifyScope = _fixture.ServiceProvider.CreateScope())
        {
            var db = verifyScope.ServiceProvider.GetRequiredService<BakeryDbContext>();
            var stock = verifyScope.ServiceProvider.GetRequiredService<IStockCalculationService>();
            var bread = await db.Items.SingleAsync(item => item.Code == "BREAD");
            var flour = await db.Items.SingleAsync(item => item.Code == "FLOUR");
            (await stock.GetCurrentStockAsync(bread.Id)).Should().Be(initialBreadStock);
            (await stock.GetCurrentStockAsync(flour.Id)).Should().Be(initialFlourStock);

            var sale = await db.SaleInvoices.IgnoreQueryFilters().SingleAsync(item => item.Id == saleId);
            var purchase = await db.PurchaseInvoices.IgnoreQueryFilters().SingleAsync(item => item.Id == purchaseId);
            var draft = await db.PurchaseInvoices.IgnoreQueryFilters().SingleAsync(item => item.Id == draftId);
            var production = await db.ProductionOrders.IgnoreQueryFilters().SingleAsync(item => item.Id == productionId);
            sale.Status.Should().Be(InvoiceStatus.Cancelled);
            sale.IsDeleted.Should().BeFalse();
            purchase.Status.Should().Be(InvoiceStatus.Cancelled);
            purchase.IsDeleted.Should().BeFalse();
            draft.IsDeleted.Should().BeTrue();
            production.Status.Should().Be(ProductionStatus.Cancelled);
            production.IsDeleted.Should().BeFalse();

            var originalManual = await db.SafeMovements.SingleAsync(item => item.Id == manualMovementId);
            originalManual.ReversedBy.Should().NotBeNull();
            (await db.SafeMovements.CountAsync(item => item.OriginalTransactionId == manualMovementId)).Should().Be(1);
            (await db.WorkingDays.CountAsync(item => item.Status == WorkingDayStatus.Open)).Should().Be(1);
            (await db.WorkingDays.SingleAsync(item => item.Id == targetDayId)).Status.Should().Be(WorkingDayStatus.Open);
            var cancelledSuccessor = await db.WorkingDays.IgnoreQueryFilters().SingleAsync(item => item.Id == currentDayId);
            cancelledSuccessor.Status.Should().Be(WorkingDayStatus.Cancelled);
            cancelledSuccessor.IsDeleted.Should().BeFalse();
            (await db.AuditLogs.CountAsync(item => item.Action == AuditActionKeys.WorkingDayReopenBlockerResolved)).Should().Be(5);
            (await db.AuditLogs.CountAsync(item => item.Action == AuditActionKeys.SaleInvoiceCancelled && item.EntityId == saleId)).Should().Be(1);
        }
    }
}

public sealed class PartyPaymentReopenReversalTests : IClassFixture<DatabaseFixture>
{
    private readonly DatabaseFixture _fixture;

    public PartyPaymentReopenReversalTests(DatabaseFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task PartyPaymentReversal_RestoresPartyAndSafeBalances_AndLinksOneOfficialReversal()
    {
        using var scope = _fixture.ServiceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BakeryDbContext>();
        var days = scope.ServiceProvider.GetRequiredService<IWorkingDayService>();
        var payments = scope.ServiceProvider.GetRequiredService<IPartyPaymentService>();
        var parties = scope.ServiceProvider.GetRequiredService<IPartyService>();
        var safes = scope.ServiceProvider.GetRequiredService<ISafeService>();
        var session = scope.ServiceProvider.GetRequiredService<IUserSessionService>();

        var opened = await days.OpenDayAsync(new OpenWorkingDayRequest(
            DateOnly.FromDateTime(DateTime.Today.AddDays(-310)), 0, "اختبار عكس دفعة حساب"));
        opened.Succeeded.Should().BeTrue(opened.ErrorMessage);
        var customer = await db.Parties.SingleAsync(item => item.Name == "Customer A");
        var safe = await db.Safes.FirstAsync(item => item.IsActive);
        var user = await db.Users.SingleAsync(item => item.Username == "test-admin");
        if (!await db.UserSafePermissions.AnyAsync(item => item.UserId == user.Id && item.SafeId == safe.Id))
        {
            db.UserSafePermissions.Add(new UserSafePermission
            {
                UserId = user.Id, SafeId = safe.Id, CanAccess = true, CanViewBalance = true,
                CanViewLedger = true, CanCashIn = true, CanCashOut = true
            });
        }
        db.PartyLedgerEntries.Add(new PartyLedgerEntry
        {
            WorkingDayId = opened.Summary!.WorkingDayId, PartyId = customer.Id,
            Debit = 500, Amount = 500, Description = "مديونية عميل للاختبار",
            ReferenceType = LedgerReferenceTypes.SaleInvoice, ReferenceId = 999999
        });
        await db.SaveChangesAsync();
        session.SignIn(new AuthenticatedUserDto(user.Id, user.Username, user.FullName,
            PermissionCatalog.All.Select(item => item.Key).ToArray(), true));

        var safeBefore = await safes.GetBalanceAsync(safe.Id);
        var partyBefore = await parties.GetBalanceAsync(customer.Id);
        var processed = await payments.ProcessPaymentAsync(customer.Id, safe.Id, 125, "تحصيل لاختبار العكس", true);
        processed.Succeeded.Should().BeTrue(processed.ErrorMessage);
        var original = await db.SafeMovements.SingleAsync(item =>
            item.ReferenceType == LedgerReferenceTypes.CustomerReceipt && item.ReferenceId == customer.Id);
        (await safes.GetBalanceAsync(safe.Id)).Should().Be(safeBefore + 125);
        (await parties.GetBalanceAsync(customer.Id)).Should().Be(partyBefore - 125);

        var reversed = await payments.ReversePaymentAsync(
            original.Id, "عكس التحصيل لتصحيح العملية", Guid.NewGuid(), true);
        reversed.Succeeded.Should().BeTrue(reversed.ErrorMessage);
        (await safes.GetBalanceAsync(safe.Id)).Should().Be(safeBefore);
        (await parties.GetBalanceAsync(customer.Id)).Should().Be(partyBefore);
        (await db.SafeMovements.CountAsync(item => item.OriginalTransactionId == original.Id)).Should().Be(1);
        (await db.PartyLedgerEntries.CountAsync(item => item.ReversalReferenceId != null && item.SourceSafeMovementId == null)).Should().BeGreaterThan(0);

        var repeated = await payments.ReversePaymentAsync(
            original.Id, "محاولة عكس مكررة للعملية", Guid.NewGuid(), true);
        repeated.Succeeded.Should().BeTrue();
        (await db.SafeMovements.CountAsync(item => item.OriginalTransactionId == original.Id)).Should().Be(1);
    }
}

public sealed class FailedReversalRollbackTests : IClassFixture<DatabaseFixture>
{
    private readonly DatabaseFixture _fixture;

    public FailedReversalRollbackTests(DatabaseFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task PurchaseCancellation_WithConsumedStock_RollsBackWithoutPartialEffects()
    {
        using var scope = _fixture.ServiceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BakeryDbContext>();
        var days = scope.ServiceProvider.GetRequiredService<IWorkingDayService>();
        var purchases = scope.ServiceProvider.GetRequiredService<IPurchaseInvoiceService>();
        var opened = await days.OpenDayAsync(new OpenWorkingDayRequest(
            DateOnly.FromDateTime(DateTime.Today.AddDays(-330)), 0, "اختبار تراجع فاشل آمن"));
        opened.Succeeded.Should().BeTrue(opened.ErrorMessage);
        var supplier = await db.Parties.SingleAsync(item => item.Name == "Supplier A");
        var flour = await db.Items.SingleAsync(item => item.Code == "FLOUR");
        var invoice = new PurchaseInvoice
        {
            InvoiceNumber = $"PUR-ROLLBACK-{Guid.NewGuid():N}", InvoiceDate = DateTime.UtcNow,
            WorkingDayId = opened.Summary!.WorkingDayId, PartyId = supplier.Id,
            Status = InvoiceStatus.Posted, PaymentType = PaymentType.Credit,
            TotalAmount = 100, RemainingAmount = 100,
            Lines = [new PurchaseInvoiceLine { ItemId = flour.Id, UnitId = flour.BaseUnitId, Quantity = 5, UnitPrice = 20, LineTotal = 100 }]
        };
        db.PurchaseInvoices.Add(invoice);
        await db.SaveChangesAsync();
        var originalMovement = new InventoryMovement
        {
            WorkingDayId = opened.Summary.WorkingDayId, ItemId = flour.Id, UnitId = flour.BaseUnitId,
            Type = InventoryMovementType.Purchase, Quantity = 5, UnitCost = 20,
            ReferenceType = LedgerReferenceTypes.PurchaseInvoice, ReferenceId = invoice.Id
        };
        var consumedMovement = new InventoryMovement
        {
            WorkingDayId = opened.Summary.WorkingDayId, ItemId = flour.Id, UnitId = flour.BaseUnitId,
            Type = InventoryMovementType.ProductionConsume, Quantity = -5, UnitCost = 20,
            ReferenceType = "DependentConsumption", ReferenceId = 1
        };
        var ledger = new PartyLedgerEntry
        {
            WorkingDayId = opened.Summary.WorkingDayId, PartyId = supplier.Id,
            Credit = 100, Amount = 100, Description = "فاتورة اختبار التراجع الفاشل",
            ReferenceType = LedgerReferenceTypes.PurchaseInvoice, ReferenceId = invoice.Id
        };
        db.AddRange(originalMovement, consumedMovement, ledger);
        await db.SaveChangesAsync();

        var result = await purchases.CancelAsync(invoice.Id, "محاولة إلغاء يجب أن تفشل لنقص المخزون");
        result.Succeeded.Should().BeFalse();
        result.ErrorMessage.Should().Contain("لا يمكن إلغاء");

        db.ChangeTracker.Clear();
        (await db.PurchaseInvoices.SingleAsync(item => item.Id == invoice.Id)).Status.Should().Be(InvoiceStatus.Posted);
        (await db.InventoryMovements.SingleAsync(item => item.Id == originalMovement.Id)).IsReversed.Should().BeFalse();
        (await db.PartyLedgerEntries.SingleAsync(item => item.Id == ledger.Id)).IsReversed.Should().BeFalse();
        (await db.InventoryMovements.CountAsync(item => item.ReversalReferenceId == originalMovement.Id)).Should().Be(0);
        (await db.PartyLedgerEntries.CountAsync(item => item.ReversalReferenceId == ledger.Id)).Should().Be(0);
        (await db.AuditLogs.CountAsync(item => item.Action == AuditActionKeys.PurchaseInvoiceCancelled && item.EntityId == invoice.Id)).Should().Be(0);
    }
}
