using Bakery.Application.Security;
using Bakery.WPF.ViewModels;

namespace Bakery.WPF.Authorization;

public static class NavigationAuthorizationPolicy
{
    private static readonly IReadOnlyDictionary<Type, string[]> Policies =
        new Dictionary<Type, string[]>
        {
            [typeof(InvoiceWorkspaceViewModel)] = [PermissionKeys.SalesView, PermissionKeys.PurchasesView],
            [typeof(SalesViewModel)] = [PermissionKeys.SalesView],
            [typeof(PurchasesViewModel)] = [PermissionKeys.PurchasesView],
            [typeof(ProductionViewModel)] = [PermissionKeys.ProductionView],
            [typeof(ProductionOrderViewModel)] = [PermissionKeys.ProductionCreate, PermissionKeys.ProductionEdit],
            [typeof(ProductionHistoryViewModel)] = [PermissionKeys.ProductionView],
            [typeof(RecipesViewModel)] = [PermissionKeys.ProductionView],
            [typeof(WasteViewModel)] = [PermissionKeys.ProductionWaste],
            [typeof(InventoryHomeViewModel)] = [PermissionKeys.InventoryView, PermissionKeys.ProductsView],
            [typeof(ItemsViewModel)] = [PermissionKeys.ProductsView],
            [typeof(UnitsViewModel)] = [PermissionKeys.ProductsView],
            [typeof(InventoryViewModel)] = [PermissionKeys.InventoryView],
            [typeof(InventoryMovementsViewModel)] = [PermissionKeys.InventoryView, PermissionKeys.ReportsInventory],
            [typeof(StockCountViewModel)] = [PermissionKeys.InventoryCount],
            [typeof(PartiesViewModel)] = [PermissionKeys.AccountingView, PermissionKeys.CustomersView, PermissionKeys.PurchasesView],
            [typeof(PartyStatementViewModel)] = [PermissionKeys.CustomersView, PermissionKeys.PurchasesView],
            [typeof(EmployeesViewModel)] = [PermissionKeys.EmployeesView],
            [typeof(EmployeeWagesViewModel)] = [PermissionKeys.EmployeesViewSalary],
            [typeof(JobRolesViewModel)] = [PermissionKeys.EmployeesViewSalary],
            [typeof(SettlementViewModel)] = [PermissionKeys.EmployeesViewSalary, PermissionKeys.EmployeesAdvances],
            [typeof(EmployeeLedgerViewModel)] = [PermissionKeys.EmployeesViewSalary],
            [typeof(TreasuryViewModel)] = [PermissionKeys.TreasuryView],
            [typeof(ReportsViewModel)] =
                [PermissionKeys.ReportsSales, PermissionKeys.ReportsInventory, PermissionKeys.ReportsFinancial, PermissionKeys.ReportsProduction],
            [typeof(ReportDetailsViewModel)] =
                [PermissionKeys.ReportsSales, PermissionKeys.ReportsInventory, PermissionKeys.ReportsFinancial, PermissionKeys.ReportsProduction],
            [typeof(BranchesViewModel)] = [PermissionKeys.SettingsBranchManagement],
            [typeof(UsersViewModel)] = [PermissionKeys.UsersView],
            [typeof(RolesViewModel)] = [PermissionKeys.RolesView],
            [typeof(AuditLogViewModel)] = [PermissionKeys.AuditView],
            [typeof(SettingsViewModel)] = [PermissionKeys.SettingsSystem],
            [typeof(HealthMonitorViewModel)] = [PermissionKeys.SettingsSystem],
            [typeof(BackupManagementViewModel)] = [PermissionKeys.BackupViewStatus]
        };

    public static IReadOnlyCollection<string> GetRequiredPermissions(Type viewModelType)
        => Policies.TryGetValue(viewModelType, out var permissions) ? permissions : [];
}
