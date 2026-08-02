namespace Bakery.Application.Interfaces;

public interface IPermissionService
{
    bool HasPermission(string permissionKey);
    bool HasAnyPermission(params string[] permissionKeys) => permissionKeys.Any(HasPermission);
    void EnsurePermission(string permissionKey);
    void EnsureAnyPermission(params string[] permissionKeys)
    {
        if (!HasAnyPermission(permissionKeys))
        {
            throw new UnauthorizedAccessException("ليس لديك صلاحية لتنفيذ هذا الإجراء.");
        }
    }
    bool IsAdmin();
}
