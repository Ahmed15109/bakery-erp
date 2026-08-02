using System.Threading.Tasks;
using Bakery.Application.DTOs.Accounting;

namespace Bakery.Application.Interfaces;

public interface ISafeSwitchService
{
    Task SwitchSafeAsync(SafeDto safe);
}
