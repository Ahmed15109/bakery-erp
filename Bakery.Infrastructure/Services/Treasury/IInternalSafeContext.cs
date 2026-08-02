using Bakery.Application.DTOs.Accounting;
using Bakery.Application.Interfaces;

namespace Bakery.Infrastructure.Services;

internal interface IInternalSafeContext : ISafeContext
{
    void ConfigureSafe(SafeDto safe);
    void Clear();
}
