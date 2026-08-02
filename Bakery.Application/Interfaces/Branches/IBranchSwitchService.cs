using System.Threading.Tasks;
using Bakery.Application.DTOs;

namespace Bakery.Application.Interfaces;

public interface IBranchSwitchService
{
    Task SwitchBranchAsync(BranchDto branch);
}
