using System.Collections.Generic;

namespace Bakery.Domain.Entities;

public sealed class Branch : BaseEntity
{
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    public string? Notes { get; set; }

    public ICollection<UserBranch> UserBranches { get; set; } = [];
}
