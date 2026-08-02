using Bakery.Application.Interfaces;
using Bakery.Application.Security;
using Bakery.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Bakery.Infrastructure.Services;

public sealed class SettingsService : ISettingsService
{
    private readonly IRepository<AppSetting> _repository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IPermissionService _permissionService;

    public SettingsService(IRepository<AppSetting> repository, IUnitOfWork unitOfWork, IPermissionService permissionService)
    {
        _repository = repository;
        _unitOfWork = unitOfWork;
        _permissionService = permissionService;
    }

    public async Task<string?> GetSettingAsync(string key, CancellationToken cancellationToken = default)
    {
        var context = ((dynamic)_repository).DbContext as DbContext;
        if (context == null) return null;

        var setting = await context.Set<AppSetting>().FirstOrDefaultAsync(s => s.Key == key, cancellationToken);
        return setting?.Value;
    }

    public async Task SetSettingAsync(string key, string value, CancellationToken cancellationToken = default)
    {
        _permissionService.EnsurePermission(PermissionKeys.SettingsSystem);
        var context = ((dynamic)_repository).DbContext as DbContext;
        if (context == null) return;

        var setting = await context.Set<AppSetting>().FirstOrDefaultAsync(s => s.Key == key, cancellationToken);
        if (setting == null)
        {
            setting = new AppSetting { Key = key, Value = value };
            await _repository.AddAsync(setting, cancellationToken);
        }
        else
        {
            setting.Value = value;
            await _repository.UpdateAsync(setting, cancellationToken);
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);
    }

    public async Task<bool> IsDarkModeAsync(CancellationToken cancellationToken = default)
    {
        var val = await GetSettingAsync("IsDarkMode", cancellationToken);
        return val == "true";
    }

    public Task SetDarkModeAsync(bool isDark, CancellationToken cancellationToken = default)
    {
        return SetSettingAsync("IsDarkMode", isDark ? "true" : "false", cancellationToken);
    }

    public async Task<bool> IsRtlAsync(CancellationToken cancellationToken = default)
    {
        var val = await GetSettingAsync("IsRtl", cancellationToken);
        return val == "true";
    }

    public Task SetRtlAsync(bool isRtl, CancellationToken cancellationToken = default)
    {
        return SetSettingAsync("IsRtl", isRtl ? "true" : "false", cancellationToken);
    }
}
