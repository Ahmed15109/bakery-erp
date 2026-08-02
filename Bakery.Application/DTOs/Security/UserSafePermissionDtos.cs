using System.Collections.Generic;

namespace Bakery.Application.DTOs;

public sealed record UserSafePermissionDto
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public int SafeId { get; set; }
    public string SafeName { get; set; } = string.Empty;
    public bool CanAccess { get; set; }
    public bool CanViewBalance { get; set; }
    public bool CanViewLedger { get; set; }
    public bool CanCashIn { get; set; }
    public bool CanCashOut { get; set; }
    public bool CanTransferFrom { get; set; }
    public bool CanReceiveTransfer { get; set; }
}

public sealed record UpdateUserSafePermissionsRequest(
    int UserId,
    IReadOnlyCollection<UserSafePermissionDto> Permissions);

public sealed record GetUserSafePermissionsResponse(
    int UserId,
    IReadOnlyCollection<UserSafePermissionDto> Permissions);
