using Bakery.Application.DTOs;

namespace Bakery.Application.Interfaces;

public interface IBranchContext
{
    int? CurrentBranchId { get; }
    BranchDto? CurrentBranch { get; }
}
