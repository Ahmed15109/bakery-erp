using System;
using Bakery.Application.Interfaces;

namespace Bakery.Infrastructure.Services;

internal static class BranchContextExtensions
{
    /// <summary>
    /// Safely casts a public IBranchContext to its internal mutating contract IInternalBranchContext.
    /// Throws an InvalidOperationException with a descriptive architectural error if the cast fails.
    /// </summary>
    public static IInternalBranchContext AsInternal(this IBranchContext branchContext)
    {
        return branchContext as IInternalBranchContext
            ?? throw new InvalidOperationException("The registered IBranchContext implementation must implement the internal IInternalBranchContext interface.");
    }
}
