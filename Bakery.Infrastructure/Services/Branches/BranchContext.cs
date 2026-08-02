using Bakery.Application.DTOs;
using Bakery.Application.Interfaces;

namespace Bakery.Infrastructure.Services;

public sealed class BranchContext : IBranchContext, IInternalBranchContext
{
    public int? CurrentBranchId => CurrentBranch?.Id;
    public BranchDto? CurrentBranch { get; private set; }

    public void ConfigureBranch(BranchDto branch)
    {
        CurrentBranch = branch;
    }

    public void Clear()
    {
        CurrentBranch = null;
    }
}
