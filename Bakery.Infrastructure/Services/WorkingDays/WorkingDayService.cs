using Bakery.Application.DTOs;
using Bakery.Application.Interfaces;
using Bakery.Application.Security;
using Bakery.Domain.Constants;
using Bakery.Domain.Entities;
using Bakery.Domain.Enums;
using Bakery.Infrastructure.Data;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Bakery.Shared.Helpers;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;
using System.Text.Json;
using System.Data;

namespace Bakery.Infrastructure.Services;

public sealed class WorkingDayService : IWorkingDayService
{
    private static readonly string[] OperationalDayPermissions =
    [
        PermissionKeys.WorkingDayView, PermissionKeys.WorkingDayOpen,
        PermissionKeys.WorkingDayClose, PermissionKeys.WorkingDayReopen,
        PermissionKeys.SalesView, PermissionKeys.SalesCreate, PermissionKeys.SalesEdit,
        PermissionKeys.SalesPrint, PermissionKeys.PurchasesView, PermissionKeys.PurchasesCreate,
        PermissionKeys.PurchasesEdit, PermissionKeys.PurchasesPrint,
        PermissionKeys.ProductionView, PermissionKeys.ProductionCreate, PermissionKeys.ProductionEdit,
        PermissionKeys.ProductionWaste, PermissionKeys.InventoryView,
        PermissionKeys.InventoryStockAdjustments, PermissionKeys.InventoryCount,
        PermissionKeys.TreasuryView, PermissionKeys.TreasuryCashIn, PermissionKeys.TreasuryCashOut,
        PermissionKeys.TreasuryTransfer, PermissionKeys.CashDeposit, PermissionKeys.CashWithdraw,
        PermissionKeys.CashReverseManualTransaction, PermissionKeys.EmployeesViewSalary,
        PermissionKeys.EmployeesManagePayroll,
        PermissionKeys.EmployeesAdvances, PermissionKeys.ReportsSales,
        PermissionKeys.ReportsInventory, PermissionKeys.ReportsFinancial,
        PermissionKeys.ReportsProduction, PermissionKeys.ReportsPrint
    ];

    private readonly BakeryDbContext _dbContext;
    private readonly IUserSessionService _userSessionService;
    private readonly IPermissionService _permissionService;
    private readonly IAuditService _auditService;
    private readonly ISystemSafeService _systemSafeService;
    private readonly ILogger<WorkingDayService> _logger;
    private readonly IValidator<OpenWorkingDayRequest> _openValidator;
    private readonly IValidator<CloseWorkingDayRequest> _closeValidator;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IBackupQueueService? _backupQueueService;
    private readonly IItemUnitConversionService _unitConversionService;

    public WorkingDayService(
        BakeryDbContext dbContext,
        IUserSessionService userSessionService,
        IPermissionService permissionService,
        IAuditService auditService,
        ISystemSafeService systemSafeService,
        ILogger<WorkingDayService> logger,
        IValidator<OpenWorkingDayRequest> openValidator,
        IValidator<CloseWorkingDayRequest> closeValidator,
        IServiceScopeFactory scopeFactory,
        IItemUnitConversionService unitConversionService,
        IBackupQueueService? backupQueueService = null)
    {
        _dbContext = dbContext;
        _userSessionService = userSessionService;
        _permissionService = permissionService;
        _auditService = auditService;
        _systemSafeService = systemSafeService;
        _logger = logger;
        _openValidator = openValidator;
        _closeValidator = closeValidator;
        _scopeFactory = scopeFactory;
        _unitConversionService = unitConversionService;
        _backupQueueService = backupQueueService;
    }

    public Task<WorkingDay?> GetCurrentOpenDayAsync(CancellationToken cancellationToken = default)
    {
        _permissionService.EnsureAnyPermission(OperationalDayPermissions);
        return GetCurrentOpenDayCoreAsync(cancellationToken);
    }

    private Task<WorkingDay?> GetCurrentOpenDayCoreAsync(CancellationToken cancellationToken = default)
    {
        DetachTrackedWorkingDays();
        return _dbContext.WorkingDays.FirstOrDefaultAsync(day => day.Status == WorkingDayStatus.Open, cancellationToken);
    }

    private void DetachTrackedWorkingDays()
    {
        var entries = _dbContext.ChangeTracker.Entries<WorkingDay>().ToList();
        foreach (var entry in entries)
        {
            entry.State = EntityState.Detached;
        }
    }

    private async Task<decimal> GetPreviousCarryOverAsync(CancellationToken ct)
    {
        var lastDay = await _dbContext.WorkingDays
            .Where(x => x.Status == WorkingDayStatus.Closed)
            .OrderByDescending(x => x.BusinessDate)
            .ThenByDescending(x => x.Id)
            .FirstOrDefaultAsync(ct);
            
        return lastDay?.CarryOverBalance ?? 0;
    }

    private Task<Safe> GetDailySafeAsync(CancellationToken ct)
        => _systemSafeService.GetDailySafeAsync(ct);

    private Task<Safe> GetMainSafeAsync(CancellationToken ct)
        => _systemSafeService.GetMainSafeAsync(ct);

    public async Task<WorkingDayResult> OpenDayAsync(OpenWorkingDayRequest request, CancellationToken cancellationToken = default)
    {
        if (!_permissionService.HasPermission(PermissionKeys.WorkingDayOpen))
        {
            return new WorkingDayResult(false, Loc.ErrAdminRequired);
        }

        var validationResult = await _openValidator.ValidateAsync(request, cancellationToken);
        if (!validationResult.IsValid)
        {
            return new WorkingDayResult(false, validationResult.Errors[0].ErrorMessage);
        }

        using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            if (await GetCurrentOpenDayCoreAsync(cancellationToken) is not null)
            {
                return new WorkingDayResult(false, Loc.ErrOnlyOneOpenDay);
            }

            decimal openingBalance = await GetPreviousCarryOverAsync(cancellationToken);
            
            var shouldCreateOpeningMovement = openingBalance == 0 && request.OpeningCash > 0;
            if (shouldCreateOpeningMovement)
            {
                openingBalance = request.OpeningCash;
            }

            var day = new WorkingDay
            {
                BusinessDate = request.BusinessDate,
                OpeningCash = openingBalance,
                OpenedAt = DateTime.UtcNow,
                OpenedBy = _userSessionService.CurrentUser?.UserName ?? "system",
                Status = WorkingDayStatus.Open,
                Notes = request.Notes
            };

            _dbContext.WorkingDays.Add(day);
            await _dbContext.SaveChangesAsync(cancellationToken);

            if (shouldCreateOpeningMovement)
            {
                var dailySafe = await GetDailySafeAsync(cancellationToken);
                _dbContext.SafeMovements.Add(new SafeMovement
                {
                    SafeId = dailySafe.Id,
                    Amount = openingBalance,
                    Description = "رصيد افتتاحي يدوي",
                    Type = SafeMovementType.OpeningBalance,
                    WorkingDayId = day.Id
                });

                await _dbContext.SaveChangesAsync(cancellationToken);
            }

            await _auditService.LogAsync(
                AuditActionKeys.WorkingDayOpened,
                nameof(WorkingDay),
                day.Id,
                null,
                JsonSerializer.Serialize(new
                {
                    Operation = "OpenDay",
                    Result = "Succeeded",
                    day.BusinessDate,
                    OpeningCash = openingBalance,
                    Notes = request.Notes
                }),
                cancellationToken);
            var summary = await BuildSummaryAsync(day, cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            return new WorkingDayResult(true, null, summary);
        }
        catch (DbUpdateException ex) when (IsWorkingDayUniqueConstraintViolation(ex))
        {
            await transaction.RollbackAsync(cancellationToken);
            return new WorkingDayResult(false, "هناك يوم عمل مفتوح بالفعل (قيد محمي)");
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync(cancellationToken);
            _logger.LogError(ex, "Failed to open working day");
            return new WorkingDayResult(false, $"فشل فتح اليوم: {Bakery.Application.UserErrorMessages.FromException(ex)}");
        }
    }

    [Obsolete("Use OpenDayAsync from an explicit operator action. This compatibility method will be removed in a future release.")]
    public async Task<WorkingDayResult> AutoOpenIfNeededAsync(CancellationToken ct = default)
    {
        try
        {
            var day = await EnsureActiveWorkingDayAsync(ct);
            return new WorkingDayResult(true, "تم ضمان وجود يوم عمل مفتوح", await BuildSummaryAsync(day, ct));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to auto-open working day");
            return new WorkingDayResult(false, $"فشل في ضمان يوم العمل: {Bakery.Application.UserErrorMessages.FromException(ex)}");
        }
    }

    public async Task<WorkingDay> EnsureActiveWorkingDayAsync(CancellationToken ct = default)
    {
        _permissionService.EnsureAnyPermission(OperationalDayPermissions);
        var current = await GetCurrentOpenDayCoreAsync(ct);
        if (current != null) return current;

        if (!_permissionService.HasPermission(PermissionKeys.WorkingDayOpen))
        {
            throw new UnauthorizedAccessException("ليس لديك صلاحية فتح يوم عمل.");
        }

        using var transaction = await _dbContext.Database.BeginTransactionAsync(ct);
        try
        {
            // Re-check inside transaction to prevent race conditions
            current = await GetCurrentOpenDayCoreAsync(ct);
            if (current != null) return current;

            // Try to find the last day (regardless of status)
            var lastDay = await _dbContext.WorkingDays
                .OrderByDescending(x => x.BusinessDate)
                .ThenByDescending(x => x.Id)
                .FirstOrDefaultAsync(ct);

            DateOnly nextDate;
            if (lastDay == null)
            {
                // Scenario 3: First ever operating day
                nextDate = DateOnly.FromDateTime(DateTime.Today);
            }
            else
            {
                // Scenario 1 & 2: Follow next day after last closed
                nextDate = lastDay.BusinessDate.AddDays(1);
            }

            var day = new WorkingDay
            {
                BusinessDate = nextDate,
                OpeningCash = lastDay?.CarryOverBalance ?? 0,
                OpenedAt = DateTime.UtcNow,
                OpenedBy = _userSessionService.CurrentUser?.UserName ?? "system",
                Status = WorkingDayStatus.Open,
                Notes = "تم الفتح تلقائياً بواسطة النظام (دورة العمل المستمرة)"
            };

            _dbContext.WorkingDays.Add(day);
            await _dbContext.SaveChangesAsync(ct);

            await _auditService.LogAsync(
                AuditActionKeys.WorkingDayAutoOpened,
                nameof(WorkingDay),
                day.Id,
                null,
                JsonSerializer.Serialize(new
                {
                    Operation = "AutoOpenDay",
                    Result = "Succeeded",
                    day.BusinessDate,
                    day.OpeningCash
                }),
                ct);
            await transaction.CommitAsync(ct);

            return day;
        }
        catch (DbUpdateException ex) when (IsWorkingDayUniqueConstraintViolation(ex))
        {
            await transaction.RollbackAsync(ct);
            // If another process opened it, just return it
            return await GetCurrentOpenDayAsync(ct) ?? throw new InvalidOperationException("Concurrency error in day opening.");
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync(ct);
            _logger.LogError(ex, "Critical failure in EnsureActiveWorkingDayAsync");
            throw;
        }
    }

    [Obsolete("Use EndCurrentDayAndOpenNextAsync with the reviewed closing summary. This compatibility method will be removed in a future release.")]
    public async Task<WorkingDayResult> SimplifiedCloseAsync(CancellationToken ct = default)
    {
        if (!_permissionService.HasPermission(PermissionKeys.WorkingDayClose))
        {
            return new WorkingDayResult(false, Loc.ErrAdminRequired);
        }

        var current = await GetCurrentOpenDayAsync(ct);
        if (current == null) return new WorkingDayResult(false, "لا يوجد يوم عمل مفتوح");

        var summary = await BuildSummaryAsync(current, ct);
        return await EndCurrentDayAndOpenNextAsync(
            new CloseWorkingDayRequest(
                0,
                summary.DailySafeBalance,
                "إغلاق وردية - ترحيل آلي",
                ExpectedWorkingDayId: current.Id,
                OperationId: Guid.NewGuid()),
            ct);
    }

    public Task<WorkingDayResult> CloseCurrentDayAsync(CloseWorkingDayRequest request, CancellationToken cancellationToken = default)
        => CloseCurrentDayCoreAsync(request, openNextDay: false, cancellationToken);

    public async Task<WorkingDayResult> EndCurrentDayAndOpenNextAsync(
        CloseWorkingDayRequest request,
        CancellationToken cancellationToken = default)
    {
        await using var commandScope = _scopeFactory.CreateAsyncScope();
        var freshService = (WorkingDayService)commandScope.ServiceProvider.GetRequiredService<IWorkingDayService>();
        var result = await freshService.CloseCurrentDayCoreAsync(request, openNextDay: true, cancellationToken);
        if (result.Succeeded)
        {
            DetachTrackedWorkingDays();
        }
        return result;
    }

    public async Task<WorkingDayCloseReadinessDto> GetEndOfDayReadinessAsync(
        CancellationToken cancellationToken = default)
    {
        _permissionService.EnsurePermission(PermissionKeys.WorkingDayClose);
        await using var commandScope = _scopeFactory.CreateAsyncScope();
        var freshService = (WorkingDayService)commandScope.ServiceProvider.GetRequiredService<IWorkingDayService>();
        return await freshService.GetEndOfDayReadinessCoreAsync(cancellationToken);
    }

    private async Task<WorkingDayResult> CloseCurrentDayCoreAsync(
        CloseWorkingDayRequest request,
        bool openNextDay,
        CancellationToken cancellationToken)
    {
        if (!_permissionService.HasPermission(PermissionKeys.WorkingDayClose))
        {
            return new WorkingDayResult(false, Loc.ErrAdminRequired);
        }

        if (request.AdminOverride)
        {
            try
            {
                _permissionService.EnsurePermission(PermissionKeys.WorkingDayOverrideCloseBlockers);
            }
            catch (UnauthorizedAccessException)
            {
                return new WorkingDayResult(
                    false,
                    "ليس لديك صلاحية التجاوز الإداري لموانع إغلاق يوم العمل.");
            }
        }

        var validationResult = await _closeValidator.ValidateAsync(request, cancellationToken);
        if (!validationResult.IsValid)
        {
            var validationBlockers = validationResult.Errors
                .Select((error, index) => new WorkingDayBlockerDto(
                    WorkingDayBlockerKind.Validation,
                    $"REQUEST_VALIDATION_{index + 1}",
                    error.ErrorMessage))
                .ToList();
            return BlockedResult(validationBlockers);
        }

        if (request.AdminOverride && string.IsNullOrWhiteSpace(request.OverrideReason))
        {
            var blocker = new WorkingDayBlockerDto(
                WorkingDayBlockerKind.Validation,
                "MISSING_OVERRIDE_REASON",
                "التجاوز الإداري يتطلب سبباً واضحاً.");
            return BlockedResult([blocker]);
        }

        if (openNextDay && request.ExpectedWorkingDayId is null)
        {
            var blocker = new WorkingDayBlockerDto(
                WorkingDayBlockerKind.Validation,
                "MISSING_EXPECTED_DAY",
                "تعذر تحديد يوم العمل المطلوب إنهاؤه. يرجى تحديث الشاشة والمحاولة مرة أخرى.");
            return BlockedResult([blocker]);
        }

        if (openNextDay && request.OperationId is null)
        {
            var blocker = new WorkingDayBlockerDto(
                WorkingDayBlockerKind.Validation,
                "MISSING_OPERATION_ID",
                "تعذر تحديد طلب إنهاء يوم العمل. يرجى تحديث الشاشة والمحاولة مرة أخرى.");
            return BlockedResult([blocker]);
        }

        var openDay = await _dbContext.WorkingDays
            .AsNoTracking()
            .SingleOrDefaultAsync(day => day.Status == WorkingDayStatus.Open, cancellationToken);
        if (openDay is null)
        {
            var completed = await TryResolveCompletedEndOfDayAsync(request, cancellationToken);
            if (completed is not null) return completed;

            var latestDay = await _dbContext.WorkingDays
                .AsNoTracking()
                .OrderByDescending(x => x.BusinessDate)
                .ThenByDescending(x => x.Id)
                .FirstOrDefaultAsync(cancellationToken);
            return new WorkingDayResult(
                false,
                latestDay?.Status == WorkingDayStatus.Closed
                    ? "يوم العمل مغلق بالفعل."
                    : Loc.ErrNoOpenDay);
        }

        if (request.ExpectedWorkingDayId.HasValue && request.ExpectedWorkingDayId.Value != openDay.Id)
        {
            var completed = await TryResolveCompletedEndOfDayAsync(request, cancellationToken);
            if (completed is not null) return completed;

            return new WorkingDayResult(
                false,
                "تم تغيير يوم العمل النشط منذ فتح شاشة الإنهاء. لم يتم تنفيذ الإغلاق؛ يرجى تحديث الشاشة والمحاولة مرة أخرى.");
        }

        var preflight = await ValidateCloseBlockersAsync(openDay, request, cancellationToken);
        if (preflight.Blockers.Count > 0 && !request.AdminOverride)
        {
            return BlockedResult(preflight.Blockers, preflight.Summary);
        }

        using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            var day = await _dbContext.WorkingDays
                .SingleOrDefaultAsync(candidate => candidate.Id == openDay.Id && candidate.Status == WorkingDayStatus.Open, cancellationToken);
            if (day is null)
            {
                await transaction.RollbackAsync(cancellationToken);
                _dbContext.ChangeTracker.Clear();
                var completed = await TryResolveCompletedEndOfDayAsync(request, cancellationToken);
                if (completed is not null) return completed;

                return new WorkingDayResult(false, "تمت معالجة يوم العمل من جهاز آخر. يرجى تحديث الشاشة والمحاولة مرة أخرى.");
            }

            // Revalidate from the fresh command context inside the transaction.
            // The row-version loaded here remains unchanged until the final save,
            // so concurrent operational postings are detected without creating a
            // false conflict by touching the token early.
            var closeValidation = await ValidateCloseBlockersAsync(day, request, cancellationToken);
            var blockingIssues = closeValidation.Blockers;
            var summary = closeValidation.Summary;
            var expectedCash = summary.ExpectedCash;
            var dailySafeBalance = summary.DailySafeBalance;
            var actualCash = request.TransferredToMainSafe + request.CarryOverBalance;

            if (blockingIssues.Count > 0 && !request.AdminOverride)
            {
                await transaction.RollbackAsync(cancellationToken);
                return BlockedResult(blockingIssues, summary);
            }
            
            if (request.TransferredToMainSafe > 0)
            {
                var dailySafe = await GetDailySafeAsync(cancellationToken);
                var mainSafe = await GetMainSafeAsync(cancellationToken);
                var transferId = Guid.NewGuid();

                _dbContext.SafeMovements.Add(new SafeMovement
                {
                    SafeId = dailySafe.Id,
                    Amount = -request.TransferredToMainSafe,
                    Description = "تحويل نهاية اليوم إلى الخزينة الرئيسية",
                    Type = SafeMovementType.TransferOut,
                    WorkingDayId = day.Id,
                    TransferId = transferId,
                    ReferenceType = LedgerReferenceTypes.WorkingDayClose,
                    ReferenceId = day.Id
                });

                _dbContext.SafeMovements.Add(new SafeMovement
                {
                    SafeId = mainSafe.Id,
                    Amount = request.TransferredToMainSafe,
                    Description = "تحويل نهاية اليوم من خزنة اليوم",
                    Type = SafeMovementType.TransferIn,
                    WorkingDayId = day.Id,
                    TransferId = transferId,
                    ReferenceType = LedgerReferenceTypes.WorkingDayClose,
                    ReferenceId = day.Id
                });
            }

            // Snapshot values
            day.TotalSales = summary.TotalSales;
            day.TotalPurchases = summary.TotalPurchases;
            day.TotalExpenses = summary.Expenses;
            day.TotalWages = summary.Wages;
            day.TotalSafeMovements = summary.SafeTransfers;
            var adjustmentMovements = await _dbContext.InventoryMovements
                .Where(m => m.WorkingDayId == day.Id && m.Type == InventoryMovementType.Adjustment)
                .Select(movement => new { movement.ItemId, movement.UnitId, movement.Quantity })
                .AsNoTracking()
                .ToListAsync(cancellationToken);
            var adjustmentConversions = await _unitConversionService.GetConversionsAsync(
                adjustmentMovements.Select(movement => new ItemUnitKey(movement.ItemId, movement.UnitId)),
                cancellationToken);
            day.TotalInventoryAdjustments = adjustmentMovements.Sum(movement => Math.Abs(
                adjustmentConversions[new ItemUnitKey(movement.ItemId, movement.UnitId)]
                    .ToBaseQuantity(movement.Quantity)));
            day.InvoiceCount = summary.InvoiceCount;
            
            day.ExpectedClosingCash = expectedCash;
            day.ClosingCash = actualCash;
            day.CashDifference = actualCash - expectedCash;
            day.TransferredToMainSafe = request.TransferredToMainSafe;
            day.CarryOverBalance = request.CarryOverBalance;
            
            day.ClosedAt = DateTime.UtcNow;
            day.ClosedBy = _userSessionService.CurrentUser?.UserName ?? "system";
            day.Status = WorkingDayStatus.Closed;
            day.Notes = string.IsNullOrWhiteSpace(request.Notes) ? day.Notes : request.Notes;

            await _dbContext.SaveChangesAsync(cancellationToken);
            await _auditService.LogAsync(
                AuditActionKeys.WorkingDayClosed,
                nameof(WorkingDay),
                day.Id,
                JsonSerializer.Serialize(new { Status = WorkingDayStatus.Open }),
                JsonSerializer.Serialize(new
                {
                    Operation = "CloseDay",
                    Result = "Succeeded",
                    Status = WorkingDayStatus.Closed,
                    ExpectedCash = expectedCash,
                    ActualCash = actualCash,
                    request.TransferredToMainSafe,
                    request.CarryOverBalance,
                    Difference = actualCash - expectedCash,
                    request.OperationId,
                    OverrideReason = request.AdminOverride ? request.OverrideReason?.Trim() : null,
                    BlockingIssues = blockingIssues.Select(blocker => blocker.Message)
                }),
                cancellationToken);

            WorkingDay resultDay = day;
            if (openNextDay)
            {
                resultDay = await OpenNextBusinessDayWithinTransactionAsync(day, cancellationToken);
            }

            await transaction.CommitAsync(cancellationToken);

            var finalSummary = await BuildSummaryAsync(resultDay, cancellationToken);
            if (_backupQueueService is not null)
            {
                try
                {
                    await _backupQueueService.QueueAutomaticBackupAsync(
                        day.BusinessDate,
                        day.Id,
                        request.OperationId ?? Guid.NewGuid(),
                        day.ClosedBy ?? _userSessionService.CurrentUser?.UserName ?? "system",
                        CancellationToken.None);
                }
                catch (Exception backupQueueException)
                {
                    // Closing the day is already committed. Backup scheduling is an
                    // independent best-effort operation and must never alter that result.
                    _logger.LogError(
                        backupQueueException,
                        "Working day {DayId} closed successfully but automatic backup scheduling failed",
                        day.Id);
                }
            }
            return new WorkingDayResult(true, null, finalSummary);
        }
        catch (DbUpdateConcurrencyException ex)
        {
            await transaction.RollbackAsync(cancellationToken);
            _logger.LogWarning(ex, "Concurrency conflict during day close.");
            _dbContext.ChangeTracker.Clear();
            var completed = await TryResolveCompletedEndOfDayAsync(request, cancellationToken);
            if (completed is not null) return completed;

            return new WorkingDayResult(false, "فشل الإغلاق بسبب تعديل متزامن. يرجى التحديث والمحاولة مرة أخرى.");
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync(cancellationToken);
            _logger.LogError(ex, "Error during close day");
            _dbContext.ChangeTracker.Clear();
            var completed = await TryResolveCompletedEndOfDayAsync(request, cancellationToken);
            if (completed is not null) return completed;

            var operation = openNextDay
                ? "إنهاء يوم العمل وفتح اليوم التالي"
                : "إغلاق يوم العمل";
            return new WorkingDayResult(false, $"تعذر {operation}: {Bakery.Application.UserErrorMessages.FromException(ex)}");
        }
    }

    private async Task<WorkingDayCloseReadinessDto> GetEndOfDayReadinessCoreAsync(
        CancellationToken cancellationToken)
    {
        if (!_permissionService.HasPermission(PermissionKeys.WorkingDayClose))
        {
            return new WorkingDayCloseReadinessDto(null, [], Loc.ErrAdminRequired);
        }

        var day = await _dbContext.WorkingDays
            .AsNoTracking()
            .SingleOrDefaultAsync(candidate => candidate.Status == WorkingDayStatus.Open, cancellationToken);
        if (day is null)
        {
            return new WorkingDayCloseReadinessDto(null, [], Loc.ErrNoOpenDay);
        }

        var validation = await ValidateCloseBlockersAsync(day, request: null, cancellationToken);
        return new WorkingDayCloseReadinessDto(validation.Summary, validation.Blockers);
    }

    private async Task<CloseValidationSnapshot> ValidateCloseBlockersAsync(
        WorkingDay day,
        CloseWorkingDayRequest? request,
        CancellationToken cancellationToken)
    {
        var blockers = new List<WorkingDayBlockerDto>();

        var stockCountSessions = await _dbContext.StockCountSessions
            .AsNoTracking()
            .Where(session => !session.IsCompleted)
            .OrderBy(session => session.Id)
            .Select(session => new { session.Id })
            .ToListAsync(cancellationToken);
        blockers.AddRange(stockCountSessions.Select(session => new WorkingDayBlockerDto(
            WorkingDayBlockerKind.StockCount,
            $"STOCK_COUNT_{session.Id}",
            $"توجد جلسة جرد غير مكتملة رقم {session.Id}",
            session.Id,
            session.Id.ToString(),
            "فتح جلسة الجرد")));

        var productionOrders = await _dbContext.ProductionOrders
            .AsNoTracking()
            .Where(order => order.WorkingDayId == day.Id &&
                (order.Status == ProductionStatus.Draft || order.Status == ProductionStatus.InProgress))
            .OrderBy(order => order.Id)
            .Select(order => new { order.Id, order.ProductionNumber })
            .ToListAsync(cancellationToken);
        blockers.AddRange(productionOrders.Select(order => new WorkingDayBlockerDto(
            WorkingDayBlockerKind.ProductionOrder,
            $"PRODUCTION_{order.Id}",
            $"يوجد أمر إنتاج غير مكتمل رقم {DisplayNumber(order.ProductionNumber, order.Id)}",
            order.Id,
            DisplayNumber(order.ProductionNumber, order.Id),
            "فتح أمر الإنتاج")));

        var saleInvoices = await _dbContext.SaleInvoices
            .AsNoTracking()
            .Where(invoice => invoice.WorkingDayId == day.Id && invoice.Status == InvoiceStatus.Draft)
            .OrderBy(invoice => invoice.Id)
            .Select(invoice => new { invoice.Id, invoice.InvoiceNumber })
            .ToListAsync(cancellationToken);
        blockers.AddRange(saleInvoices.Select(invoice => new WorkingDayBlockerDto(
            WorkingDayBlockerKind.SaleInvoice,
            $"SALE_INVOICE_{invoice.Id}",
            $"توجد فاتورة مبيعات غير مكتملة رقم {DisplayNumber(invoice.InvoiceNumber, invoice.Id)}",
            invoice.Id,
            DisplayNumber(invoice.InvoiceNumber, invoice.Id),
            "عرض الفواتير المفتوحة")));

        var purchaseInvoices = await _dbContext.PurchaseInvoices
            .AsNoTracking()
            .Where(invoice => invoice.WorkingDayId == day.Id && invoice.Status == InvoiceStatus.Draft)
            .OrderBy(invoice => invoice.Id)
            .Select(invoice => new { invoice.Id, invoice.InvoiceNumber })
            .ToListAsync(cancellationToken);
        blockers.AddRange(purchaseInvoices.Select(invoice => new WorkingDayBlockerDto(
            WorkingDayBlockerKind.PurchaseInvoice,
            $"PURCHASE_INVOICE_{invoice.Id}",
            $"توجد فاتورة مشتريات مسودة لم يتم ترحيلها:{Environment.NewLine}" +
            $"رقم الفاتورة: {DisplayNumber(invoice.InvoiceNumber, invoice.Id)}{Environment.NewLine}{Environment.NewLine}" +
            "يرجى ترحيل الفاتورة أو حذف المسودة قبل إنهاء يوم العمل.",
            invoice.Id,
            DisplayNumber(invoice.InvoiceNumber, invoice.Id),
            "عرض فواتير المشتريات المسودة")));

        var unbalancedTransfers = await _dbContext.SafeMovements
            .AsNoTracking()
            .Where(movement => movement.WorkingDayId == day.Id && movement.TransferId.HasValue)
            .GroupBy(movement => movement.TransferId!.Value)
            .Select(group => new
            {
                TransferId = group.Key,
                MovementCount = group.Count(),
                NetAmount = group.Sum(movement => movement.Amount)
            })
            .Where(transfer => transfer.MovementCount != 2 || transfer.NetAmount != 0)
            .ToListAsync(cancellationToken);
        blockers.AddRange(unbalancedTransfers.Select(transfer => new WorkingDayBlockerDto(
            WorkingDayBlockerKind.TreasuryMovement,
            $"TREASURY_TRANSFER_{transfer.TransferId:N}",
            $"توجد حركة خزنة معلقة أو غير متوازنة رقم {transfer.TransferId:N}",
            ReferenceNumber: transfer.TransferId.ToString("N"),
            ActionLabel: "عرض الحركات المعلقة")));

        var summary = await BuildSummaryAsync(day, cancellationToken);
        if (summary.DailySafeBalance < 0)
        {
            blockers.Add(new WorkingDayBlockerDto(
                WorkingDayBlockerKind.FinancialIntegrity,
                "NEGATIVE_DAILY_SAFE",
                "رصيد خزنة اليوم سالب"));
        }

        if (Math.Abs(summary.ExpectedCash - summary.DailySafeBalance) >= 0.01m)
        {
            blockers.Add(new WorkingDayBlockerDto(
                WorkingDayBlockerKind.FinancialIntegrity,
                "EXPECTED_CASH_MISMATCH",
                $"رصيد خزنة اليوم ({summary.DailySafeBalance:N2}) لا يطابق الرصيد المتوقع ({summary.ExpectedCash:N2})",
                ActionLabel: "عرض الحركات المعلقة"));
        }

        if (request is not null)
        {
            var actualCash = request.TransferredToMainSafe + request.CarryOverBalance;
            if (Math.Abs(actualCash - summary.DailySafeBalance) >= 0.01m)
            {
                blockers.Add(new WorkingDayBlockerDto(
                    WorkingDayBlockerKind.FinancialIntegrity,
                    "CLOSING_ALLOCATION_MISMATCH",
                    $"إجمالي الترحيل والمتبقي ({actualCash:N2}) لا يساوي رصيد خزنة اليوم ({summary.DailySafeBalance:N2})"));
            }

            if (request.TransferredToMainSafe > summary.DailySafeBalance)
            {
                blockers.Add(new WorkingDayBlockerDto(
                    WorkingDayBlockerKind.FinancialIntegrity,
                    "TRANSFER_EXCEEDS_BALANCE",
                    "المبلغ المرحل أكبر من رصيد خزنة اليوم وسيؤدي إلى رصيد سالب"));
            }
        }

        return new CloseValidationSnapshot(summary, blockers);
    }

    private async Task<WorkingDayResult?> TryResolveCompletedEndOfDayAsync(
        CloseWorkingDayRequest request,
        CancellationToken cancellationToken)
    {
        if (!request.ExpectedWorkingDayId.HasValue || !request.OperationId.HasValue)
            return null;

        var closedDay = await _dbContext.WorkingDays
            .AsNoTracking()
            .SingleOrDefaultAsync(day => day.Id == request.ExpectedWorkingDayId.Value &&
                day.Status == WorkingDayStatus.Closed, cancellationToken);
        if (closedDay is null) return null;

        var auditValues = await _dbContext.AuditLogs
            .AsNoTracking()
            .Where(audit => audit.EntityName == nameof(WorkingDay) &&
                audit.EntityId == closedDay.Id && audit.Action == AuditActionKeys.WorkingDayClosed)
            .Select(audit => audit.NewValues)
            .ToListAsync(cancellationToken);
        if (!auditValues.Any(values => ContainsOperationId(values, request.OperationId.Value)))
            return null;

        var activeDay = await _dbContext.WorkingDays
            .AsNoTracking()
            .SingleOrDefaultAsync(day => day.Status == WorkingDayStatus.Open, cancellationToken);
        if (activeDay is null || activeDay.BusinessDate != closedDay.BusinessDate.AddDays(1))
            return null;

        var summary = await BuildSummaryAsync(activeDay, cancellationToken);
        return new WorkingDayResult(true, null, summary, [], WasAlreadyCompleted: true);
    }

    private static WorkingDayResult BlockedResult(
        IReadOnlyList<WorkingDayBlockerDto> blockers,
        WorkingDaySummaryDto? summary = null)
    {
        var error = "لا يمكن إنهاء يوم العمل للأسباب التالية:" + Environment.NewLine +
            string.Join(Environment.NewLine, blockers.Select(blocker => $"- {blocker.Message}"));
        return new WorkingDayResult(false, error, summary, blockers);
    }

    private static string DisplayNumber(string? number, int id)
        => string.IsNullOrWhiteSpace(number) ? id.ToString() : number;

    private static bool ContainsOperationId(string? auditValues, Guid operationId)
    {
        if (string.IsNullOrWhiteSpace(auditValues)) return false;

        try
        {
            using var json = JsonDocument.Parse(auditValues);
            return json.RootElement.TryGetProperty("OperationId", out var value) &&
                value.ValueKind == JsonValueKind.String &&
                Guid.TryParse(value.GetString(), out var parsed) &&
                parsed == operationId;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private sealed record CloseValidationSnapshot(
        WorkingDaySummaryDto Summary,
        IReadOnlyList<WorkingDayBlockerDto> Blockers);

    private async Task<WorkingDay> OpenNextBusinessDayWithinTransactionAsync(
        WorkingDay closedDay,
        CancellationToken cancellationToken)
    {
        var nextBusinessDate = closedDay.BusinessDate.AddDays(1);
        var existingNextDay = await _dbContext.WorkingDays
            .SingleOrDefaultAsync(day => day.BusinessDate == nextBusinessDate, cancellationToken);

        if (existingNextDay is not null)
        {
            if (existingNextDay.Status == WorkingDayStatus.Open)
                return existingNextDay;

            if (existingNextDay.Status == WorkingDayStatus.Cancelled)
            {
                existingNextDay.Status = WorkingDayStatus.Open;
                existingNextDay.OpeningCash = closedDay.CarryOverBalance;
                existingNextDay.OpenedAt = DateTime.UtcNow;
                existingNextDay.OpenedBy = _userSessionService.CurrentUser?.UserName ?? "system";
                existingNextDay.ClosedAt = null;
                existingNextDay.ClosedBy = null;
                existingNextDay.Notes = $"أعيد فتح اليوم الملغى تلقائياً بعد إنهاء يوم العمل بتاريخ {closedDay.BusinessDate:yyyy-MM-dd}";
                await _dbContext.SaveChangesAsync(cancellationToken);
                await _auditService.LogAsync(
                    AuditActionKeys.WorkingDayOpened,
                    nameof(WorkingDay),
                    existingNextDay.Id,
                    JsonSerializer.Serialize(new { PreviousStatus = WorkingDayStatus.Cancelled.ToString() }),
                    JsonSerializer.Serialize(new
                    {
                        Operation = "ReactivateCancelledSuccessor",
                        PreviousWorkingDayId = closedDay.Id,
                        existingNextDay.BusinessDate,
                        existingNextDay.OpeningCash,
                        NewStatus = WorkingDayStatus.Open.ToString()
                    }),
                    cancellationToken);
                return existingNextDay;
            }

            throw new InvalidOperationException($"يوجد يوم عمل بتاريخ {nextBusinessDate:yyyy-MM-dd} ولكنه غير مفتوح.");
        }

        var nextDay = new WorkingDay
        {
            BusinessDate = nextBusinessDate,
            OpeningCash = closedDay.CarryOverBalance,
            OpenedAt = DateTime.UtcNow,
            OpenedBy = _userSessionService.CurrentUser?.UserName ?? "system",
            Status = WorkingDayStatus.Open,
            Notes = $"تم الفتح تلقائياً بعد إنهاء يوم العمل بتاريخ {closedDay.BusinessDate:yyyy-MM-dd}"
        };

        _dbContext.WorkingDays.Add(nextDay);
        await _dbContext.SaveChangesAsync(cancellationToken);
        await _auditService.LogAsync(
            AuditActionKeys.WorkingDayOpened,
            nameof(WorkingDay),
            nextDay.Id,
            null,
            JsonSerializer.Serialize(new
            {
                Operation = "OpenDay",
                Result = "Succeeded",
                Source = "EndOfDay",
                PreviousWorkingDayId = closedDay.Id,
                nextDay.BusinessDate,
                nextDay.OpeningCash
            }),
            cancellationToken);

        return nextDay;
    }

    public async Task<(bool Match, decimal Difference, string Details)> VerifyTreasuryIntegrityAsync(int dayId, CancellationToken ct = default)
    {
        _permissionService.EnsureAnyPermission(PermissionKeys.WorkingDayClose, PermissionKeys.TreasuryView, PermissionKeys.ReportsFinancial);
        var day = await _dbContext.WorkingDays.FindAsync(new object[] { dayId }, ct);
        if (day == null) return (false, 0, "يوم العمل غير موجود");

        var dailySafe = await GetDailySafeAsync(ct);
        var expected = await CalculateExpectedClosingCashAsync(dayId, ct);
        var actual = await _dbContext.SafeMovements
            .Where(m => m.SafeId == dailySafe.Id)
            .SumAsync(m => (decimal?)m.Amount, ct) ?? 0;
        var diff = actual - expected;
        var match = Math.Abs(diff) < 0.01m;
        var details = $"الرصيد المتوقع: {expected:N2}، رصيد خزنة اليوم: {actual:N2}، الفرق: {diff:N2}";
        
        return (match, diff, details);
    }

    public async Task<WorkingDayReopenEligibilityDto> GetReopenEligibilityAsync(
        CancellationToken cancellationToken = default)
    {
        _permissionService.EnsureAnyPermission(
            PermissionKeys.WorkingDayView,
            PermissionKeys.WorkingDayReopen);

        var currentDay = await _dbContext.WorkingDays
            .AsNoTracking()
            .SingleOrDefaultAsync(day => day.Status == WorkingDayStatus.Open, cancellationToken);
        var lastClosedDay = await _dbContext.WorkingDays
            .AsNoTracking()
            .Where(day => day.Status == WorkingDayStatus.Closed)
            .OrderByDescending(day => day.BusinessDate)
            .ThenByDescending(day => day.Id)
            .FirstOrDefaultAsync(cancellationToken);

        var currentSummary = currentDay is null
            ? null
            : await BuildLifecycleSummaryAsync(currentDay, cancellationToken);
        var lastClosedSummary = lastClosedDay is null
            ? null
            : await BuildLifecycleSummaryAsync(lastClosedDay, cancellationToken);

        if (lastClosedDay is null)
        {
            const string noClosedDay = "لا يوجد يوم عمل مغلق متاح لإعادة الفتح";
            return new WorkingDayReopenEligibilityDto(
                currentSummary,
                null,
                false,
                noClosedDay,
                [noClosedDay]);
        }

        if (!_permissionService.HasPermission(PermissionKeys.WorkingDayReopen))
        {
            const string permissionRequired = "ليست لديك صلاحية إعادة فتح يوم العمل.";
            return new WorkingDayReopenEligibilityDto(
                currentSummary,
                lastClosedSummary,
                false,
                permissionRequired,
                [permissionRequired]);
        }

        var evaluation = await EvaluateReopenEligibilityAsync(lastClosedDay, cancellationToken);
        if (evaluation.BlockingReasons.Count > 0)
        {
            return new WorkingDayReopenEligibilityDto(
                currentSummary,
                lastClosedSummary,
                false,
                $"إعادة الفتح غير متاحة: {string.Join(" • ", evaluation.BlockingReasons)}",
                evaluation.BlockingReasons,
                evaluation.Blockers);
        }

        var availableMessage = evaluation.SuccessorId.HasValue
            ? $"متاح لإعادة الفتح: يوم العمل الحالي {currentDay!.BusinessDate:dd/MM/yyyy} مفتوح تلقائياً ولم تُسجل عليه عمليات."
            : "متاح لإعادة الفتح.";
        return new WorkingDayReopenEligibilityDto(
            currentSummary,
            lastClosedSummary,
            true,
            availableMessage,
            [],
            []);
    }

    public async Task<WorkingDayResult> ReopenDayAsync(int dayId, string reason, CancellationToken ct = default)
    {
        await using var commandScope = _scopeFactory.CreateAsyncScope();
        var freshService = (WorkingDayService)commandScope.ServiceProvider.GetRequiredService<IWorkingDayService>();
        var result = await freshService.ReopenDayCoreAsync(dayId, reason, ct);
        if (result.Succeeded)
        {
            DetachTrackedWorkingDays();
        }
        return result;
    }

    private async Task<WorkingDayResult> ReopenDayCoreAsync(int dayId, string reason, CancellationToken ct)
    {
        if (!_permissionService.HasPermission(PermissionKeys.WorkingDayReopen))
        {
            return new WorkingDayResult(false, "ليس لديك صلاحية إعادة فتح يوم عمل");
        }

        reason = reason?.Trim() ?? string.Empty;
        if (reason.Length == 0)
            return new WorkingDayResult(false, "سبب إعادة فتح يوم العمل مطلوب.");
        if (reason.Length > 500)
            return new WorkingDayResult(false, "سبب إعادة فتح يوم العمل يجب ألا يتجاوز 500 حرف.");
        if (!ContainsArabicLetter(reason))
            return new WorkingDayResult(false, "يجب كتابة سبب إعادة فتح يوم العمل باللغة العربية.");

        await using var transaction = await _dbContext.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        try
        {
            var day = await _dbContext.WorkingDays.FindAsync(new object[] { dayId }, ct);
            if (day == null) return new WorkingDayResult(false, "يوم العمل غير موجود");
            if (day.Status != WorkingDayStatus.Closed)
                return new WorkingDayResult(false, day.Status == WorkingDayStatus.Open ? "اليوم مفتوح بالفعل" : "يمكن إعادة فتح يوم مغلق فقط.");

            // Lock the lifecycle row before inspecting newer days or safe
            // movements so two terminals cannot reopen the same close.
            day.UpdatedAt = DateTime.UtcNow;
            await _dbContext.SaveChangesAsync(ct);

            var eligibility = await EvaluateReopenEligibilityAsync(day, ct);
            if (eligibility.BlockingReasons.Count > 0)
            {
                await transaction.RollbackAsync(ct);
                _dbContext.ChangeTracker.Clear();
                return new WorkingDayResult(
                    false,
                    $"لا يمكن إعادة فتح يوم العمل: {string.Join(" • ", eligibility.BlockingReasons)}");
            }

            var oldCloseState = JsonSerializer.Serialize(new
            {
                day.Status,
                day.ClosedAt,
                day.ClosedBy,
                day.ExpectedClosingCash,
                day.ClosingCash,
                day.CashDifference,
                day.TransferredToMainSafe,
                day.CarryOverBalance
            });

            var closeMovements = await GetActiveCloseMovementsAsync(day.Id, ct);

            if (eligibility.SuccessorId.HasValue)
            {
                var successor = await _dbContext.WorkingDays
                    .SingleOrDefaultAsync(candidate => candidate.Id == eligibility.SuccessorId.Value, ct);
                if (successor is null || successor.Status != WorkingDayStatus.Open)
                {
                    await transaction.RollbackAsync(ct);
                    _dbContext.ChangeTracker.Clear();
                    return new WorkingDayResult(false, "لا يمكن إعادة الفتح لأن حالة يوم العمل التالي تغيرت. يرجى تحديث الشاشة.");
                }

                var discardedAtUtc = DateTime.UtcNow;
                successor.Status = WorkingDayStatus.Cancelled;
                successor.ClosedAt = discardedAtUtc;
                successor.ClosedBy = _userSessionService.CurrentUser?.UserName ?? "system";
                successor.Notes = $"أُلغي لإعادة فتح يوم العمل السابق. السبب: {reason}";
                await _dbContext.SaveChangesAsync(ct);
                await _auditService.LogAsync(
                    AuditActionKeys.WorkingDayEmptySuccessorDiscarded,
                    nameof(WorkingDay),
                    successor.Id,
                    JsonSerializer.Serialize(new
                    {
                        WorkingDayId = successor.Id,
                        successor.BusinessDate,
                        PreviousStatus = WorkingDayStatus.Open.ToString(),
                        successor.OpenedAt,
                        successor.OpenedBy
                    }),
                    JsonSerializer.Serialize(new
                    {
                        Operation = "CancelEmptySuccessorForReopen",
                        Result = "Succeeded",
                        DiscardedWorkingDayId = successor.Id,
                        DiscardedBusinessDate = successor.BusinessDate,
                        successor.BranchId,
                        UserId = _userSessionService.UserId,
                        ReopenedWorkingDayId = day.Id,
                        ReopenedBusinessDate = day.BusinessDate,
                        Reason = reason,
                        PreviousStatus = WorkingDayStatus.Open.ToString(),
                        NewStatus = WorkingDayStatus.Cancelled.ToString(),
                        Timestamp = discardedAtUtc
                    }),
                    ct);
            }

            var reversalTransferId = Guid.NewGuid();
            var reversals = new List<(SafeMovement Original, SafeMovement Reversal)>();
            foreach (var original in closeMovements)
            {
                original.ReferenceType = LedgerReferenceTypes.WorkingDayClose;
                original.ReferenceId ??= day.Id;
                original.IsReversed = true;
                original.ReversedBy = _userSessionService.CurrentUser?.UserName ?? "system";
                original.ReversedAt = DateTime.UtcNow;
                original.ReverseReason = reason;

                var reversal = new SafeMovement
                {
                    SafeId = original.SafeId,
                    Amount = -original.Amount,
                    Description = "عكس ترحيل إغلاق يوم العمل بعد إعادة الفتح",
                    Type = SafeMovementType.Adjustment,
                    WorkingDayId = day.Id,
                    TransferId = reversalTransferId,
                    ReferenceType = LedgerReferenceTypes.WorkingDayReopen,
                    ReferenceId = day.Id,
                    ReversalReferenceId = original.Id,
                    OriginalTransactionId = original.Id,
                    Notes = reason
                };
                _dbContext.SafeMovements.Add(reversal);
                reversals.Add((original, reversal));
            }

            day.Status = WorkingDayStatus.Open;
            day.ClosedAt = null;
            day.ClosedBy = null;
            day.ExpectedClosingCash = null;
            day.ClosingCash = null;
            day.CashDifference = null;
            day.TransferredToMainSafe = 0;
            day.CarryOverBalance = 0;
            var reopenedAtUtc = DateTime.UtcNow;
            
            await _dbContext.SaveChangesAsync(ct);
            foreach (var pair in reversals)
                pair.Original.ReverseTransactionId = pair.Reversal.Id;

            await _dbContext.SaveChangesAsync(ct);
            await _auditService.LogAsync(
                AuditActionKeys.WorkingDayReopened,
                nameof(WorkingDay),
                day.Id,
                oldCloseState,
                JsonSerializer.Serialize(new
                {
                    Operation = "ReopenDay",
                    Result = "Succeeded",
                    WorkingDayId = day.Id,
                    day.BusinessDate,
                    day.BranchId,
                    UserId = _userSessionService.UserId,
                    Reason = reason,
                    PreviousStatus = WorkingDayStatus.Closed.ToString(),
                    NewStatus = WorkingDayStatus.Open.ToString(),
                    Timestamp = reopenedAtUtc,
                    ReversedCloseMovementCount = reversals.Count
                }),
                ct);
            var summary = await BuildSummaryAsync(day, ct);
            await transaction.CommitAsync(ct);

            return new WorkingDayResult(true, "تم إعادة فتح اليوم بنجاح", summary);
        }
        catch (DbUpdateConcurrencyException ex)
        {
            await transaction.RollbackAsync(ct);
            _logger.LogWarning(ex, "Concurrent reopen rejected for working day {DayId}", dayId);
            return new WorkingDayResult(false, "تم تعديل حالة يوم العمل من جهاز آخر. يرجى التحديث قبل إعادة المحاولة.");
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync(ct);
            _logger.LogError(ex, "Failed to reopen day {DayId}", dayId);
            return new WorkingDayResult(false, $"فشل إعادة الفتح: {Bakery.Application.UserErrorMessages.FromException(ex)}");
        }
    }

    public async Task<WorkingDaySummaryDto?> GetCurrentDaySummaryAsync(CancellationToken cancellationToken = default)
    {
        _permissionService.EnsureAnyPermission(
            PermissionKeys.WorkingDayView,
            PermissionKeys.SalesView,
            PermissionKeys.ProductionView,
            PermissionKeys.TreasuryView,
            PermissionKeys.EmployeesViewSalary,
            PermissionKeys.InventoryView,
            PermissionKeys.ReportsSales,
            PermissionKeys.ReportsProduction,
            PermissionKeys.ReportsInventory,
            PermissionKeys.ReportsFinancial);
        var day = await GetCurrentOpenDayAsync(cancellationToken)
            ?? await _dbContext.WorkingDays
                .OrderByDescending(x => x.BusinessDate)
                .ThenByDescending(x => x.Id)
                .FirstOrDefaultAsync(cancellationToken);
        if (day is null) return null;
        var summary = await BuildSummaryAsync(day, cancellationToken);
        var canViewSales = _permissionService.HasAnyPermission(PermissionKeys.SalesView, PermissionKeys.ReportsSales, PermissionKeys.ReportsFinancial);
        var canViewProduction = _permissionService.HasAnyPermission(PermissionKeys.ProductionView, PermissionKeys.ReportsProduction);
        var canViewTreasury = _permissionService.HasAnyPermission(PermissionKeys.TreasuryView, PermissionKeys.ReportsFinancial);
        var canViewPayroll = _permissionService.HasAnyPermission(PermissionKeys.EmployeesViewSalary, PermissionKeys.ReportsFinancial);
        var canViewInventory = _permissionService.HasAnyPermission(PermissionKeys.InventoryView, PermissionKeys.ReportsInventory);
        return summary with
        {
            OpeningCash = canViewTreasury ? summary.OpeningCash : 0,
            TotalSales = canViewSales ? summary.TotalSales : 0,
            TotalPurchases = _permissionService.HasAnyPermission(PermissionKeys.PurchasesView, PermissionKeys.ReportsFinancial) ? summary.TotalPurchases : 0,
            Expenses = canViewTreasury ? summary.Expenses : 0,
            Wages = canViewPayroll ? summary.Wages : 0,
            SafeTransfers = canViewTreasury ? summary.SafeTransfers : 0,
            ExpectedCash = canViewTreasury ? summary.ExpectedCash : 0,
            ActualCash = canViewTreasury ? summary.ActualCash : null,
            CashDifference = canViewTreasury ? summary.CashDifference : null,
            TransferredToMainSafe = canViewTreasury ? summary.TransferredToMainSafe : 0,
            CarryOverBalance = canViewTreasury ? summary.CarryOverBalance : 0,
            ProductionCount = canViewProduction ? summary.ProductionCount : 0,
            TotalIncome = canViewSales ? summary.TotalIncome : 0,
            DailySafeBalance = canViewTreasury ? summary.DailySafeBalance : 0,
            TransactionCount = canViewTreasury ? summary.TransactionCount : 0,
            InvoiceCount = canViewSales ? summary.InvoiceCount : 0,
            WasteCost = canViewProduction ? summary.WasteCost : 0,
            InventoryAdjustmentCount = canViewInventory ? summary.InventoryAdjustmentCount : 0,
            ProductionEfficiency = canViewProduction ? summary.ProductionEfficiency : 0
        };
    }

    public async Task<IReadOnlyList<DashboardTrendPointDto>> GetRecentDashboardTrendAsync(
        int days = 7,
        CancellationToken cancellationToken = default)
    {
        _permissionService.EnsureAnyPermission(
            PermissionKeys.ReportsSales,
            PermissionKeys.ReportsProduction,
            PermissionKeys.SalesView,
            PermissionKeys.ProductionView);
        days = Math.Clamp(days, 1, 31);
        var workingDays = await _dbContext.WorkingDays
            .AsNoTracking()
            .OrderByDescending(day => day.BusinessDate)
            .ThenByDescending(day => day.Id)
            .Take(days)
            .Select(day => new { day.Id, day.BusinessDate })
            .ToListAsync(cancellationToken);
        workingDays.Reverse();
        if (workingDays.Count == 0) return [];

        var dayIds = workingDays.Select(day => day.Id).ToList();
        var salesByDay = new Dictionary<int, decimal>();
        if (_permissionService.HasPermission(PermissionKeys.SalesView) ||
            _permissionService.HasPermission(PermissionKeys.ReportsSales))
        {
            salesByDay = await _dbContext.SaleInvoices
                .AsNoTracking()
                .Where(invoice => dayIds.Contains(invoice.WorkingDayId) && invoice.Status == InvoiceStatus.Posted)
                .GroupBy(invoice => invoice.WorkingDayId)
                .Select(group => new { WorkingDayId = group.Key, Total = group.Sum(invoice => invoice.PaidAmount) })
                .ToDictionaryAsync(item => item.WorkingDayId, item => item.Total, cancellationToken);
        }

        var productionByDay = new Dictionary<int, decimal>();
        if (_permissionService.HasAnyPermission(PermissionKeys.ProductionView, PermissionKeys.ReportsProduction))
        {
            productionByDay = await _dbContext.ProductionProducedItems
                .AsNoTracking()
                .Where(item => dayIds.Contains(item.ProductionOrder.WorkingDayId) &&
                    item.ProductionOrder.Status == ProductionStatus.Completed)
                .GroupBy(item => item.ProductionOrder.WorkingDayId)
                .Select(group => new { WorkingDayId = group.Key, Total = group.Sum(item => item.ActualProducedQty) })
                .ToDictionaryAsync(item => item.WorkingDayId, item => item.Total, cancellationToken);
        }

        return workingDays
            .Select(day => new DashboardTrendPointDto(
                day.BusinessDate,
                salesByDay.GetValueOrDefault(day.Id),
                productionByDay.GetValueOrDefault(day.Id)))
            .ToList();
    }

    public async Task<decimal> CalculateExpectedClosingCashAsync(int workingDayId, CancellationToken cancellationToken = default)
    {
        _permissionService.EnsureAnyPermission(PermissionKeys.WorkingDayClose, PermissionKeys.TreasuryView, PermissionKeys.ReportsFinancial);
        var day = await _dbContext.WorkingDays.FirstAsync(entity => entity.Id == workingDayId, cancellationToken);
        var dailySafe = await GetDailySafeAsync(cancellationToken);
        var netSafeCash = await _dbContext.SafeMovements
            .Where(movement => movement.WorkingDayId == workingDayId &&
                movement.SafeId == dailySafe.Id &&
                movement.Type != SafeMovementType.OpeningBalance &&
                movement.ReferenceType != LedgerReferenceTypes.WorkingDayClose &&
                movement.ReferenceType != LedgerReferenceTypes.WorkingDayReopen)
            .SumAsync(movement => (decimal?)movement.Amount, cancellationToken) ?? 0;

        return day.OpeningCash + netSafeCash;
    }

    public async Task<ClosingReportDto?> GetClosingReportAsync(int dayId, CancellationToken cancellationToken = default)
    {
        _permissionService.EnsurePermission(PermissionKeys.ReportsFinancial);
        var day = await _dbContext.WorkingDays.FindAsync(new object[] { dayId }, cancellationToken);
        if (day == null) return null;

        var summary = await BuildSummaryAsync(day, cancellationToken);

        var rawLines = await _dbContext.SaleInvoiceLines
            .Where(l => l.SaleInvoice.WorkingDayId == dayId && l.SaleInvoice.Status == InvoiceStatus.Posted)
            .Select(l => new
            {
                ItemName = l.Item.Name,
                Quantity = l.Quantity,
                LineTotal = l.LineTotal
            })
            .ToListAsync(cancellationToken);

        var topProducts = rawLines
            .GroupBy(x => x.ItemName)
            .Select(g => new ProductSalesDto(g.Key, g.Sum(x => x.Quantity), g.Sum(x => x.LineTotal)))
            .OrderByDescending(x => x.TotalAmount)
            .Take(10)
            .ToList();

        var settlementsDb = await _dbContext.EmployeeSettlements
            .Where(s => s.Transactions.Any(t => t.WorkingDayId == dayId))
            .Select(s => new
            {
                EmployeeName = s.Employee.Name,
                PaidAmount = s.PaidAmount,
                WageType = s.WageTypeSnapshot
            })
            .ToListAsync(cancellationToken);

        var settlements = settlementsDb
            .Select(s => new EmployeeSettlementSummaryDto(s.EmployeeName, s.PaidAmount, s.WageType.ToString()))
            .ToList();

        var expenses = await _dbContext.Expenses
            .Where(e => e.WorkingDayId == dayId)
            .GroupBy(e => e.Category)
            .Select(g => new ExpenseSummaryDto(g.Key, g.Sum(x => x.Amount)))
            .ToListAsync(cancellationToken);

        var saleCount = await _dbContext.SaleInvoices.CountAsync(i => i.WorkingDayId == dayId, cancellationToken);
        var purchaseCount = await _dbContext.PurchaseInvoices.CountAsync(i => i.WorkingDayId == dayId, cancellationToken);

        return new ClosingReportDto(summary, topProducts, settlements, expenses, saleCount, purchaseCount);
    }

    private async Task<WorkingDaySummaryDto> BuildSummaryAsync(WorkingDay day, CancellationToken cancellationToken)
    {
        var totalSales = await _dbContext.SaleInvoices
            .Where(invoice => invoice.WorkingDayId == day.Id && invoice.Status == InvoiceStatus.Posted)
            .SumAsync(invoice => (decimal?)invoice.PaidAmount, cancellationToken) ?? 0;

        var totalPurchases = await _dbContext.PurchaseInvoices
            .Where(invoice => invoice.WorkingDayId == day.Id && invoice.Status == InvoiceStatus.Posted)
            .SumAsync(invoice => (decimal?)invoice.PaidAmount, cancellationToken) ?? 0;

        var expenses = await _dbContext.Expenses
            .Where(expense => expense.WorkingDayId == day.Id)
            .SumAsync(expense => (decimal?)expense.Amount, cancellationToken) ?? 0;

        var wages = await _dbContext.EmployeeWages
            .Where(wage => wage.WorkingDayId == day.Id)
            .SumAsync(wage => (decimal?)wage.Amount, cancellationToken) ?? 0;

        var safeTransfers = await _dbContext.SafeMovements
            .Where(movement => movement.WorkingDayId == day.Id && (movement.Type == SafeMovementType.TransferIn || movement.Type == SafeMovementType.TransferOut))
            .SumAsync(movement => (decimal?)movement.Amount, cancellationToken) ?? 0;

        var expectedCash = await CalculateExpectedClosingCashAsync(day.Id, cancellationToken);
        var dailySafe = await GetDailySafeAsync(cancellationToken);
        var dailySafeBalance = await _dbContext.SafeMovements
            .Where(movement => movement.SafeId == dailySafe.Id)
            .SumAsync(movement => (decimal?)movement.Amount, cancellationToken) ?? 0;

        var productionCount = await _dbContext.ProductionOrders.CountAsync(
            order => order.WorkingDayId == day.Id && order.Status == ProductionStatus.Completed,
            cancellationToken);

        var expectedProduction = await _dbContext.ProductionProducedItems
            .Where(item => item.ProductionOrder.WorkingDayId == day.Id &&
                item.ProductionOrder.Status == ProductionStatus.Completed)
            .SumAsync(item => (decimal?)item.ExpectedProducedQty, cancellationToken) ?? 0;
        var actualProduction = await _dbContext.ProductionProducedItems
            .Where(item => item.ProductionOrder.WorkingDayId == day.Id &&
                item.ProductionOrder.Status == ProductionStatus.Completed)
            .SumAsync(item => (decimal?)item.ActualProducedQty, cancellationToken) ?? 0;
        var productionEfficiency = expectedProduction <= 0
            ? 0
            : Math.Round(actualProduction / expectedProduction * 100m, 2);

        var wasteCost = await _dbContext.WasteEntries
            .Where(entry => entry.WorkingDayId == day.Id)
            .SumAsync(entry => (decimal?)entry.WasteCost, cancellationToken) ?? 0;

        var inventoryAdjustmentCount = await _dbContext.InventoryMovements.CountAsync(
            movement => movement.WorkingDayId == day.Id && movement.Type == InventoryMovementType.Adjustment,
            cancellationToken);

        var totalIncome = await _dbContext.SafeMovements
            .Where(m => m.WorkingDayId == day.Id && m.Amount > 0 &&
                m.Type != SafeMovementType.OpeningBalance &&
                m.Type != SafeMovementType.SaleCollection &&
                m.Type != SafeMovementType.TransferIn &&
                m.ReferenceType != LedgerReferenceTypes.WorkingDayClose &&
                m.ReferenceType != LedgerReferenceTypes.WorkingDayReopen)
            .SumAsync(m => (decimal?)m.Amount, cancellationToken) ?? 0;

        var transactionCount = await _dbContext.SafeMovements.CountAsync(
            m => m.WorkingDayId == day.Id &&
                m.ReferenceType != LedgerReferenceTypes.WorkingDayClose &&
                m.ReferenceType != LedgerReferenceTypes.WorkingDayReopen,
            cancellationToken);
        var invoiceCount = await _dbContext.SaleInvoices.CountAsync(i => i.WorkingDayId == day.Id, cancellationToken)
            + await _dbContext.PurchaseInvoices.CountAsync(i => i.WorkingDayId == day.Id, cancellationToken);

        var reopenAudits = await _dbContext.AuditLogs
            .Where(a => a.EntityName == nameof(WorkingDay) && a.EntityId == day.Id && a.Action == AuditActionKeys.WorkingDayReopened)
            .OrderBy(a => a.OccurredAt)
            .Select(a => new { a.OccurredAt, a.CreatedBy, a.NewValues })
            .ToListAsync(cancellationToken);
        var latestReopen = reopenAudits.LastOrDefault();
        var reopenReason = latestReopen is null ? null : ExtractReopenReason(latestReopen.NewValues);
        var latestClose = await _dbContext.AuditLogs
            .Where(a => a.EntityName == nameof(WorkingDay) && a.EntityId == day.Id && a.Action == AuditActionKeys.WorkingDayClosed)
            .OrderByDescending(a => a.OccurredAt)
            .Select(a => new { a.OccurredAt, a.CreatedBy })
            .FirstOrDefaultAsync(cancellationToken);

        return new WorkingDaySummaryDto(
            day.Id,
            day.BusinessDate,
            day.Status,
            day.OpeningCash,
            totalSales,
            totalPurchases,
            expenses,
            wages,
            safeTransfers,
            expectedCash,
            day.ClosingCash ?? 0,
            day.CashDifference ?? 0,
            day.TransferredToMainSafe,
            day.CarryOverBalance,
            productionCount,
            totalIncome,
            dailySafeBalance,
            transactionCount,
            invoiceCount,
            latestReopen?.OccurredAt,
            latestReopen?.CreatedBy,
            reopenReason,
            reopenAudits.Count,
            day.OpenedAt,
            day.OpenedBy,
            day.ClosedAt ?? latestClose?.OccurredAt,
            day.ClosedBy ?? latestClose?.CreatedBy,
            wasteCost,
            inventoryAdjustmentCount,
            productionEfficiency);
    }

    private sealed record ReopenEligibilityEvaluation(
        int? SuccessorId,
        IReadOnlyList<string> BlockingReasons,
        IReadOnlyList<WorkingDayReopenBlockerDto> Blockers);

    private async Task<ReopenEligibilityEvaluation> EvaluateReopenEligibilityAsync(
        WorkingDay day,
        CancellationToken cancellationToken)
    {
        var laterDays = await _dbContext.WorkingDays
            .Where(candidate => candidate.BusinessDate > day.BusinessDate)
            .OrderBy(candidate => candidate.BusinessDate)
            .ThenBy(candidate => candidate.Id)
            .ToListAsync(cancellationToken);

        if (laterDays.Count > 1 ||
            (laterDays.Count == 1 && laterDays[0].BusinessDate != day.BusinessDate.AddDays(1)))
        {
            return new ReopenEligibilityEvaluation(
                null,
                ["يوجد أكثر من يوم عمل أحدث من اليوم المغلق أو أن اليوم التالي ليس متتالياً."],
                [CreateLifecycleBlocker("LIFECYCLE_SEQUENCE", "يوجد أكثر من يوم عمل أحدث من اليوم المغلق أو أن اليوم التالي ليس متتالياً.")]);
        }

        WorkingDay? successor = null;
        if (laterDays.Count == 1)
        {
            successor = laterDays[0];
            if (successor.Status != WorkingDayStatus.Open)
            {
                return new ReopenEligibilityEvaluation(
                    successor.Id,
                    ["يوم العمل التالي لم يعد في حالة مفتوحة."],
                    [CreateLifecycleBlocker("LIFECYCLE_SUCCESSOR_STATUS", "يوم العمل التالي لم يعد في حالة مفتوحة.")]);
            }
        }
        else if (await _dbContext.WorkingDays.AnyAsync(
                     candidate => candidate.Id != day.Id && candidate.Status == WorkingDayStatus.Open,
                     cancellationToken))
        {
            return new ReopenEligibilityEvaluation(
                null,
                ["يوجد يوم عمل مفتوح لا يلي اليوم المغلق مباشرة."],
                [CreateLifecycleBlocker("LIFECYCLE_OPEN_DAY", "يوجد يوم عمل مفتوح لا يلي اليوم المغلق مباشرة.")]);
        }

        var blockers = successor is null
            ? new List<WorkingDayReopenBlockerDto>()
            : await GetSuccessorActivityBlockersAsync(successor, cancellationToken);
        var blockingReasons = blockers.Select(item => item.BlockingReason).Distinct().ToList();
        var closeMovements = await GetActiveCloseMovementsAsync(day.Id, cancellationToken);
        if (closeMovements.Count > 0 && Math.Abs(closeMovements.Sum(movement => movement.Amount)) >= 0.01m)
        {
            const string reason = "حركات ترحيل الإغلاق غير متوازنة؛ يجب مراجعة الخزنة أولاً.";
            blockingReasons.Add(reason);
            blockers.Add(CreateLifecycleBlocker("LIFECYCLE_CLOSE_UNBALANCED", reason));
        }
        if (day.TransferredToMainSafe > 0 && closeMovements.Count == 0)
        {
            const string reason = "حركات ترحيل الإغلاق مفقودة؛ يجب مراجعة الخزنة أولاً.";
            blockingReasons.Add(reason);
            blockers.Add(CreateLifecycleBlocker("LIFECYCLE_CLOSE_MISSING", reason));
        }

        return new ReopenEligibilityEvaluation(successor?.Id, blockingReasons, blockers);
    }

    private Task<List<SafeMovement>> GetActiveCloseMovementsAsync(
        int workingDayId,
        CancellationToken cancellationToken)
    {
        return _dbContext.SafeMovements
            .Where(movement => movement.WorkingDayId == workingDayId && !movement.IsReversed &&
                (movement.Type == SafeMovementType.TransferIn || movement.Type == SafeMovementType.TransferOut) &&
                (movement.ReferenceType == LedgerReferenceTypes.WorkingDayClose ||
                 movement.Description == "تحويل نهاية اليوم إلى الخزينة الرئيسية" ||
                 movement.Description == "تحويل نهاية اليوم من خزنة اليوم"))
            .ToListAsync(cancellationToken);
    }

    private async Task<List<WorkingDayReopenBlockerDto>> GetSuccessorActivityBlockersAsync(
        WorkingDay successor,
        CancellationToken cancellationToken)
    {
        const string unsupported = "لا يمكن التراجع عن هذه العملية تلقائياً";
        var blockers = new List<WorkingDayReopenBlockerDto>();
        var workingDayId = successor.Id;

        var sales = await _dbContext.SaleInvoices.AsNoTracking()
            .Include(item => item.Party)
            .Where(item => item.WorkingDayId == workingDayId && item.Status != InvoiceStatus.Cancelled)
            .ToListAsync(cancellationToken);
        foreach (var invoice in sales)
        {
            var isDraft = invoice.Status == InvoiceStatus.Draft;
            var permission = isDraft ? PermissionKeys.SalesDelete : PermissionKeys.SalesCancel;
            blockers.Add(new WorkingDayReopenBlockerDto(
                $"SALE:{invoice.Id}", WorkingDayReopenBlockerKind.SaleInvoice, invoice.Id,
                isDraft ? "مسودة فاتورة بيع" : "فاتورة بيع مرحّلة", invoice.InvoiceNumber,
                $"العميل: {invoice.Party.Name}", invoice.TotalAmount, "المبلغ",
                invoice.CreatedBy ?? "—", invoice.InvoiceDate, InvoiceStatusLabel(invoice.Status),
                $"فاتورة البيع {invoice.InvoiceNumber} مسجلة على يوم العمل الحالي.",
                isDraft ? WorkingDayReopenActionKind.DeleteDraft : WorkingDayReopenActionKind.CancelInvoice,
                isDraft ? "حذف المسودة" : "إلغاء الفاتورة", permission,
                _permissionService.HasPermission(permission),
                isDraft ? "حذف المسودة التي لم تُرحّل دون إنشاء حركات مالية أو مخزنية." :
                    "سيتم عكس المخزون وحركة الخزنة وقيد العميل مع إبقاء الفاتورة وسجل التدقيق.",
                _permissionService.HasPermission(permission) ? null : "ليست لديك صلاحية تنفيذ هذا الإجراء."));
        }

        var purchases = await _dbContext.PurchaseInvoices.AsNoTracking()
            .Include(item => item.Party)
            .Where(item => item.WorkingDayId == workingDayId && item.Status != InvoiceStatus.Cancelled)
            .ToListAsync(cancellationToken);
        foreach (var invoice in purchases)
        {
            var isDraft = invoice.Status == InvoiceStatus.Draft;
            var permission = isDraft ? PermissionKeys.PurchasesDelete : PermissionKeys.PurchasesCancel;
            blockers.Add(new WorkingDayReopenBlockerDto(
                $"PURCHASE:{invoice.Id}", WorkingDayReopenBlockerKind.PurchaseInvoice, invoice.Id,
                isDraft ? "مسودة فاتورة مشتريات" : "فاتورة مشتريات مرحّلة", invoice.InvoiceNumber,
                $"المورد: {invoice.Party.Name}", invoice.TotalAmount, "المبلغ",
                invoice.CreatedBy ?? "—", invoice.InvoiceDate, InvoiceStatusLabel(invoice.Status),
                $"فاتورة المشتريات {invoice.InvoiceNumber} مسجلة على يوم العمل الحالي.",
                isDraft ? WorkingDayReopenActionKind.DeleteDraft : WorkingDayReopenActionKind.CancelInvoice,
                isDraft ? "حذف المسودة" : "إلغاء الفاتورة", permission,
                _permissionService.HasPermission(permission),
                isDraft ? "حذف المسودة التي لم تُرحّل دون إنشاء حركات مالية أو مخزنية." :
                    "سيتم عكس المخزون وحركة الخزنة وقيد المورد مع إبقاء الفاتورة وسجل التدقيق.",
                _permissionService.HasPermission(permission) ? null : "ليست لديك صلاحية تنفيذ هذا الإجراء."));
        }

        var productionOrders = await _dbContext.ProductionOrders.AsNoTracking()
            .Include(item => item.ProducedItems)
            .Where(item => item.WorkingDayId == workingDayId && item.Status != ProductionStatus.Cancelled)
            .ToListAsync(cancellationToken);
        foreach (var order in productionOrders)
        {
            var isDraft = order.Status == ProductionStatus.Draft;
            var isCompleted = order.Status == ProductionStatus.Completed;
            var permission = isDraft ? PermissionKeys.ProductionEdit : PermissionKeys.ProductionCancel;
            var supported = isDraft || isCompleted;
            var canResolve = supported && _permissionService.HasPermission(permission);
            blockers.Add(new WorkingDayReopenBlockerDto(
                $"PRODUCTION:{order.Id}", WorkingDayReopenBlockerKind.ProductionOrder, order.Id,
                "أمر إنتاج", order.ProductionNumber, order.Notes ?? "أمر إنتاج مسجل على يوم العمل الحالي",
                order.ProducedItems.Sum(item => item.ActualProducedQty), "الكمية المنتجة",
                order.CreatedBy ?? "—", order.StartedAt, ProductionStatusLabel(order.Status),
                $"أمر الإنتاج {order.ProductionNumber} مسجل على يوم العمل الحالي.",
                isDraft ? WorkingDayReopenActionKind.DeleteDraft : isCompleted
                    ? WorkingDayReopenActionKind.CancelProduction : WorkingDayReopenActionKind.None,
                isDraft ? "حذف المسودة" : isCompleted ? "التراجع عن العملية" : null,
                supported ? permission : null, canResolve,
                isDraft ? "حذف مسودة أمر الإنتاج التي لم تُرحّل." : isCompleted
                    ? "سيتم عكس المواد المستهلكة والكميات المنتجة والأجور المرتبطة بعد التحقق من المخزون."
                    : "حالة أمر الإنتاج الحالية لا تدعم تراجعاً تلقائياً آمناً.",
                supported ? canResolve ? null : "ليست لديك صلاحية تنفيذ هذا الإجراء." : unsupported));
        }

        var activeSafeMovements = await _dbContext.SafeMovements.AsNoTracking()
            .Include(item => item.Safe)
            .Where(item => item.WorkingDayId == workingDayId && !item.IsReversed &&
                item.ReversalReferenceId == null && item.OriginalTransactionId == null && item.ReversedBy == null)
            .ToListAsync(cancellationToken);
        var paymentMovementIds = activeSafeMovements
            .Where(item => item.ReferenceType == LedgerReferenceTypes.CustomerReceipt ||
                item.ReferenceType == LedgerReferenceTypes.SupplierPayment)
            .Select(item => item.Id)
            .ToArray();
        var linkedPaymentIds = await _dbContext.PartyLedgerEntries.AsNoTracking()
            .Where(item => item.SourceSafeMovementId.HasValue && paymentMovementIds.Contains(item.SourceSafeMovementId.Value))
            .Select(item => item.SourceSafeMovementId!.Value)
            .ToListAsync(cancellationToken);

        foreach (var movement in activeSafeMovements)
        {
            if (movement.ReferenceType is LedgerReferenceTypes.SaleInvoice or LedgerReferenceTypes.PurchaseInvoice or
                LedgerReferenceTypes.SaleCancel or LedgerReferenceTypes.PurchaseCancel or LedgerReferenceTypes.WorkingDayReopen)
                continue;

            var isPartyPayment = movement.ReferenceType is LedgerReferenceTypes.CustomerReceipt or LedgerReferenceTypes.SupplierPayment;
            var isManual = movement.Origin == CashMovementOrigin.Manual;
            var supported = isManual || (isPartyPayment && linkedPaymentIds.Contains(movement.Id));
            var permission = isManual ? PermissionKeys.CashReverseManualTransaction :
                isPartyPayment ? PermissionKeys.TreasuryReversePartyPayment : null;
            var canResolve = supported && permission is not null && _permissionService.HasPermission(permission);
            blockers.Add(new WorkingDayReopenBlockerDto(
                $"SAFE:{movement.Id}", isPartyPayment ? WorkingDayReopenBlockerKind.PartyPayment : WorkingDayReopenBlockerKind.TreasuryTransaction,
                movement.Id, isPartyPayment ? "دفعة عميل/مورد" : "حركة خزينة",
                movement.TransactionNumber ?? $"#{movement.Id}", movement.Description,
                Math.Abs(movement.Amount), "المبلغ", movement.CreatedByUserName ?? movement.CreatedBy ?? "—",
                movement.CreatedAt, "نشطة", "توجد حركات خزينة نشطة على يوم العمل الحالي.",
                supported ? WorkingDayReopenActionKind.ReverseTransaction : WorkingDayReopenActionKind.None,
                supported ? "عكس الحركة" : null, permission, canResolve,
                supported ? "سيتم إنشاء حركة عكسية مرتبطة بالأصل واستعادة رصيد الخزنة والحساب المرتبط." :
                    "لا يوجد مسار تراجع تلقائي مكتمل وآمن لهذا النوع من حركات الخزنة.",
                supported ? canResolve ? null : "ليست لديك صلاحية تنفيذ هذا الإجراء." : unsupported));
        }

        var inventoryMovements = await _dbContext.InventoryMovements.AsNoTracking()
            .Include(item => item.Item)
            .Where(item => item.WorkingDayId == workingDayId && !item.IsReversed && item.ReversalReferenceId == null)
            .Where(item => item.ReferenceType != LedgerReferenceTypes.SaleInvoice &&
                item.ReferenceType != LedgerReferenceTypes.PurchaseInvoice &&
                item.ReferenceType != "ProductionOrder" && item.ReferenceType != "Waste" &&
                item.ReferenceType != LedgerReferenceTypes.SaleCancel &&
                item.ReferenceType != LedgerReferenceTypes.PurchaseCancel &&
                item.ReferenceType != "ProductionCancel" && item.ReferenceType != "StockCount")
            .ToListAsync(cancellationToken);
        foreach (var movement in inventoryMovements)
        {
            blockers.Add(UnsupportedBlocker(
                $"INVENTORY:{movement.Id}", WorkingDayReopenBlockerKind.InventoryAdjustment, movement.Id,
                "حركة تسوية مخزنية", $"#{movement.Id}", movement.Item.Name,
                Math.Abs(movement.Quantity), "الكمية", movement.CreatedBy, movement.CreatedAt,
                "نشطة", "توجد حركة مخزون مستقلة على يوم العمل الحالي.", unsupported));
        }

        var wasteEntries = await _dbContext.WasteEntries.AsNoTracking().Include(item => item.Item)
            .Where(item => item.WorkingDayId == workingDayId).ToListAsync(cancellationToken);
        foreach (var waste in wasteEntries)
            blockers.Add(UnsupportedBlocker($"WASTE:{waste.Id}", WorkingDayReopenBlockerKind.Waste, waste.Id,
                "عملية هالك", $"#{waste.Id}", $"{waste.Item.Name} - {waste.Reason}", waste.Quantity, "الكمية",
                waste.CreatedBy, waste.CreatedAt, "مرحّلة", "توجد عملية هالك على يوم العمل الحالي.", unsupported));

        var expenses = await _dbContext.Expenses.AsNoTracking().Where(item => item.WorkingDayId == workingDayId)
            .ToListAsync(cancellationToken);
        foreach (var expense in expenses)
            blockers.Add(UnsupportedBlocker($"EXPENSE:{expense.Id}", WorkingDayReopenBlockerKind.Expense, expense.Id,
                "مصروف", $"#{expense.Id}", $"{expense.Category} - {expense.Description}", expense.Amount, "المبلغ",
                expense.CreatedBy, expense.CreatedAt, "مرحّل", "يوجد مصروف على يوم العمل الحالي.", unsupported));

        var activeProductionNumbers = productionOrders.Select(item => item.ProductionNumber).ToArray();
        var wages = await _dbContext.EmployeeWages.AsNoTracking().Include(item => item.Employee)
            .Where(item => item.WorkingDayId == workingDayId && !item.IsReversed && item.ReversalReferenceId == null)
            .ToListAsync(cancellationToken);
        foreach (var wage in wages.Where(wage => !activeProductionNumbers.Any(number => wage.Notes != null && wage.Notes.Contains(number))))
            blockers.Add(UnsupportedBlocker($"WAGE:{wage.Id}", WorkingDayReopenBlockerKind.EmployeeWage, wage.Id,
                "استحقاق موظف", $"#{wage.Id}", wage.Employee.Name, wage.Amount, "المبلغ",
                wage.CreatedBy, wage.CreatedAt, "مرحّل", "يوجد استحقاق موظف على يوم العمل الحالي.", unsupported));

        var attendances = await _dbContext.Attendances.AsNoTracking().Include(item => item.Employee)
            .Where(item => item.WorkingDayId == workingDayId).ToListAsync(cancellationToken);
        foreach (var attendance in attendances)
            blockers.Add(UnsupportedBlocker($"ATTENDANCE:{attendance.Id}", WorkingDayReopenBlockerKind.Attendance, attendance.Id,
                "حضور وانصراف", $"#{attendance.Id}", attendance.Employee.Name, null, null,
                attendance.CreatedBy, attendance.CheckIn, "مسجل", "يوجد سجل حضور على يوم العمل الحالي.", unsupported));

        var employeeTransactions = await _dbContext.EmployeeTransactions.AsNoTracking().Include(item => item.Employee)
            .Where(item => item.WorkingDayId == workingDayId).ToListAsync(cancellationToken);
        foreach (var transaction in employeeTransactions)
            blockers.Add(UnsupportedBlocker($"EMPLOYEE_TX:{transaction.Id}", WorkingDayReopenBlockerKind.EmployeeTransaction, transaction.Id,
                "معاملة موظف", $"#{transaction.Id}", transaction.Employee.Name, transaction.Amount, "المبلغ",
                transaction.CreatedBy, transaction.Date, "مرحّلة", "توجد معاملة موظف على يوم العمل الحالي.", unsupported));

        var stockCounts = await _dbContext.StockCountSessions.AsNoTracking()
            .Where(item => item.StartedAt >= successor.OpenedAt).ToListAsync(cancellationToken);
        foreach (var count in stockCounts)
            blockers.Add(UnsupportedBlocker($"STOCK_COUNT:{count.Id}", WorkingDayReopenBlockerKind.StockCount, count.Id,
                "جلسة جرد", $"#{count.Id}", count.Notes ?? "جلسة جرد مخزني", null, null,
                count.StartedBy, count.StartedAt, count.IsCompleted ? "مكتملة" : "مفتوحة",
                "بدأت جلسة جرد بعد فتح يوم العمل الحالي.", unsupported));

        var payrolls = await _dbContext.PayrollPeriods.AsNoTracking()
            .Where(item => item.CreatedAt >= successor.OpenedAt).ToListAsync(cancellationToken);
        foreach (var payroll in payrolls)
            blockers.Add(UnsupportedBlocker($"PAYROLL:{payroll.Id}", WorkingDayReopenBlockerKind.Payroll, payroll.Id,
                "دورة رواتب", $"#{payroll.Id}", payroll.Notes ?? "دورة رواتب", payroll.TotalNetAmount, "المبلغ",
                payroll.CreatedBy, payroll.CreatedAt, payroll.Status.ToString(), "توجد دورة رواتب أُنشئت بعد فتح يوم العمل الحالي.", unsupported));

        var settlements = await _dbContext.EmployeeSettlements.AsNoTracking().Include(item => item.Employee)
            .Where(item => item.CreatedAt >= successor.OpenedAt).ToListAsync(cancellationToken);
        foreach (var settlement in settlements)
            blockers.Add(UnsupportedBlocker($"SETTLEMENT:{settlement.Id}", WorkingDayReopenBlockerKind.Payroll, settlement.Id,
                "تسوية موظف", $"#{settlement.Id}", settlement.Employee.Name, settlement.NetAmount, "المبلغ",
                settlement.CreatedBy, settlement.CreatedAt, settlement.IsFullyPaid ? "مسددة" : "غير مكتملة",
                "توجد تسوية موظف أُنشئت بعد فتح يوم العمل الحالي.", unsupported));

        var partyLedgers = await _dbContext.PartyLedgerEntries.AsNoTracking().Include(item => item.Party)
            .Where(item => item.WorkingDayId == workingDayId && !item.IsReversed && item.ReversalReferenceId == null &&
                item.ReferenceType != LedgerReferenceTypes.SaleInvoice && item.ReferenceType != LedgerReferenceTypes.PurchaseInvoice &&
                item.ReferenceType != LedgerReferenceTypes.CustomerReceipt && item.ReferenceType != LedgerReferenceTypes.SupplierPayment &&
                item.ReferenceType != "ProductionWages")
            .ToListAsync(cancellationToken);
        foreach (var entry in partyLedgers)
            blockers.Add(UnsupportedBlocker($"PARTY_LEDGER:{entry.Id}", WorkingDayReopenBlockerKind.PartyLedger, entry.Id,
                "قيد حساب طرف", $"#{entry.Id}", $"{entry.Party.Name} - {entry.Description}", Math.Abs(entry.Amount), "المبلغ",
                entry.CreatedBy, entry.EntryDate, "مرحّل", "يوجد قيد حساب طرف مستقل على يوم العمل الحالي.", unsupported));

        return blockers.OrderBy(item => item.Timestamp).ThenBy(item => item.Code).ToList();
    }

    private static WorkingDayReopenBlockerDto UnsupportedBlocker(
        string code, WorkingDayReopenBlockerKind kind, int entityId, string typeLabel,
        string recordNumber, string description, decimal? amount, string? amountLabel,
        string? user, DateTime timestamp, string status, string reason, string unsupportedMessage)
        => new(code, kind, entityId, typeLabel, recordNumber, description, amount, amountLabel,
            user ?? "—", timestamp, status, reason, WorkingDayReopenActionKind.None, null, null,
            false, "يتطلب هذا النوع مراجعة يدوية من الشاشة الأصلية.", unsupportedMessage);

    private static WorkingDayReopenBlockerDto CreateLifecycleBlocker(string code, string reason)
        => UnsupportedBlocker(code, WorkingDayReopenBlockerKind.LifecycleIntegrity, 0,
            "سلامة دورة العمل", "—", reason, null, null, "النظام", DateTime.UtcNow,
            "يتطلب مراجعة", reason, "لا يمكن التراجع عن هذه العملية تلقائياً");

    private static string InvoiceStatusLabel(InvoiceStatus status) => status switch
    {
        InvoiceStatus.Draft => "مسودة",
        InvoiceStatus.Posted => "مرحّلة",
        InvoiceStatus.Cancelled => "ملغاة",
        _ => status.ToString()
    };

    private static string ProductionStatusLabel(ProductionStatus status) => status switch
    {
        ProductionStatus.Draft => "مسودة",
        ProductionStatus.InProgress => "قيد التنفيذ",
        ProductionStatus.Completed => "مكتمل",
        ProductionStatus.Cancelled => "ملغى",
        _ => status.ToString()
    };

    private async Task<WorkingDaySummaryDto> BuildLifecycleSummaryAsync(
        WorkingDay day,
        CancellationToken cancellationToken)
    {
        var latestClose = await _dbContext.AuditLogs
            .Where(audit => audit.EntityName == nameof(WorkingDay) &&
                            audit.EntityId == day.Id &&
                            audit.Action == AuditActionKeys.WorkingDayClosed)
            .OrderByDescending(audit => audit.OccurredAt)
            .Select(audit => new { audit.OccurredAt, audit.CreatedBy })
            .FirstOrDefaultAsync(cancellationToken);

        return new WorkingDaySummaryDto(
            WorkingDayId: day.Id,
            BusinessDate: day.BusinessDate,
            Status: day.Status,
            OpeningCash: day.OpeningCash,
            TotalSales: 0,
            TotalPurchases: 0,
            Expenses: 0,
            Wages: 0,
            SafeTransfers: 0,
            ExpectedCash: day.ExpectedClosingCash ?? 0,
            ActualCash: day.ClosingCash,
            CashDifference: day.CashDifference,
            TransferredToMainSafe: day.TransferredToMainSafe,
            CarryOverBalance: day.CarryOverBalance,
            OpenedAt: day.OpenedAt,
            OpenedBy: day.OpenedBy,
            LastClosedAt: day.ClosedAt ?? latestClose?.OccurredAt,
            LastClosedBy: day.ClosedBy ?? latestClose?.CreatedBy);
    }

    private static bool IsWorkingDayUniqueConstraintViolation(DbUpdateException exception)
    {
        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            if (current.Message.Contains("IX_WorkingDays_BranchId_Status", StringComparison.OrdinalIgnoreCase)
                || current.Message.Contains("IX_WorkingDays_BranchId_BusinessDate", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static string? ExtractReopenReason(string? auditValues)
    {
        if (string.IsNullOrWhiteSpace(auditValues)) return null;

        try
        {
            using var json = JsonDocument.Parse(auditValues);
            return json.RootElement.TryGetProperty("Reason", out var reason)
                ? reason.GetString()
                : null;
        }
        catch (JsonException)
        {
            const string legacyPrefix = "Reason:";
            return auditValues.StartsWith(legacyPrefix, StringComparison.OrdinalIgnoreCase)
                ? auditValues[legacyPrefix.Length..].Trim()
                : auditValues;
        }
    }

    private static bool ContainsArabicLetter(string value)
        => value.Any(character =>
            character is >= '\u0600' and <= '\u06FF' or
                         >= '\u0750' and <= '\u077F' or
                         >= '\u08A0' and <= '\u08FF');
}

