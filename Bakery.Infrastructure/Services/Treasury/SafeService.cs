using System.Text.Json;
using Bakery.Application.DTOs.Accounting;
using Bakery.Application.Interfaces;
using Bakery.Application.Security;
using Bakery.Domain.Constants;
using Bakery.Domain.Entities;
using Bakery.Domain.Enums;
using Bakery.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Bakery.Shared.Helpers;
using System.ComponentModel.DataAnnotations;
using System.Data;
using Microsoft.EntityFrameworkCore.Storage;

namespace Bakery.Infrastructure.Services;

public sealed class SafeService : ISafeService
{
    private readonly BakeryDbContext _db;
    private readonly IDefaultCashSafeService _defaultCashSafeService;
    private readonly IWorkingDayService _workingDayService;
    private readonly ILogger<SafeService> _logger;
    private readonly IPermissionService _permissionService;
    private readonly ISystemSafeService _systemSafeService;
    private readonly ICurrentUserService _currentUserService;
    private readonly IUserSafePermissionService _userSafePermissionService;
    private readonly IAuditService _auditService;
    private readonly IAttachmentStorageService _attachmentStorageService;

    public SafeService(
        BakeryDbContext db,
        IDefaultCashSafeService defaultCashSafeService,
        IWorkingDayService workingDayService,
        ILogger<SafeService> logger,
        IPermissionService permissionService,
        ISystemSafeService systemSafeService,
        ICurrentUserService currentUserService,
        IUserSafePermissionService userSafePermissionService,
        IAuditService auditService,
        IAttachmentStorageService attachmentStorageService)
    {
        _db = db;
        _defaultCashSafeService = defaultCashSafeService;
        _workingDayService = workingDayService;
        _logger = logger;
        _permissionService = permissionService;
        _systemSafeService = systemSafeService;
        _currentUserService = currentUserService;
        _userSafePermissionService = userSafePermissionService;
        _auditService = auditService;
        _attachmentStorageService = attachmentStorageService;
    }

    public Task<Safe> GetDefaultCashSafeAsync(CancellationToken ct = default)
        => _defaultCashSafeService.GetDefaultCashSafeAsync(ct);

    public async Task<int> GetDefaultSafeIdAsync(CancellationToken ct = default)
    {
        var safe = await GetDefaultCashSafeAsync(ct);
        return safe.Id;
    }

    public async Task<decimal> GetBalanceAsync(int safeId, CancellationToken ct = default)
    {
        _permissionService.EnsureAnyPermission(
            PermissionKeys.TreasuryView, PermissionKeys.TreasuryCashIn,
            PermissionKeys.TreasuryCashOut, PermissionKeys.TreasuryTransfer,
            PermissionKeys.CashDeposit, PermissionKeys.CashWithdraw,
            PermissionKeys.CashReverseManualTransaction);
        var currentUserId = _currentUserService.UserId ?? 0;
        if (!await _userSafePermissionService.CanAccessSafeAsync(currentUserId, safeId, ct))
        {
            return 0;
        }
        if (!await _userSafePermissionService.CanViewBalanceAsync(currentUserId, safeId, ct))
        {
            return 0;
        }

        return await GetActualBalanceAsync(safeId, ct);
    }

    private async Task<decimal> GetActualBalanceAsync(int safeId, CancellationToken ct = default)
        => await _db.SafeMovements
            .Where(x => x.SafeId == safeId)
            .SumAsync(x => (decimal?)x.Amount, ct) ?? 0m;

    public async Task ValidateSufficientBalanceAsync(int safeId, decimal amount, CancellationToken ct = default)
    {
        _permissionService.EnsureAnyPermission(
            PermissionKeys.TreasuryView, PermissionKeys.TreasuryCashOut,
            PermissionKeys.TreasuryTransfer, PermissionKeys.CashWithdraw,
            PermissionKeys.CashReverseManualTransaction,
            PermissionKeys.EmployeesAdvances, PermissionKeys.EmployeesManagePayroll);
        var allowNegative = await _db.AppSettings
            .AnyAsync(s => s.Key == "Treasury.AllowNegativeSafeBalance" && s.Value == "true", ct);

        if (allowNegative) return;

        var currentBalance = await GetActualBalanceAsync(safeId, ct);
        if (currentBalance < amount)
        {
            var safe = await _db.Safes.FindAsync(new object[] { safeId }, ct);
            var safeName = !string.IsNullOrWhiteSpace(safe?.ArabicName) ? safe.ArabicName : (safe?.Name ?? "الخزنة");
            throw new ValidationException(
                $"لا يوجد رصيد كافٍ في خزنة \"{safeName}\".\n\nالرصيد المتاح: {currentBalance:N2} جنيه.\n\nالمطلوب: {amount:N2} جنيه.");
        }
    }

    public async Task<IReadOnlyList<SafeMovementDto>> GetMovementsAsync(int safeId, CancellationToken ct = default)
    {
        if (!_permissionService.HasPermission(PermissionKeys.TreasuryView))
        {
            return Array.Empty<SafeMovementDto>();
        }
        return await GetLedgerAsync(safeId, ct: ct);
    }

    public async Task<IReadOnlyList<SafeDto>> ListSafesAsync(CancellationToken ct = default)
    {
        if (!_permissionService.HasPermission(PermissionKeys.TreasuryView))
        {
            return Array.Empty<SafeDto>();
        }
        var safes = await _db.Safes
            .Include(x => x.Branch)
            .Where(x => x.IsActive)
            .AsNoTracking()
            .ToListAsync(ct);
        var result = new List<SafeDto>();

        var currentUserId = _currentUserService.UserId ?? 0;

        foreach (var safe in safes
                     .OrderByDescending(x => x.Code == "DAILY_CASH_SAFE")
                     .ThenBy(x => x.Name))
        {
            if (await _userSafePermissionService.CanAccessSafeAsync(currentUserId, safe.Id, ct))
            {
                decimal balance = 0;
                if (await _userSafePermissionService.CanViewBalanceAsync(currentUserId, safe.Id, ct))
                {
                    balance = await GetBalanceAsync(safe.Id, ct);
                }
                result.Add(new SafeDto(safe.Id, safe.Name, safe.ArabicName, balance, safe.Type, safe.Branch.Name));
            }
        }

        return result;
    }

    public async Task<IReadOnlyList<SafeMovementDto>> GetLedgerAsync(
        int safeId,
        DateTime? startDate = null,
        DateTime? endDate = null,
        int? workingDayId = null,
        SafeMovementType? movementType = null,
        string? search = null,
        CancellationToken ct = default)
    {
        if (!_permissionService.HasPermission(PermissionKeys.TreasuryView))
        {
            return Array.Empty<SafeMovementDto>();
        }
        var query = _db.SafeMovements
            .Include(x => x.Safe)
            .Where(x => x.SafeId == safeId)
            .AsNoTracking()
            .AsQueryable();
        var currentUserId = _currentUserService.UserId ?? 0;

        var hasViewAllManual = _permissionService.HasPermission(PermissionKeys.CashViewAllTransactions);
        if (!hasViewAllManual)
        {
            query = query.Where(x => x.Origin == CashMovementOrigin.System || x.CreatedByUserId == currentUserId);
        }

        if (!await _userSafePermissionService.CanViewLedgerAsync(currentUserId, safeId, ct))
        {
            return Array.Empty<SafeMovementDto>();
        }
        if (startDate.HasValue)
        {
            query = query.Where(x => x.CreatedAt >= startDate.Value);
        }
        if (endDate.HasValue)
        {
            var endOfDay = endDate.Value.Date.AddDays(1).AddTicks(-1);
            query = query.Where(x => x.CreatedAt <= endOfDay);
        }
        if (workingDayId.HasValue)
        {
            query = query.Where(x => x.WorkingDayId == workingDayId.Value);
        }

        var list = await query.OrderBy(x => x.CreatedAt).ThenBy(x => x.Id).ToListAsync(ct);
        if (list.Count == 0) return [];

        var first = list[0];
        var runningBalance = await _db.SafeMovements
            .AsNoTracking()
            .Where(x => x.SafeId == safeId &&
                (x.CreatedAt < first.CreatedAt || (x.CreatedAt == first.CreatedAt && x.Id < first.Id)))
            .SumAsync(x => (decimal?)x.Amount, ct) ?? 0;

        var balances = new Dictionary<int, decimal>(list.Count);
        foreach (var movement in list)
        {
            runningBalance += movement.Amount;
            balances[movement.Id] = runningBalance;
        }

        if (movementType.HasValue)
        {
            list = list.Where(x => x.Type == movementType.Value).ToList();
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            list = list.Where(x =>
                    (x.TransactionNumber != null && x.TransactionNumber.Contains(term, StringComparison.OrdinalIgnoreCase)) ||
                    x.Description.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                    (x.CreatedByUserName != null && x.CreatedByUserName.Contains(term, StringComparison.OrdinalIgnoreCase)) ||
                    (x.CreatedBy != null && x.CreatedBy.Contains(term, StringComparison.OrdinalIgnoreCase)) ||
                    (x.Notes != null && x.Notes.Contains(term, StringComparison.OrdinalIgnoreCase)))
                .ToList();
        }

        var transferIds = list.Where(x => x.TransferId.HasValue).Select(x => x.TransferId!.Value).Distinct().ToList();
        var counterparts = new Dictionary<Guid, Dictionary<int, string>>();
        if (transferIds.Any())
        {
            var counterpartData = await _db.SafeMovements
                .Where(x => x.SafeId != safeId && transferIds.Contains(x.TransferId!.Value))
                .Select(x => new { x.TransferId, x.SafeId, SafeName = !string.IsNullOrEmpty(x.Safe.ArabicName) ? x.Safe.ArabicName : x.Safe.Name })
                .ToListAsync(ct);

            foreach (var cp in counterpartData)
            {
                if (cp.TransferId.HasValue)
                {
                    if (!counterparts.ContainsKey(cp.TransferId.Value))
                    {
                        counterparts[cp.TransferId.Value] = new Dictionary<int, string>();
                    }
                    counterparts[cp.TransferId.Value][cp.SafeId] = cp.SafeName;
                }
            }
        }

        var movementIdsToQuery = list.Select(x => x.Id)
            .Concat(list.Where(x => x.ReverseTransactionId.HasValue).Select(x => x.ReverseTransactionId!.Value))
            .Concat(list.Where(x => x.OriginalTransactionId.HasValue).Select(x => x.OriginalTransactionId!.Value))
            .Distinct()
            .ToList();

        var txMap = await _db.SafeMovements
            .Where(x => x.SafeId == safeId && movementIdsToQuery.Contains(x.Id))
            .Select(x => new { x.Id, x.TransactionNumber })
            .ToDictionaryAsync(x => x.Id, x => x.TransactionNumber, ct);

        var result = new List<SafeMovementDto>();

        foreach (var movement in list)
        {
            string? counterpartSafeName = null;
            if (movement.TransferId.HasValue && counterparts.TryGetValue(movement.TransferId.Value, out var safeMap))
            {
                var key = safeMap.Keys.FirstOrDefault(k => k != movement.SafeId);
                if (key != 0)
                {
                    counterpartSafeName = safeMap[key];
                }
            }

            string? reversedByTransactionNumber = null;
            if (movement.ReverseTransactionId.HasValue && txMap.TryGetValue(movement.ReverseTransactionId.Value, out var revNum))
            {
                reversedByTransactionNumber = revNum;
            }

            string? originalTransactionNumber = null;
            if (movement.OriginalTransactionId.HasValue && txMap.TryGetValue(movement.OriginalTransactionId.Value, out var origNum))
            {
                originalTransactionNumber = origNum;
            }

            result.Add(new SafeMovementDto(
                movement.Id,
                movement.SafeId,
                movement.CreatedAt,
                !string.IsNullOrEmpty(movement.Safe.ArabicName) ? movement.Safe.ArabicName : movement.Safe.Name,
                movement.Description,
                movement.Type,
                movement.Amount,
                balances[movement.Id],
                movement.ReferenceType,
                movement.ReferenceId,
                movement.Notes,
                movement.TransferId,
                counterpartSafeName,
                movement.Origin,
                movement.TransactionNumber,
                movement.Reason,
                GetReasonText(movement.Reason),
                movement.ReversedBy != null,
                movement.OriginalTransactionId,
                movement.CreatedByUserName ?? movement.CreatedBy,
                movement.ReversedBy,
                movement.ReversedAt,
                movement.ReverseReason,
                movement.BalanceBefore,
                movement.BalanceAfter,
                reversedByTransactionNumber,
                originalTransactionNumber
            ));
        }

        result.Reverse();
        return result;
    }

    public async Task<TreasurySnapshotDto> GetTreasurySnapshotAsync(
        int treasuryId,
        CancellationToken ct = default)
    {
        _permissionService.EnsurePermission(PermissionKeys.TreasuryView);

        var currentUserId = _currentUserService.UserId ?? 0;
        var treasury = await _db.Safes
            .AsNoTracking()
            .Where(safe => safe.Id == treasuryId && safe.IsActive && !safe.IsDeleted)
            .Select(safe => new
            {
                safe.Id,
                safe.Name,
                safe.ArabicName,
                safe.Type,
                BranchName = safe.Branch.Name
            })
            .SingleOrDefaultAsync(ct)
            ?? throw new KeyNotFoundException("الخزينة المحددة غير موجودة أو غير نشطة.");

        if (!await _userSafePermissionService.CanAccessSafeAsync(currentUserId, treasuryId, ct))
        {
            throw new UnauthorizedAccessException(Loc.ErrUnauthorizedSafeAccess);
        }

        var canViewBalance = await _userSafePermissionService.CanViewBalanceAsync(currentUserId, treasuryId, ct);
        var canViewLedger = await _userSafePermissionService.CanViewLedgerAsync(currentUserId, treasuryId, ct);
        var canDeposit = _permissionService.HasPermission(PermissionKeys.CashDeposit) &&
            _permissionService.HasPermission(PermissionKeys.TreasuryCashIn) &&
            await _userSafePermissionService.CanCashInAsync(currentUserId, treasuryId, ct);
        var canWithdraw = _permissionService.HasPermission(PermissionKeys.CashWithdraw) &&
            _permissionService.HasPermission(PermissionKeys.TreasuryCashOut) &&
            await _userSafePermissionService.CanCashOutAsync(currentUserId, treasuryId, ct);
        var canTransfer = _permissionService.HasPermission(PermissionKeys.TreasuryTransfer) &&
            await _userSafePermissionService.CanTransferFromAsync(currentUserId, treasuryId, ct);

        var workingDay = await _db.WorkingDays
            .AsNoTracking()
            .OrderByDescending(day => day.Status == WorkingDayStatus.Open)
            .ThenByDescending(day => day.BusinessDate)
            .ThenByDescending(day => day.Id)
            .Select(day => new
            {
                day.Id,
                day.BusinessDate,
                day.Status,
                day.CarryOverBalance
            })
            .FirstOrDefaultAsync(ct);

        decimal currentBalance = 0;
        decimal todayReceipts = 0;
        decimal todayPayments = 0;
        decimal openingBalance = 0;
        decimal todaySales = 0;
        decimal expectedCash = 0;
        decimal carriedBalance = 0;

        if (canViewBalance)
        {
            var treasuryMovements = _db.SafeMovements
                .AsNoTracking()
                .Where(movement => movement.SafeId == treasuryId);

            currentBalance = await treasuryMovements
                .SumAsync(movement => (decimal?)movement.Amount, ct) ?? 0;

            if (workingDay is not null)
            {
                var dayMovements = treasuryMovements
                    .Where(movement => movement.WorkingDayId == workingDay.Id);
                var dayNet = await dayMovements
                    .SumAsync(movement => (decimal?)movement.Amount, ct) ?? 0;
                var dayOpeningMovements = await dayMovements
                    .Where(movement => movement.Type == SafeMovementType.OpeningBalance)
                    .SumAsync(movement => (decimal?)movement.Amount, ct) ?? 0;

                openingBalance = currentBalance - dayNet + dayOpeningMovements;

                var operationalMovements = dayMovements.Where(movement =>
                    movement.Type != SafeMovementType.OpeningBalance &&
                    movement.ReferenceType != LedgerReferenceTypes.WorkingDayClose &&
                    movement.ReferenceType != LedgerReferenceTypes.WorkingDayReopen);

                todayReceipts = await operationalMovements
                    .Where(movement => movement.Amount > 0)
                    .SumAsync(movement => (decimal?)movement.Amount, ct) ?? 0;
                todayPayments = -(await operationalMovements
                    .Where(movement => movement.Amount < 0)
                    .SumAsync(movement => (decimal?)movement.Amount, ct) ?? 0);
                todaySales = await operationalMovements
                    .Where(movement => movement.Type == SafeMovementType.SaleCollection && movement.Amount > 0)
                    .SumAsync(movement => (decimal?)movement.Amount, ct) ?? 0;
                expectedCash = openingBalance + todayReceipts - todayPayments;
                carriedBalance = treasury.Type == SafeType.Daily ? workingDay.CarryOverBalance : 0;
            }
            else
            {
                openingBalance = currentBalance;
                expectedCash = currentBalance;
            }
        }

        var treasuryName = !string.IsNullOrWhiteSpace(treasury.ArabicName)
            ? treasury.ArabicName
            : treasury.Name;

        return new TreasurySnapshotDto(
            treasury.Id,
            treasuryName,
            treasury.Type,
            treasury.BranchName,
            workingDay?.Id,
            workingDay?.BusinessDate,
            workingDay?.Status,
            currentBalance,
            todayReceipts,
            todayPayments,
            openingBalance,
            todaySales,
            expectedCash,
            carriedBalance,
            canViewBalance,
            canViewLedger,
            canDeposit,
            canWithdraw,
            canTransfer);
    }

    public async Task<TreasuryReportDto> GetTreasuryReportAsync(
        int treasuryId,
        DateTime? startDate = null,
        DateTime? endDate = null,
        SafeMovementType? movementType = null,
        string? search = null,
        CancellationToken ct = default)
    {
        _permissionService.EnsurePermission(PermissionKeys.ReportsFinancial);

        var summary = await GetTreasurySnapshotAsync(treasuryId, ct);
        var movements = await GetLedgerAsync(
            treasuryId,
            startDate,
            endDate,
            movementType: movementType,
            search: search,
            ct: ct);

        if (summary.TreasuryId != treasuryId || movements.Any(movement => movement.TreasuryId != treasuryId))
        {
            throw new InvalidOperationException("تم رفض تقرير يحتوي على بيانات خزينة مختلفة عن الخزينة المحددة.");
        }

        return new TreasuryReportDto(
            treasuryId,
            summary,
            movements,
            startDate,
            endDate,
            movementType,
            search);
    }

    public async Task<bool> DepositAsync(int safeId, decimal amount, string description, SafeMovementType type = SafeMovementType.Adjustment, CancellationToken ct = default)
    {
        _permissionService.EnsurePermission(PermissionKeys.TreasuryCashIn);
        var currentUserId = _currentUserService.UserId ?? 0;
        if (!await _userSafePermissionService.CanAccessSafeAsync(currentUserId, safeId, ct))
        {
            throw new UnauthorizedAccessException(Loc.ErrUnauthorizedSafeAccess);
        }
        if (!await _userSafePermissionService.CanCashInAsync(currentUserId, safeId, ct))
        {
            throw new UnauthorizedAccessException(Loc.ErrUnauthorizedSafeCashIn);
        }

        try
        {
            var workingDay = await _workingDayService.EnsureActiveWorkingDayAsync(ct);
            
            _db.SafeMovements.Add(new SafeMovement
            {
                SafeId = safeId,
                Amount = Math.Abs(amount),
                Description = description,
                Type = type,
                WorkingDayId = workingDay.Id
            });

            return await _db.SaveChangesAsync(ct) > 0;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to deposit into safe {SafeId}", safeId);
            return false;
        }
    }

    public async Task<bool> WithdrawAsync(int safeId, decimal amount, string description, SafeMovementType type = SafeMovementType.Adjustment, CancellationToken ct = default)
    {
        _permissionService.EnsurePermission(PermissionKeys.TreasuryCashOut);
        var currentUserId = _currentUserService.UserId ?? 0;
        if (!await _userSafePermissionService.CanAccessSafeAsync(currentUserId, safeId, ct))
        {
            throw new UnauthorizedAccessException(Loc.ErrUnauthorizedSafeAccess);
        }
        if (!await _userSafePermissionService.CanCashOutAsync(currentUserId, safeId, ct))
        {
            throw new UnauthorizedAccessException(Loc.ErrUnauthorizedSafeCashOut);
        }

        await using var transaction = await BeginSerializableTransactionIfNeededAsync(ct);
        try
        {
            await ValidateSufficientBalanceAsync(safeId, amount, ct);
            
            var workingDay = await _workingDayService.EnsureActiveWorkingDayAsync(ct);

            _db.SafeMovements.Add(new SafeMovement
            {
                SafeId = safeId,
                Amount = -Math.Abs(amount),
                Description = description,
                Type = type,
                WorkingDayId = workingDay.Id
            });

            var success = await _db.SaveChangesAsync(ct) > 0;
            if (transaction is not null) await transaction.CommitAsync(ct);
            return success;
        }
        catch (ValidationException)
        {
            if (transaction is not null) await transaction.RollbackAsync(ct);
            throw;
        }
        catch (Exception ex)
        {
            if (transaction is not null) await transaction.RollbackAsync(ct);
            _logger.LogError(ex, "Failed to withdraw from safe {SafeId}", safeId);
            return false;
        }
    }

    public async Task<bool> TransferAsync(
        int sourceSafeId,
        int destinationSafeId,
        decimal amount,
        string? notes,
        string? idempotencyKey = null,
        CancellationToken ct = default)
    {
        _permissionService.EnsurePermission(PermissionKeys.TreasuryTransfer);
        var operationKey = NormalizeIdempotencyKey(idempotencyKey);
        var currentUserId = _currentUserService.UserId ?? 0;
        if (!await _userSafePermissionService.CanTransferFromAsync(currentUserId, sourceSafeId, ct))
        {
            throw new UnauthorizedAccessException(Loc.ErrUnauthorizedSafeTransferFrom);
        }
        if (!await _userSafePermissionService.CanReceiveTransferAsync(currentUserId, destinationSafeId, ct))
        {
            throw new UnauthorizedAccessException(Loc.ErrUnauthorizedSafeReceiveTransfer);
        }

        await using var transaction = await BeginSerializableTransactionIfNeededAsync(ct);
        try
        {
            if (amount <= 0)
            {
                throw new ValidationException("المبلغ يجب أن يكون أكبر من صفر");
            }
            if (sourceSafeId == destinationSafeId)
            {
                throw new ValidationException("لا يمكن التحويل لنفس الخزنة");
            }

            if (operationKey is not null)
            {
                var existing = await _db.SafeMovements.AsNoTracking()
                    .SingleOrDefaultAsync(item => item.IdempotencyKey == operationKey, ct);
                if (existing is not null)
                {
                    var counterpartMatches = existing.TransferId.HasValue &&
                        await _db.SafeMovements.AsNoTracking().AnyAsync(item =>
                            item.TransferId == existing.TransferId &&
                            item.SafeId == destinationSafeId &&
                            item.Type == SafeMovementType.TransferIn &&
                            item.Amount == Math.Abs(amount), ct);
                    if (existing.SafeId != sourceSafeId ||
                        existing.Type != SafeMovementType.TransferOut ||
                        existing.Amount != -Math.Abs(amount) || !counterpartMatches)
                    {
                        throw new ValidationException("مفتاح العملية مستخدم لعملية مالية مختلفة.");
                    }

                    if (transaction is not null) await transaction.CommitAsync(ct);
                    return true;
                }
            }

            var sourceSafe = await _db.Safes.FindAsync(new object[] { sourceSafeId }, ct);
            var destSafe = await _db.Safes.FindAsync(new object[] { destinationSafeId }, ct);

            if (sourceSafe == null || !sourceSafe.IsActive || destSafe == null || !destSafe.IsActive)
            {
                throw new ValidationException("إحدى الخزائن المحددة غير نشطة أو غير موجودة");
            }

            // Perform strict balance validation: no negative balance allowed for transfers
            var currentBalance = await GetActualBalanceAsync(sourceSafeId, ct);
            if (currentBalance < amount)
            {
                throw new ValidationException($"رصيد الخزنة '{sourceSafe.ArabicName ?? sourceSafe.Name}' غير كافٍ. المتاح: {currentBalance:N2} ج.م");
            }

            var workingDay = await _workingDayService.EnsureActiveWorkingDayAsync(ct);
            var absAmount = Math.Abs(amount);
            var transferId = Guid.NewGuid();

            var sourceSafeDisplayName = !string.IsNullOrEmpty(sourceSafe.ArabicName) ? sourceSafe.ArabicName : sourceSafe.Name;
            var destSafeDisplayName = !string.IsNullOrEmpty(destSafe.ArabicName) ? destSafe.ArabicName : destSafe.Name;

            _db.SafeMovements.Add(new SafeMovement
            {
                SafeId = sourceSafeId,
                Amount = -absAmount,
                Description = $"تحويل إلى {destSafeDisplayName}",
                Type = SafeMovementType.TransferOut,
                WorkingDayId = workingDay.Id,
                TransferId = transferId,
                Notes = notes,
                IdempotencyKey = operationKey
            });

            _db.SafeMovements.Add(new SafeMovement
            {
                SafeId = destinationSafeId,
                Amount = absAmount,
                Description = $"تحويل من {sourceSafeDisplayName}",
                Type = SafeMovementType.TransferIn,
                WorkingDayId = workingDay.Id,
                TransferId = transferId,
                Notes = notes
            });

            await _db.SaveChangesAsync(ct);
            if (transaction is not null) await transaction.CommitAsync(ct);
            return true;
        }
        catch (ValidationException)
        {
            if (transaction is not null) await transaction.RollbackAsync(ct);
            throw;
        }
        catch (Exception ex)
        {
            if (transaction is not null) await transaction.RollbackAsync(ct);
            _logger.LogError(ex, "Failed to transfer from {Source} to {Dest}", sourceSafeId, destinationSafeId);
            return false;
        }
    }

    public async Task<IReadOnlyList<SafeManagementDto>> ListAllSafesForManagementAsync(CancellationToken ct = default)
    {
        if (!_permissionService.HasPermission(PermissionKeys.TreasuryManageSafes))
        {
            return Array.Empty<SafeManagementDto>();
        }
        var safes = await _db.Safes.Where(x => !x.IsDeleted).ToListAsync(ct);
        var result = new List<SafeManagementDto>();
        foreach (var safe in safes.OrderBy(x => x.Name))
        {
            var balance = await GetActualBalanceAsync(safe.Id, ct);
            result.Add(new SafeManagementDto(
                safe.Id,
                safe.Name,
                safe.ArabicName,
                safe.Type,
                safe.IsActive,
                balance
            ));
        }
        return result;
    }

    public async Task<SafeManagementDto> CreateSafeAsync(CreateSafeRequest request, CancellationToken ct = default)
    {
        _permissionService.EnsurePermission(PermissionKeys.TreasuryManageSafes);
        if (string.IsNullOrWhiteSpace(request.ArabicName))
        {
            throw new ValidationException("اسم الخزنة مطلوب");
        }

        var cleanName = request.ArabicName.Trim();
        var duplicateExists = await _db.Safes.AnyAsync(x => !x.IsDeleted && x.IsActive && 
            (x.ArabicName == cleanName || x.Name == cleanName), ct);
        if (duplicateExists)
        {
            throw new ValidationException("خزنة نشطة بنفس الاسم موجودة بالفعل");
        }

        var safe = new Safe
        {
            ArabicName = cleanName,
            Name = cleanName,
            IsActive = true,
            Type = SafeType.Normal
        };

        _db.Safes.Add(safe);
        await _db.SaveChangesAsync(ct);

        return new SafeManagementDto(
            safe.Id,
            safe.Name,
            safe.ArabicName,
            safe.Type,
            safe.IsActive,
            0
        );
    }

    public async Task<SafeManagementDto> UpdateSafeAsync(UpdateSafeRequest request, CancellationToken ct = default)
    {
        _permissionService.EnsurePermission(PermissionKeys.TreasuryManageSafes);
        if (string.IsNullOrWhiteSpace(request.ArabicName))
        {
            throw new ValidationException("اسم الخزنة مطلوب");
        }

        var safe = await _db.Safes.FindAsync(new object[] { request.Id }, ct);
        if (safe == null || safe.IsDeleted)
        {
            throw new ValidationException("الخزنة المحددة غير موجودة");
        }

        if (safe.Type != SafeType.Normal)
        {
            // System safe: renaming allowed, but deactivation is blocked.
            if (!request.IsActive)
            {
                throw new ValidationException("لا يمكن تعديل أو تعطيل خزنة نظام.");
            }
        }

        var cleanName = request.ArabicName.Trim();
        var duplicateExists = await _db.Safes.AnyAsync(x => x.Id != request.Id && !x.IsDeleted && x.IsActive && 
            (x.ArabicName == cleanName || x.Name == cleanName), ct);
        if (duplicateExists)
        {
            throw new ValidationException("خزنة نشطة بنفس الاسم موجودة بالفعل");
        }

        // Deactivation check
        if (safe.IsActive && !request.IsActive)
        {
            var balance = await GetActualBalanceAsync(safe.Id, ct);
            if (balance != 0)
            {
                throw new ValidationException("لا يمكن تعطيل خزنة بها رصيد. قم بتحويل الرصيد أولاً.");
            }
        }

        safe.ArabicName = cleanName;
        safe.Name = cleanName;
        safe.IsActive = request.IsActive;

        await _db.SaveChangesAsync(ct);
        var finalBalance = await GetActualBalanceAsync(safe.Id, ct);

        return new SafeManagementDto(
            safe.Id,
            safe.Name,
            safe.ArabicName,
            safe.Type,
            safe.IsActive,
            finalBalance
        );
    }

    public async Task<bool> DeactivateSafeAsync(int safeId, CancellationToken ct = default)
    {
        _permissionService.EnsurePermission(PermissionKeys.TreasuryManageSafes);
        var safe = await _db.Safes.FindAsync(new object[] { safeId }, ct);
        if (safe == null || safe.IsDeleted)
        {
            throw new ValidationException("الخزنة المحددة غير موجودة");
        }

        if (safe.Type != SafeType.Normal)
        {
            throw new ValidationException("لا يمكن تعديل أو تعطيل خزنة نظام.");
        }

        var balance = await GetActualBalanceAsync(safe.Id, ct);
        if (balance != 0)
        {
            throw new ValidationException("لا يمكن تعطيل خزنة بها رصيد. قم بتحويل الرصيد أولاً.");
        }

        safe.IsActive = false;
        await _db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<IReadOnlyList<SafeDto>> ListSafesForDepositAsync(CancellationToken ct = default)
    {
        if (!_permissionService.HasPermission(PermissionKeys.TreasuryCashIn))
        {
            return Array.Empty<SafeDto>();
        }
        var safes = await ListSafesAsync(ct);
        var result = new List<SafeDto>();
        var currentUserId = _currentUserService.UserId ?? 0;
        foreach (var safe in safes)
        {
            if (await _userSafePermissionService.CanCashInAsync(currentUserId, safe.Id, ct))
            {
                result.Add(safe);
            }
        }
        return result;
    }

    public async Task<IReadOnlyList<SafeDto>> ListSafesForWithdrawAsync(CancellationToken ct = default)
    {
        if (!_permissionService.HasPermission(PermissionKeys.TreasuryCashOut))
        {
            return Array.Empty<SafeDto>();
        }
        var safes = await ListSafesAsync(ct);
        var result = new List<SafeDto>();
        var currentUserId = _currentUserService.UserId ?? 0;
        foreach (var safe in safes)
        {
            if (await _userSafePermissionService.CanCashOutAsync(currentUserId, safe.Id, ct))
            {
                result.Add(safe);
            }
        }
        return result;
    }

    public async Task<IReadOnlyList<SafeDto>> ListSafesForTransferSourceAsync(CancellationToken ct = default)
    {
        if (!_permissionService.HasPermission(PermissionKeys.TreasuryTransfer))
        {
            return Array.Empty<SafeDto>();
        }
        var safes = await ListSafesAsync(ct);
        var result = new List<SafeDto>();
        var currentUserId = _currentUserService.UserId ?? 0;
        foreach (var safe in safes)
        {
            if (await _userSafePermissionService.CanTransferFromAsync(currentUserId, safe.Id, ct))
            {
                result.Add(safe);
            }
        }
        return result;
    }

    public async Task<IReadOnlyList<SafeDto>> ListSafesForTransferDestAsync(CancellationToken ct = default)
    {
        if (!_permissionService.HasPermission(PermissionKeys.TreasuryTransfer))
        {
            return Array.Empty<SafeDto>();
        }
        var safes = await ListSafesAsync(ct);
        var result = new List<SafeDto>();
        var currentUserId = _currentUserService.UserId ?? 0;
        foreach (var safe in safes)
        {
            if (await _userSafePermissionService.CanReceiveTransferAsync(currentUserId, safe.Id, ct))
            {
                result.Add(safe);
            }
        }
        return result;
    }

    private void ValidateManualTransactionRequest(ManualCashTransactionRequest request)
    {
        if (request.Amount <= 0)
        {
            throw new ValidationException("المبلغ يجب أن يكون أكبر من صفر");
        }

        if (request.Reason == ManualMovementReason.Other && string.IsNullOrWhiteSpace(request.Description))
        {
            throw new ValidationException("البيان مطلوب عند اختيار 'أخرى'");
        }
    }

    public async Task<bool> ManualDepositAsync(ManualCashTransactionRequest request, CancellationToken ct = default)
    {
        _permissionService.EnsurePermission(PermissionKeys.CashDeposit);

        var currentUserId = _currentUserService.UserId ?? 0;
        var currentUsername = _currentUserService.Username;

        if (!await _userSafePermissionService.CanAccessSafeAsync(currentUserId, request.SafeId, ct))
        {
            throw new UnauthorizedAccessException(Loc.ErrUnauthorizedSafeAccess);
        }

        if (!await _userSafePermissionService.CanCashInAsync(currentUserId, request.SafeId, ct))
        {
            throw new UnauthorizedAccessException(Loc.ErrUnauthorizedSafeCashIn);
        }

        ValidateManualTransactionRequest(request);
        var operationKey = NormalizeIdempotencyKey(request.IdempotencyKey);

        await using var transaction = await BeginSerializableTransactionIfNeededAsync(ct);

        if (operationKey is not null)
        {
            var existing = await _db.SafeMovements.AsNoTracking()
                .SingleOrDefaultAsync(item => item.IdempotencyKey == operationKey, ct);
            if (existing is not null)
            {
                if (existing.SafeId != request.SafeId ||
                    existing.Origin != CashMovementOrigin.Manual ||
                    existing.Amount != Math.Abs(request.Amount) ||
                    existing.Reason != request.Reason)
                {
                    throw new ValidationException("مفتاح العملية مستخدم لعملية مالية مختلفة.");
                }

                if (transaction is not null) await transaction.CommitAsync(ct);
                return true;
            }
        }

        var workingDay = await _workingDayService.EnsureActiveWorkingDayAsync(ct);

        var balanceBefore = await GetActualBalanceAsync(request.SafeId, ct);
        var balanceAfter = balanceBefore + Math.Abs(request.Amount);

        string? savedAttachmentPath = null;
        if (!string.IsNullOrWhiteSpace(request.AttachmentPath))
        {
            savedAttachmentPath = await _attachmentStorageService.SaveAttachmentAsync(request.AttachmentPath, ct);
        }

        var transactionNumber = await GenerateTransactionNumberAsync("DEP", ct);

        var movement = new SafeMovement
        {
            SafeId = request.SafeId,
            Amount = Math.Abs(request.Amount),
            Description = request.Description,
            Notes = request.ReferenceNumber,
            Type = SafeMovementType.Adjustment,
            WorkingDayId = workingDay.Id,
            Origin = CashMovementOrigin.Manual,
            TransactionNumber = transactionNumber,
            Reason = request.Reason,
            ReferenceNumber = request.ReferenceNumber,
            AttachmentPath = savedAttachmentPath,
            BalanceBefore = balanceBefore,
            BalanceAfter = balanceAfter,
            CreatedByUserId = currentUserId,
            CreatedByUserName = currentUsername,
            IdempotencyKey = operationKey
        };

        _db.SafeMovements.Add(movement);
        var success = await _db.SaveChangesAsync(ct) > 0;

        if (success)
        {
            await LogAuditAsync(AuditActionKeys.ManualDeposit, "SafeMovement", movement.Id,
                JsonSerializer.Serialize(new { User = currentUsername, TransactionNumber = transactionNumber, movement.SafeId, Amount = movement.Amount }), ct);
        }

        if (transaction is not null) await transaction.CommitAsync(ct);
        return success;
    }

    public async Task<bool> ManualWithdrawalAsync(ManualCashTransactionRequest request, CancellationToken ct = default)
    {
        _permissionService.EnsurePermission(PermissionKeys.CashWithdraw);

        var currentUserId = _currentUserService.UserId ?? 0;
        var currentUsername = _currentUserService.Username;

        if (!await _userSafePermissionService.CanAccessSafeAsync(currentUserId, request.SafeId, ct))
        {
            throw new UnauthorizedAccessException(Loc.ErrUnauthorizedSafeAccess);
        }

        if (!await _userSafePermissionService.CanCashOutAsync(currentUserId, request.SafeId, ct))
        {
            throw new UnauthorizedAccessException(Loc.ErrUnauthorizedSafeCashOut);
        }

        ValidateManualTransactionRequest(request);
        var operationKey = NormalizeIdempotencyKey(request.IdempotencyKey);

        await using var transaction = await BeginSerializableTransactionIfNeededAsync(ct);

        if (operationKey is not null)
        {
            var existing = await _db.SafeMovements.AsNoTracking()
                .SingleOrDefaultAsync(item => item.IdempotencyKey == operationKey, ct);
            if (existing is not null)
            {
                if (existing.SafeId != request.SafeId ||
                    existing.Origin != CashMovementOrigin.Manual ||
                    existing.Amount != -Math.Abs(request.Amount) ||
                    existing.Reason != request.Reason)
                {
                    throw new ValidationException("مفتاح العملية مستخدم لعملية مالية مختلفة.");
                }

                if (transaction is not null) await transaction.CommitAsync(ct);
                return true;
            }
        }

        var workingDay = await _workingDayService.EnsureActiveWorkingDayAsync(ct);

        await ValidateSufficientBalanceAsync(request.SafeId, request.Amount, ct);

        var balanceBefore = await GetActualBalanceAsync(request.SafeId, ct);
        var balanceAfter = balanceBefore - Math.Abs(request.Amount);

        string? savedAttachmentPath = null;
        if (!string.IsNullOrWhiteSpace(request.AttachmentPath))
        {
            savedAttachmentPath = await _attachmentStorageService.SaveAttachmentAsync(request.AttachmentPath, ct);
        }

        var transactionNumber = await GenerateTransactionNumberAsync("WDR", ct);

        var movement = new SafeMovement
        {
            SafeId = request.SafeId,
            Amount = -Math.Abs(request.Amount),
            Description = request.Description,
            Notes = request.ReferenceNumber,
            Type = SafeMovementType.Adjustment,
            WorkingDayId = workingDay.Id,
            Origin = CashMovementOrigin.Manual,
            TransactionNumber = transactionNumber,
            Reason = request.Reason,
            ReferenceNumber = request.ReferenceNumber,
            AttachmentPath = savedAttachmentPath,
            BalanceBefore = balanceBefore,
            BalanceAfter = balanceAfter,
            CreatedByUserId = currentUserId,
            CreatedByUserName = currentUsername,
            IdempotencyKey = operationKey
        };

        _db.SafeMovements.Add(movement);
        var success = await _db.SaveChangesAsync(ct) > 0;

        if (success)
        {
            await LogAuditAsync(AuditActionKeys.ManualWithdrawal, "SafeMovement", movement.Id,
                JsonSerializer.Serialize(new { User = currentUsername, TransactionNumber = transactionNumber, movement.SafeId, Amount = movement.Amount }), ct);
        }

        if (transaction is not null) await transaction.CommitAsync(ct);
        return success;
    }

    public async Task<bool> ReverseManualTransactionAsync(ReverseTransactionRequest request, CancellationToken ct = default)
    {
        _permissionService.EnsurePermission(PermissionKeys.CashReverseManualTransaction);
        await using var transaction = await BeginSerializableTransactionIfNeededAsync(ct);

        var workingDay = await _workingDayService.EnsureActiveWorkingDayAsync(ct);

        var original = await _db.SafeMovements
            .Include(x => x.Safe)
            .FirstOrDefaultAsync(x => x.Id == request.OriginalTransactionId, ct);

        if (original == null)
        {
            throw new ValidationException("الحركة الأصلية غير موجودة");
        }

        if (original.Origin != CashMovementOrigin.Manual)
        {
            throw new ValidationException("لا يمكن إلغاء الحركات التلقائية التابعة للنظام من هنا");
        }
        if (original.ReversedBy != null)
        {
            throw new ValidationException("هذه الحركة تم إلغاؤها بالفعل");
        }
        if (original.Origin == CashMovementOrigin.Reverse)
        {
            throw new ValidationException("لا يمكن إلغاء حركة عكسية");
        }
        if (string.IsNullOrWhiteSpace(request.ReverseReason))
        {
            throw new ValidationException("سبب الإلغاء مطلوب");
        }

        if (!original.Safe.IsActive)
        {
            throw new ValidationException("الخزنة الخاصة بهذه الحركة غير نشطة حالياً");
        }

        var currentUserId = _currentUserService.UserId ?? 0;
        var currentUsername = _currentUserService.Username;

        if (!await _userSafePermissionService.CanAccessSafeAsync(currentUserId, original.SafeId, ct))
        {
            throw new UnauthorizedAccessException(Loc.ErrUnauthorizedSafeAccess);
        }

        var isDepositReversal = original.Amount > 0;
        if (isDepositReversal)
        {
            if (!await _userSafePermissionService.CanCashOutAsync(currentUserId, original.SafeId, ct))
            {
                throw new UnauthorizedAccessException(Loc.ErrUnauthorizedSafeCashOut);
            }
            await ValidateSufficientBalanceAsync(original.SafeId, Math.Abs(original.Amount), ct);
        }
        else
        {
            if (!await _userSafePermissionService.CanCashInAsync(currentUserId, original.SafeId, ct))
            {
                throw new UnauthorizedAccessException(Loc.ErrUnauthorizedSafeCashIn);
            }
        }

        var reverseTransactionNumber = await GenerateTransactionNumberAsync("REV", ct);

        original.ReversedAt = DateTime.UtcNow;
        original.ReversedBy = currentUsername;
        original.ReverseReason = request.ReverseReason;

        var balanceBefore = await GetActualBalanceAsync(original.SafeId, ct);
        var balanceAfter = balanceBefore - original.Amount;

        var reverseMovement = new SafeMovement
        {
            SafeId = original.SafeId,
            Amount = -original.Amount,
            Description = $"إلغاء حركة #{original.TransactionNumber} - {request.ReverseReason}",
            Notes = $"حركة عكسية للحركة #{original.TransactionNumber}",
            Type = original.Type,
            WorkingDayId = workingDay.Id,
            Origin = CashMovementOrigin.Reverse,
            OriginalTransactionId = original.Id,
            TransactionNumber = reverseTransactionNumber,
            Reason = original.Reason,
            BalanceBefore = balanceBefore,
            BalanceAfter = balanceAfter,
            CreatedByUserId = currentUserId,
            CreatedByUserName = currentUsername
        };

        try
        {
            _db.SafeMovements.Add(reverseMovement);
            var success = await _db.SaveChangesAsync(ct) > 0;

            if (success)
            {
                original.ReverseTransactionId = reverseMovement.Id;
                await _db.SaveChangesAsync(ct);

                await LogAuditAsync(AuditActionKeys.ManualTransactionReversed, "SafeMovement", original.Id,
                    JsonSerializer.Serialize(new { User = currentUsername, OriginalTransactionNumber = original.TransactionNumber, original.SafeId, OriginalAmount = original.Amount }), ct);
                await LogAuditAsync(AuditActionKeys.ReverseTransactionCreated, "SafeMovement", reverseMovement.Id,
                    JsonSerializer.Serialize(new { User = currentUsername, ReverseTransactionNumber = reverseTransactionNumber, reverseMovement.SafeId, Amount = reverseMovement.Amount, OriginalTransactionId = original.Id }), ct);
            }

            if (transaction is not null) await transaction.CommitAsync(ct);
            return success;
        }
        catch
        {
            if (transaction is not null) await transaction.RollbackAsync(ct);
            throw;
        }
    }

    private async Task<string> GenerateTransactionNumberAsync(string prefix, CancellationToken ct)
    {
        var workingDay = await _workingDayService.EnsureActiveWorkingDayAsync(ct);
        var branchId = workingDay.BranchId;

        const int maxRetries = 10;
        for (int i = 0; i < maxRetries; i++)
        {
            try
            {
                var counter = await _db.TransactionNumberCounters
                    .FirstOrDefaultAsync(x => x.BranchId == branchId && x.Prefix == prefix, ct);

                if (counter == null)
                {
                    counter = new TransactionNumberCounter
                    {
                        BranchId = branchId,
                        Prefix = prefix,
                        LastValue = 0
                    };
                    _db.TransactionNumberCounters.Add(counter);
                    await _db.SaveChangesAsync(ct);
                }
                else if (i > 0)
                {
                    await _db.Entry(counter).ReloadAsync(ct);
                }

                counter.LastValue++;
                await _db.SaveChangesAsync(ct);

                return $"{prefix}-{counter.LastValue:D5}";
            }
            catch (DbUpdateConcurrencyException)
            {
                if (i == maxRetries - 1) throw;
                await Task.Delay(100, ct);
            }
        }

        throw new InvalidOperationException("Failed to generate transaction number due to concurrent updates");
    }

    private static string? NormalizeIdempotencyKey(string? idempotencyKey)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey)) return null;
        var normalized = idempotencyKey.Trim();
        if (normalized.Length > 100)
        {
            throw new ValidationException("مفتاح العملية غير صالح.");
        }
        return normalized;
    }

    private static string? GetReasonText(ManualMovementReason? reason)
    {
        if (reason == null) return null;
        return reason.Value switch
        {
            ManualMovementReason.OwnerCapital => "رأس مال المالك",
            ManualMovementReason.OwnerWithdrawal => "مسحوبات المالك",
            ManualMovementReason.BankDeposit => "إيداع بنكي",
            ManualMovementReason.BankWithdrawal => "سحب بنكي",
            ManualMovementReason.CashAdjustment => "تسوية نقدية",
            ManualMovementReason.TransferCorrection => "تصحيح تحويل",
            ManualMovementReason.Emergency => "حركة نقدية طارئة",
            ManualMovementReason.Other => "أخرى",
            _ => reason.Value.ToString()
        };
    }

    private async Task LogAuditAsync(string action, string entityName, int entityId, string? newValues, CancellationToken ct)
    {
        try
        {
            await _auditService.LogAsync(action, entityName, entityId, null, newValues, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to log manual transaction audit trail");
        }
    }

    private async Task<IDbContextTransaction?> BeginSerializableTransactionIfNeededAsync(CancellationToken ct)
    {
        if (_db.Database.CurrentTransaction is not null)
        {
            return null;
        }

        return await _db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
    }
}
