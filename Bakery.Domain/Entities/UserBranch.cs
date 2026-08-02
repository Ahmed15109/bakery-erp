namespace Bakery.Domain.Entities;

public sealed class UserBranch
{
    public int UserId { get; set; }
    public User User { get; set; } = null!;

    public int BranchId { get; set; }
    public Branch Branch { get; set; } = null!;
}
