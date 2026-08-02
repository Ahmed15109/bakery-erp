using Bakery.Domain.Entities;

namespace Bakery.Domain.Interfaces;

public interface IBranchScoped
{
    int BranchId { get; set; }
    Branch Branch { get; set; }
}
