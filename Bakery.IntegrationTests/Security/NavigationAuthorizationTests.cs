using Bakery.Application.Interfaces;
using Bakery.Application.Security;
using Bakery.WPF.Authorization;
using Bakery.WPF.Services;
using Bakery.WPF.ViewModels;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Bakery.IntegrationTests;

public sealed class NavigationAuthorizationTests
{
    [Fact]
    public void NavigationItems_UseTheSameCentralPolicyAsDeepLinks()
    {
        var item = new NavigationItemViewModel(
            "Users", "AccountCog", typeof(UsersViewModel), () => { });

        item.PermissionKeys.Should().BeEquivalentTo(
            NavigationAuthorizationPolicy.GetRequiredPermissions(typeof(UsersViewModel)));
        item.PermissionKeys.Should().BeEquivalentTo([PermissionKeys.UsersView]);
    }

    [Fact]
    public void NavigationService_DeniesUnauthorizedDeepLinkBeforeResolvingViewModel()
    {
        using var provider = new ServiceCollection().BuildServiceProvider();
        var permissions = new TestPermissionService();
        var service = new NavigationService(provider, permissions);

        service.CanNavigateTo<UsersViewModel>().Should().BeFalse();
        var action = () => service.NavigateTo<UsersViewModel>();

        action.Should().Throw<UnauthorizedAccessException>();
        permissions.LastRequired.Should().BeEquivalentTo([PermissionKeys.UsersView]);
        service.CurrentViewModel.Should().BeNull();
    }

    [Fact]
    public void SecurityAdministrationPages_HaveExplicitPolicies()
    {
        NavigationAuthorizationPolicy.GetRequiredPermissions(typeof(UsersViewModel))
            .Should().BeEquivalentTo([PermissionKeys.UsersView]);
        NavigationAuthorizationPolicy.GetRequiredPermissions(typeof(RolesViewModel))
            .Should().BeEquivalentTo([PermissionKeys.RolesView]);
        NavigationAuthorizationPolicy.GetRequiredPermissions(typeof(AuditLogViewModel))
            .Should().BeEquivalentTo([PermissionKeys.AuditView]);
    }

    [Fact]
    public void ParentNavigationItem_GroupsSubItems_AndCalculatesCombinedPermissions()
    {
        var sub1 = new NavigationItemViewModel("Sub1", "Icon1", typeof(EmployeesViewModel), () => { });
        var sub2 = new NavigationItemViewModel("Sub2", "Icon2", typeof(SettlementViewModel), () => { });
        var parent = new NavigationItemViewModel("Employees", "AccountGroup", new[] { sub1, sub2 });

        parent.HasSubItems.Should().BeTrue();
        parent.SubItems.Should().HaveCount(2);
        parent.PermissionKeys.Should().Contain(PermissionKeys.EmployeesView);
        parent.PermissionKeys.Should().Contain(PermissionKeys.EmployeesViewSalary);
    }

    [Fact]
    public void UserManagementParentNavigationItem_GroupsUsersAndRoles_AndCalculatesCombinedPermissions()
    {
        var usersSub = new NavigationItemViewModel("المستخدمون", "AccountCog", typeof(UsersViewModel), () => { });
        var rolesSub = new NavigationItemViewModel("الأدوار والصلاحيات", "ShieldAccount", typeof(RolesViewModel), () => { });
        var parent = new NavigationItemViewModel("المستخدمون", "AccountKey", new[] { usersSub, rolesSub });

        parent.HasSubItems.Should().BeTrue();
        parent.SubItems.Should().HaveCount(2);
        parent.PermissionKeys.Should().Contain(PermissionKeys.UsersView);
        parent.PermissionKeys.Should().Contain(PermissionKeys.RolesView);
    }

    private sealed class TestPermissionService : IPermissionService
    {
        public IReadOnlyCollection<string> LastRequired { get; private set; } = [];
        public bool HasPermission(string permissionKey) => false;
        public bool HasAnyPermission(params string[] permissionKeys) => false;
        public void EnsurePermission(string permissionKey) => throw new UnauthorizedAccessException();
        public void EnsureAnyPermission(params string[] permissionKeys)
        {
            LastRequired = permissionKeys;
            throw new UnauthorizedAccessException();
        }
        public bool IsAdmin() => false;
    }
}
