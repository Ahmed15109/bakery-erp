using Bakery.Application.Interfaces;
using Bakery.Domain.Enums;
using Bakery.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Bakery.Infrastructure.Services;

public sealed class BusinessDateService : IBusinessDateService
{
    private readonly BakeryDbContext _dbContext;

    public BusinessDateService(BakeryDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<BusinessDayReference?> GetAsync(
        DateOnly businessDate,
        CancellationToken cancellationToken = default)
    {
        return await _dbContext.WorkingDays
            .AsNoTracking()
            .Where(day => day.BusinessDate == businessDate)
            .Select(day => (BusinessDayReference?)new BusinessDayReference(day.Id, day.BusinessDate))
            .SingleOrDefaultAsync(cancellationToken);
    }

    public async Task<BusinessDayReference?> GetCurrentAsync(
        CancellationToken cancellationToken = default)
    {
        return await _dbContext.WorkingDays
            .AsNoTracking()
            .OrderByDescending(day => day.Status == WorkingDayStatus.Open)
            .ThenByDescending(day => day.BusinessDate)
            .ThenByDescending(day => day.Id)
            .Select(day => (BusinessDayReference?)new BusinessDayReference(day.Id, day.BusinessDate))
            .FirstOrDefaultAsync(cancellationToken);
    }
}
