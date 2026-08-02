namespace Bakery.Application.Interfaces;

public readonly record struct BusinessDayReference(int WorkingDayId, DateOnly BusinessDate);

public interface IBusinessDateService
{
    Task<BusinessDayReference?> GetAsync(
        DateOnly businessDate,
        CancellationToken cancellationToken = default);

    Task<BusinessDayReference?> GetCurrentAsync(
        CancellationToken cancellationToken = default);
}
