using Bakery.Application.DTOs;

namespace Bakery.Application.Interfaces;

public interface IFirstRunSetupService
{
    Task<bool> IsSetupRequiredAsync(CancellationToken cancellationToken = default);
    Task<FirstRunSetupResult> CreateInitialAdministratorAsync(
        FirstRunAdminRequest request,
        CancellationToken cancellationToken = default);
}
