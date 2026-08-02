namespace Bakery.Application.DTOs;

public sealed record BranchDto(int Id, string Code, string Name, bool IsActive, string? Notes);

public sealed record CreateBranchRequest(string Code, string Name, string? Notes);

public sealed record UpdateBranchRequest(int Id, string Code, string Name, bool IsActive, string? Notes);
