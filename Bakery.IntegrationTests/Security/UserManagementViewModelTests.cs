using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Bakery.Application.DTOs;
using Bakery.Application.Interfaces;
using Bakery.Application.Security;
using Bakery.Shared.Helpers;
using Bakery.WPF.ViewModels;
using FluentAssertions;
using Xunit;

namespace Bakery.IntegrationTests;

public class UserManagementViewModelTests
{
    private readonly List<PermissionDto> _catalogPermissions;

    public UserManagementViewModelTests()
    {
        // Mock standard catalog permissions based on PermissionCatalog.All
        _catalogPermissions = PermissionCatalog.All
            .Select((p, idx) => new PermissionDto(idx + 1, p.Key, p.DisplayName, p.Category))
            .ToList();
    }

    [Fact]
    public void AllCatalogPermissions_ShouldHaveArabicLocalization()
    {
        foreach (var def in PermissionCatalog.All)
        {
            // Verify Display Name has been translated (does not match default English name)
            var displayName = Loc.GetPermissionDisplayName(def.Key, def.DisplayName);
            displayName.Should().NotBe(def.DisplayName, $"Permission '{def.Key}' display name should be localized in Arabic");

            // Verify Description has been translated
            var description = Loc.GetPermissionDescription(def.Key);
            description.Should().NotBe("إذن بالوصول للعمليات المطلوبة.", $"Permission '{def.Key}' description should be localized");
            description.Should().NotBeNullOrEmpty();

            // Verify Category has been translated
            var categoryName = Loc.GetPermissionCategoryName(def.Category);
            categoryName.Should().NotBe(def.Category, $"Category '{def.Category}' should be localized in Arabic");
        }
    }

    [Fact]
    public void DangerousPermissions_ShouldBeHighlightedCorrectly()
    {
        var vm = new UserFormDialogViewModel();
        vm.Initialize(_catalogPermissions, Array.Empty<BranchDto>());

        var allVMs = vm.PermissionCategories.SelectMany(c => c.Permissions).ToList();

        // 1. Delete permissions must be dangerous
        var deletePerms = allVMs.Where(p => p.Key.EndsWith(".Delete", StringComparison.OrdinalIgnoreCase)).ToList();
        deletePerms.Should().NotBeEmpty();
        foreach (var p in deletePerms)
        {
            p.IsDangerous.Should().BeTrue($"Permission '{p.Key}' should be flagged as dangerous");
        }

        // 2. Specific sensitive operations must be dangerous
        var dangerousKeys = new[]
        {
            PermissionKeys.WorkingDayReopen,
            PermissionKeys.SettingsSystem,
            PermissionKeys.SettingsResetSystem,
            PermissionKeys.TreasuryManageSafes,
            PermissionKeys.EmployeesAdvances,
            PermissionKeys.UsersChangePermissions
        };

        foreach (var key in dangerousKeys)
        {
            var p = allVMs.FirstOrDefault(x => x.Key.Equals(key, StringComparison.OrdinalIgnoreCase));
            p.Should().NotBeNull($"Permission '{key}' should exist in the VM");
            p!.IsDangerous.Should().BeTrue($"Permission '{key}' should be flagged as dangerous");
        }

        // 3. View permissions should not be dangerous
        var viewPerms = allVMs.Where(p => p.Key.EndsWith(".View", StringComparison.OrdinalIgnoreCase)).ToList();
        foreach (var p in viewPerms)
        {
            p.IsDangerous.Should().BeFalse($"View permission '{p.Key}' should not be dangerous");
        }
    }

    [Fact]
    public void SelectingChildPermission_ShouldAutoSelectParentPermission()
    {
        // Arrange
        var vm = new UserFormDialogViewModel();
        vm.Initialize(_catalogPermissions, Array.Empty<BranchDto>());

        var allVMs = vm.PermissionCategories.SelectMany(c => c.Permissions).ToDictionary(p => p.Key, StringComparer.OrdinalIgnoreCase);

        var salesView = allVMs[PermissionKeys.SalesView];
        var salesCreate = allVMs[PermissionKeys.SalesCreate];

        salesView.IsSelected.Should().BeFalse();
        salesCreate.IsSelected.Should().BeFalse();

        // Act - Select child
        salesCreate.IsSelected = true;

        // Assert - Parent should be auto-selected and child remains selected/enabled
        salesView.IsSelected.Should().BeTrue("Parent permission should auto-select when child is selected");
        salesCreate.IsSelected.Should().BeTrue();
        salesCreate.IsEnabled.Should().BeTrue();
    }

    [Fact]
    public void UnselectingParentPermission_ShouldAutoUnselectAndDisableChildPermissions()
    {
        // Arrange
        var vm = new UserFormDialogViewModel();
        vm.Initialize(_catalogPermissions, Array.Empty<BranchDto>());

        var allVMs = vm.PermissionCategories.SelectMany(c => c.Permissions).ToDictionary(p => p.Key, StringComparer.OrdinalIgnoreCase);

        var salesView = allVMs[PermissionKeys.SalesView];
        var salesCreate = allVMs[PermissionKeys.SalesCreate];
        var salesDelete = allVMs[PermissionKeys.SalesDelete];

        // Pre-select children
        salesCreate.IsSelected = true;
        salesDelete.IsSelected = true;
        salesView.IsSelected.Should().BeTrue();

        // Act - Unselect parent
        salesView.IsSelected = false;

        // Assert - All children must be unselected and disabled
        salesCreate.IsSelected.Should().BeFalse("Child permission should auto-unselect when parent is unselected");
        salesDelete.IsSelected.Should().BeFalse("Child permission should auto-unselect when parent is unselected");
        salesCreate.IsEnabled.Should().BeFalse("Child permission should be disabled when parent is unselected");
        salesDelete.IsEnabled.Should().BeFalse("Child permission should be disabled when parent is unselected");
    }

    [Fact]
    public void DirectPermission_ShouldNotReplaceRequiredRole()
    {
        var branches = new[] { new BranchDto(1, "MAIN", "الفرع الرئيسي", true, null) };
        var roles = new[] { new RoleListItemDto(7, "كاشير", null, false, false, 0, 1) };
        var vm = new UserFormDialogViewModel();

        vm.Initialize(_catalogPermissions, branches, roles: roles);
        vm.FullName = "مستخدم جديد";
        vm.Username = "new-user";
        vm.Password = "securepassword123";
        vm.ConfirmPassword = "securepassword123";
        vm.Branches.Single().IsSelected = true;
        vm.PermissionCategories.SelectMany(category => category.Permissions)
            .Single(permission => permission.Key == PermissionKeys.SalesView).IsSelected = true;

        vm.CanSave.Should().BeFalse("a direct permission is optional and cannot replace the required job role");
        vm.ValidationMessages.Should().Contain(message => message.Contains("دور وظيفي"));
    }

    [Fact]
    public void CompleteRequiredFields_WithRoleAndNoDirectPermissions_ShouldEnableSave()
    {
        var branches = new[] { new BranchDto(1, "MAIN", "الفرع الرئيسي", true, null) };
        var roles = new[] { new RoleListItemDto(7, "كاشير", null, false, false, 0, 1) };
        var vm = new UserFormDialogViewModel();

        vm.Initialize(_catalogPermissions, branches, roles: roles);
        vm.FullName = "مستخدم جديد";
        vm.Username = "new-user";
        vm.Password = "securepassword123";
        vm.ConfirmPassword = "securepassword123";
        vm.Branches.Single().IsSelected = true;
        vm.Roles.Single().IsSelected = true;

        vm.CanSave.Should().BeTrue();
        vm.SaveCommand.CanExecute(null).Should().BeTrue();
        vm.ValidationMessages.Should().BeEmpty();

        var request = vm.ToSaveRequest(null);
        request.PermissionKeys.Should().BeEmpty();
        request.RoleIds.Should().BeEquivalentTo([7]);
    }

    [Fact]
    public void RelevantFieldAndSelectionChanges_ShouldNotifySaveCanExecute()
    {
        var branches = new[] { new BranchDto(1, "MAIN", "الفرع الرئيسي", true, null) };
        var roles = new[] { new RoleListItemDto(7, "كاشير", null, false, false, 0, 1) };
        var vm = new UserFormDialogViewModel();
        vm.Initialize(_catalogPermissions, branches, roles: roles);
        var notifications = 0;
        vm.SaveCommand.CanExecuteChanged += (_, _) => notifications++;

        void ShouldNotify(Action change)
        {
            var before = notifications;
            change();
            notifications.Should().BeGreaterThan(before);
        }

        ShouldNotify(() => vm.FullName = "مستخدم جديد");
        ShouldNotify(() => vm.Username = "new-user");
        ShouldNotify(() => vm.Password = "securepassword123");
        ShouldNotify(() => vm.ConfirmPassword = "securepassword123");
        ShouldNotify(() => vm.Branches.Single().IsSelected = true);
        ShouldNotify(() => vm.Roles.Single().IsSelected = true);
        ShouldNotify(() => vm.PermissionCategories.SelectMany(category => category.Permissions)
            .Single(permission => permission.Key == PermissionKeys.SalesView).IsSelected = true);
    }

    [Fact]
    public async Task UsedUsername_ShouldKeepSaveDisabledAndShowReason()
    {
        var branches = new[] { new BranchDto(1, "MAIN", "الفرع الرئيسي", true, null) };
        var roles = new[] { new RoleListItemDto(7, "كاشير", null, false, false, 0, 1) };
        var vm = new UserFormDialogViewModel(new StubValidationService(usernameUsed: true));

        vm.Initialize(_catalogPermissions, branches, roles: roles);
        vm.FullName = "مستخدم جديد";
        vm.Username = "existing-user";
        vm.Password = "securepassword123";
        vm.ConfirmPassword = "securepassword123";
        vm.Branches.Single().IsSelected = true;
        vm.Roles.Single().IsSelected = true;

        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (vm.IsCheckingUsername && DateTime.UtcNow < deadline)
        {
            await Task.Delay(25);
        }

        vm.IsCheckingUsername.Should().BeFalse();
        vm.CanSave.Should().BeFalse();
        vm.ValidationMessages.Should().Contain(message => message.Contains("مستخدم بالفعل"));
    }

    [Fact]
    public void SearchText_ShouldFilterPermissionsAndCategories()
    {
        // Arrange
        var vm = new UserFormDialogViewModel();
        vm.Initialize(_catalogPermissions, Array.Empty<BranchDto>());

        var salesCategory = vm.PermissionCategories.First(c => c.Name == "المبيعات");
        var customersCategory = vm.PermissionCategories.First(c => c.Name == "العملاء");

        // Act - Search for something specific in Sales (like "فاتورة بيع")
        vm.PermissionSearchText = "فاتورة بيع";

        // Assert
        salesCategory.IsVisible.Should().BeTrue("Sales category should remain visible since it has matching items");
        customersCategory.IsVisible.Should().BeFalse("Customers category should be hidden since none of its items match");

        // Verify individual items visibility
        var salesCreate = salesCategory.Permissions.First(p => p.Key == PermissionKeys.SalesCreate);
        var salesPrint = salesCategory.Permissions.First(p => p.Key == PermissionKeys.SalesPrint);

        salesCreate.IsVisible.Should().BeTrue("Sales.Create matches the search text");
        salesPrint.IsVisible.Should().BeFalse("Sales.Print does not match the search text");
    }

    [Fact]
    public void BasicProfileMode_ShouldNotSubmitSecurityAssignments()
    {
        var details = new UserDetailsDto(
            17,
            "profile-user",
            "Profile User",
            true,
            [PermissionKeys.SalesView],
            [4],
            [9],
            [],
            "AQID");
        var vm = new UserFormDialogViewModel();

        vm.Initialize([], [], details, [], null, canManageSecurity: false);
        var request = vm.ToSaveRequest(details.Id);

        vm.CanManageSecurity.Should().BeFalse();
        vm.CanManageRoles.Should().BeFalse();
        vm.CanSave.Should().BeTrue();
        request.PermissionKeys.Should().BeNull();
        request.BranchIds.Should().BeNull();
        request.RoleIds.Should().BeNull();
        request.SafePermissions.Should().BeNull();
        request.RowVersion.Should().Be(details.RowVersion);
    }

    private sealed class StubValidationService(bool usernameUsed) : IValidationService
    {
        public Task<bool> IsItemCodeUsedAsync(string code, int? excludeId = null) => Task.FromResult(false);
        public Task<bool> IsBarcodeUsedAsync(string? barcode, int? excludeId = null) => Task.FromResult(false);
        public Task<bool> IsUsernameUsedAsync(string username, int? excludeId = null) => Task.FromResult(usernameUsed);
        public Task<bool> IsEmployeeCodeUsedAsync(string code, int? excludeId = null) => Task.FromResult(false);
        public Task<bool> IsSafeNameUsedAsync(string name, int? excludeId = null) => Task.FromResult(false);
        public Task<bool> IsJobRoleNameUsedAsync(string name, int? excludeId = null) => Task.FromResult(false);
        public Task<bool> IsPartyNameUsedAsync(string name, int? excludeId = null) => Task.FromResult(false);
    }
}
