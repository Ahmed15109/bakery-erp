namespace Bakery.Application.Interfaces;

public interface IValidationService
{
    Task<bool> IsItemCodeUsedAsync(string code, int? excludeId = null);
    Task<bool> IsBarcodeUsedAsync(string? barcode, int? excludeId = null);
    Task<bool> IsUsernameUsedAsync(string username, int? excludeId = null);
    Task<bool> IsEmployeeCodeUsedAsync(string code, int? excludeId = null);
    Task<bool> IsSafeNameUsedAsync(string name, int? excludeId = null);
    Task<bool> IsJobRoleNameUsedAsync(string name, int? excludeId = null);
    Task<bool> IsPartyNameUsedAsync(string name, int? excludeId = null);
}
