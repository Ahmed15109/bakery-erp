using System;
using Bakery.Application.Interfaces;

namespace Bakery.Infrastructure.Services;

internal static class SafeContextExtensions
{
    /// <summary>
    /// Safely casts a public ISafeContext to its internal mutating contract IInternalSafeContext.
    /// Throws an InvalidOperationException with a descriptive architectural error if the cast fails.
    /// </summary>
    public static IInternalSafeContext AsInternal(this ISafeContext safeContext)
    {
        return safeContext as IInternalSafeContext
            ?? throw new InvalidOperationException("The registered ISafeContext implementation must implement the internal IInternalSafeContext interface.");
    }
}
