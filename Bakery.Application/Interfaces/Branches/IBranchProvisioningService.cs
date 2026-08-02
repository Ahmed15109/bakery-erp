using System.Threading;
using System.Threading.Tasks;

namespace Bakery.Application.Interfaces;

public interface IBranchProvisioningService
{
    Task ProvisionBranchAsync(int branchId, CancellationToken cancellationToken = default);
}
