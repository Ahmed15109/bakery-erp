using Bakery.Application.DTOs;
using Bakery.Application.Interfaces;

namespace Bakery.Infrastructure.Services;

/// <summary>
/// Internal interface used within the Infrastructure layer to allow configuring and clearing active branch context.
/// Implementations of <see cref="IBranchContext"/> registered in the application DI must also implement this interface,
/// as Infrastructure components will cast the injected read-only interface to this type.
/// </summary>
internal interface IInternalBranchContext : IBranchContext
{
    void ConfigureBranch(BranchDto branch);
    void Clear();
}
