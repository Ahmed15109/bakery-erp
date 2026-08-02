using Bakery.Domain.Interfaces;

namespace Bakery.Domain.Entities;

public sealed class UserSafePermission : BaseEntity, IBranchScoped
{
    public int BranchId { get; set; }
    public Branch Branch { get; set; } = null!;

    public int UserId { get; set; }
    public User User { get; set; } = null!;

    public int SafeId { get; set; }
    public Safe Safe { get; set; } = null!;

    public bool CanAccess { get; set; }
    public bool CanViewBalance { get; set; }
    public bool CanViewLedger { get; set; }
    public bool CanCashIn { get; set; }
    public bool CanCashOut { get; set; }
    public bool CanTransferFrom { get; set; }
    public bool CanReceiveTransfer { get; set; }
}
