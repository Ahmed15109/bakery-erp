using Bakery.Application.DTOs.Accounting;

namespace Bakery.Application.Interfaces;

public interface IEmployeeStatementProvider
{
    Task<IReadOnlyList<PartyStatementLineDto>> GetStatementAsync(int employeeId, CancellationToken cancellationToken = default);
}
