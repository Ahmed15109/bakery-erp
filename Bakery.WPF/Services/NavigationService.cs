using CommunityToolkit.Mvvm.ComponentModel;
using Bakery.Application.Interfaces;
using Bakery.WPF.Authorization;
using Microsoft.Extensions.DependencyInjection;

namespace Bakery.WPF.Services;

public sealed partial class NavigationService : ObservableObject, INavigationService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly IPermissionService _permissionService;

    [ObservableProperty]
    private ObservableObject? currentViewModel;

    public NavigationService(IServiceProvider serviceProvider, IPermissionService permissionService)
    {
        _serviceProvider = serviceProvider;
        _permissionService = permissionService;
    }

    public bool CanNavigateTo<TViewModel>() where TViewModel : ObservableObject
    {
        var required = NavigationAuthorizationPolicy.GetRequiredPermissions(typeof(TViewModel));
        return required.Count == 0 || required.Any(_permissionService.HasPermission);
    }

    public TViewModel NavigateTo<TViewModel>() where TViewModel : ObservableObject
    {
        var required = NavigationAuthorizationPolicy.GetRequiredPermissions(typeof(TViewModel));
        if (required.Count > 0)
        {
            _permissionService.EnsureAnyPermission(required.ToArray());
        }
        if (CurrentViewModel is IDisposable disposable)
        {
            disposable.Dispose();
        }
        var vm = _serviceProvider.GetRequiredService<TViewModel>();
        CurrentViewModel = vm;
        return vm;
    }
}
