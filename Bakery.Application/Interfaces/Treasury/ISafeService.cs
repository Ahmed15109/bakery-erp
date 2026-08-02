using Bakery.Application.DTOs.Accounting;
using Bakery.Domain.Entities;
using Bakery.Domain.Enums;

namespace Bakery.Application.Interfaces;

public interface ISafeService
{
    Task<Safe> GetDefaultCashSafeAsync(CancellationToken cancellationToken = default);
    Task<int> GetDefaultSafeIdAsync(CancellationToken cancellationToken = default);
    Task<decimal> GetBalanceAsync(int safeId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<SafeMovementDto>> GetMovementsAsync(int safeId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<SafeDto>> ListSafesAsync(CancellationToken cancellationToken = default);
    Task<bool> DepositAsync(int safeId, decimal amount, string description, SafeMovementType type = SafeMovementType.Adjustment, CancellationToken cancellationToken = default);
    Task<bool> WithdrawAsync(int safeId, decimal amount, string description, SafeMovementType type = SafeMovementType.Adjustment, CancellationToken cancellationToken = default);
    Task<bool> TransferAsync(int sourceSafeId, int destinationSafeId, decimal amount, string? notes, string? idempotencyKey = null, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<SafeMovementDto>> GetLedgerAsync(
        int safeId,
        DateTime? startDate = null,
        DateTime? endDate = null,
        int? workingDayId = null,
        SafeMovementType? movementType = null,
        string? search = null,
        CancellationToken cancellationToken = default);
    Task<TreasurySnapshotDto> GetTreasurySnapshotAsync(int treasuryId, CancellationToken cancellationToken = default);
    Task<TreasuryReportDto> GetTreasuryReportAsync(
        int treasuryId,
        DateTime? startDate = null,
        DateTime? endDate = null,
        SafeMovementType? movementType = null,
        string? search = null,
        CancellationToken cancellationToken = default);
    Task ValidateSufficientBalanceAsync(int safeId, decimal amount, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<SafeManagementDto>> ListAllSafesForManagementAsync(CancellationToken cancellationToken = default);
    Task<SafeManagementDto> CreateSafeAsync(CreateSafeRequest request, CancellationToken cancellationToken = default);
    Task<SafeManagementDto> UpdateSafeAsync(UpdateSafeRequest request, CancellationToken cancellationToken = default);
    Task<bool> DeactivateSafeAsync(int safeId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<SafeDto>> ListSafesForDepositAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<SafeDto>> ListSafesForWithdrawAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<SafeDto>> ListSafesForTransferSourceAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<SafeDto>> ListSafesForTransferDestAsync(CancellationToken cancellationToken = default);
    Task<bool> ManualDepositAsync(ManualCashTransactionRequest request, CancellationToken ct = default);
    Task<bool> ManualWithdrawalAsync(ManualCashTransactionRequest request, CancellationToken ct = default);
    Task<bool> ReverseManualTransactionAsync(ReverseTransactionRequest request, CancellationToken ct = default);
}
