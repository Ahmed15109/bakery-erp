using Bakery.Application.DTOs;
using Bakery.Domain.Entities;

namespace Bakery.Application.Interfaces;

public interface IWorkingDayService
{
    Task<WorkingDay?> GetCurrentOpenDayAsync(CancellationToken cancellationToken = default);
    Task<WorkingDayResult> OpenDayAsync(OpenWorkingDayRequest request, CancellationToken cancellationToken = default);
    Task<WorkingDayResult> CloseCurrentDayAsync(CloseWorkingDayRequest request, CancellationToken cancellationToken = default);
    Task<WorkingDayResult> EndCurrentDayAndOpenNextAsync(CloseWorkingDayRequest request, CancellationToken cancellationToken = default);
    Task<WorkingDayCloseReadinessDto> GetEndOfDayReadinessAsync(CancellationToken cancellationToken = default);
    [Obsolete("Use OpenDayAsync from an explicit operator action. This compatibility method will be removed in a future release.")]
    Task<WorkingDayResult> AutoOpenIfNeededAsync(CancellationToken cancellationToken = default);
    [Obsolete("Use EndCurrentDayAndOpenNextAsync with the reviewed closing summary. This compatibility method will be removed in a future release.")]
    Task<WorkingDayResult> SimplifiedCloseAsync(CancellationToken cancellationToken = default);
    Task<WorkingDay> EnsureActiveWorkingDayAsync(CancellationToken cancellationToken = default);
    Task<WorkingDaySummaryDto?> GetCurrentDaySummaryAsync(CancellationToken cancellationToken = default);
    Task<WorkingDayReopenEligibilityDto> GetReopenEligibilityAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<DashboardTrendPointDto>> GetRecentDashboardTrendAsync(int days = 7, CancellationToken cancellationToken = default);
    Task<decimal> CalculateExpectedClosingCashAsync(int workingDayId, CancellationToken cancellationToken = default);
    Task<(bool Match, decimal Difference, string Details)> VerifyTreasuryIntegrityAsync(int dayId, CancellationToken ct = default);
    Task<WorkingDayResult> ReopenDayAsync(int dayId, string reason, CancellationToken cancellationToken = default);
    Task<ClosingReportDto?> GetClosingReportAsync(int dayId, CancellationToken cancellationToken = default);
}

public interface IWorkingDayReopenResolutionService
{
    Task<WorkingDayReopenBlockerResolutionResult> ResolveAsync(
        ResolveWorkingDayReopenBlockerRequest request,
        CancellationToken cancellationToken = default);
}
