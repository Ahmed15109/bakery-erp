namespace Bakery.Application.Security;


public static class PermissionPolicyCatalog
{
    private static readonly IReadOnlyDictionary<string, string[]> RequiredParents =
        new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
        {
            [PermissionKeys.SalesCreate] = [PermissionKeys.SalesView],
            [PermissionKeys.SalesEdit] = [PermissionKeys.SalesView],
            [PermissionKeys.SalesDelete] = [PermissionKeys.SalesView],
            [PermissionKeys.SalesCancel] = [PermissionKeys.SalesView],
            [PermissionKeys.SalesPrint] = [PermissionKeys.SalesView],
            [PermissionKeys.CustomersAdd] = [PermissionKeys.CustomersView],
            [PermissionKeys.CustomersEdit] = [PermissionKeys.CustomersView],
            [PermissionKeys.CustomersDelete] = [PermissionKeys.CustomersView],
            [PermissionKeys.ProductsAdd] = [PermissionKeys.ProductsView],
            [PermissionKeys.ProductsEdit] = [PermissionKeys.ProductsView],
            [PermissionKeys.ProductsDelete] = [PermissionKeys.ProductsView],
            [PermissionKeys.ProductsViewCost] = [PermissionKeys.ProductsView],
            [PermissionKeys.PurchasesCreate] = [PermissionKeys.PurchasesView],
            [PermissionKeys.PurchasesEdit] = [PermissionKeys.PurchasesView],
            [PermissionKeys.PurchasesDelete] = [PermissionKeys.PurchasesView],
            [PermissionKeys.PurchasesCancel] = [PermissionKeys.PurchasesView],
            [PermissionKeys.PurchasesPrint] = [PermissionKeys.PurchasesView],
            [PermissionKeys.ProductionCreate] = [PermissionKeys.ProductionView],
            [PermissionKeys.ProductionEdit] = [PermissionKeys.ProductionView],
            [PermissionKeys.ProductionCancel] = [PermissionKeys.ProductionView],
            [PermissionKeys.ProductionWaste] = [PermissionKeys.ProductionView],
            [PermissionKeys.InventoryStockAdjustments] = [PermissionKeys.InventoryView],
            [PermissionKeys.InventoryCount] = [PermissionKeys.InventoryView],
            [PermissionKeys.TreasuryCashIn] = [PermissionKeys.TreasuryView],
            [PermissionKeys.TreasuryCashOut] = [PermissionKeys.TreasuryView],
            [PermissionKeys.TreasuryTransfer] = [PermissionKeys.TreasuryView],
            [PermissionKeys.TreasuryManageSafes] = [PermissionKeys.TreasuryView],
            [PermissionKeys.CashDeposit] = [PermissionKeys.TreasuryView],
            [PermissionKeys.CashWithdraw] = [PermissionKeys.TreasuryView],
            [PermissionKeys.CashReverseManualTransaction] = [PermissionKeys.TreasuryView],
            [PermissionKeys.TreasuryReversePartyPayment] = [PermissionKeys.TreasuryView],
            [PermissionKeys.CashViewAllTransactions] = [PermissionKeys.TreasuryView],
            [PermissionKeys.AccountingJournalEntries] = [PermissionKeys.AccountingView],
            [PermissionKeys.AccountingCustomerLedger] = [PermissionKeys.AccountingView],
            [PermissionKeys.AccountingSupplierLedger] = [PermissionKeys.AccountingView],
            [PermissionKeys.EmployeesAdd] = [PermissionKeys.EmployeesView],
            [PermissionKeys.EmployeesEdit] = [PermissionKeys.EmployeesView],
            [PermissionKeys.EmployeesDelete] = [PermissionKeys.EmployeesView],
            [PermissionKeys.EmployeesViewSalary] = [PermissionKeys.EmployeesView],
            [PermissionKeys.EmployeesManagePayroll] = [PermissionKeys.EmployeesViewSalary],
            [PermissionKeys.EmployeesSalaries] = [PermissionKeys.EmployeesViewSalary],
            [PermissionKeys.EmployeesAdvances] = [PermissionKeys.EmployeesView],
            [PermissionKeys.WorkingDayOpen] = [PermissionKeys.WorkingDayView],
            [PermissionKeys.WorkingDayClose] = [PermissionKeys.WorkingDayView],
            [PermissionKeys.WorkingDayOverrideCloseBlockers] = [PermissionKeys.WorkingDayClose],
            [PermissionKeys.WorkingDayReopen] = [PermissionKeys.WorkingDayView],
            [PermissionKeys.UsersAdd] = [PermissionKeys.UsersView],
            [PermissionKeys.UsersEdit] = [PermissionKeys.UsersView],
            [PermissionKeys.UsersDelete] = [PermissionKeys.UsersView],
            [PermissionKeys.UsersChangePermissions] = [PermissionKeys.UsersView],
            [PermissionKeys.UsersResetPassword] = [PermissionKeys.UsersView],
            [PermissionKeys.RolesAdd] = [PermissionKeys.RolesView],
            [PermissionKeys.RolesEdit] = [PermissionKeys.RolesView],
            [PermissionKeys.RolesDelete] = [PermissionKeys.RolesView],
            [PermissionKeys.RolesAssign] = [PermissionKeys.RolesView],
            [PermissionKeys.AuditExport] = [PermissionKeys.AuditView],
            [PermissionKeys.BackupCreateManual] = [PermissionKeys.BackupViewStatus],
            [PermissionKeys.BackupRestore] = [PermissionKeys.BackupViewStatus],
            [PermissionKeys.BackupDelete] = [PermissionKeys.BackupViewStatus],
            [PermissionKeys.BackupManageSettings] = [PermissionKeys.BackupViewStatus],
            [PermissionKeys.BackupConnectGoogleDrive] = [PermissionKeys.BackupManageSettings],
            [PermissionKeys.BackupDisconnectGoogleDrive] = [PermissionKeys.BackupManageSettings]
        };

    public static IReadOnlyCollection<string> GetRequiredParents(string permissionKey)
        => RequiredParents.TryGetValue(permissionKey, out var parents) ? parents : [];

    public static IReadOnlyCollection<string> GetDependentPermissions(string parentPermissionKey)
        => RequiredParents
            .Where(pair => pair.Value.Contains(parentPermissionKey, StringComparer.OrdinalIgnoreCase))
            .Select(pair => pair.Key)
            .ToArray();

    public static void Validate(IReadOnlyCollection<string> effectivePermissions)
    {
        var selected = effectivePermissions.ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var (permission, parents) in RequiredParents)
        {
            if (!selected.Contains(permission))
            {
                continue;
            }

            var missing = parents.Where(parent => !selected.Contains(parent)).ToArray();
            if (missing.Length > 0)
            {
                throw new InvalidOperationException(
                    $"الصلاحية {permission} تتطلب الصلاحيات التالية: {string.Join(", ", missing)}.");
            }
        }

        if ((selected.Contains(PermissionKeys.ReportsPrint) || selected.Contains(PermissionKeys.ReportsExport)) &&
            !selected.Contains(PermissionKeys.ReportsSales) &&
            !selected.Contains(PermissionKeys.ReportsInventory) &&
            !selected.Contains(PermissionKeys.ReportsFinancial) &&
            !selected.Contains(PermissionKeys.ReportsProduction))
        {
            throw new InvalidOperationException("صلاحية طباعة أو تصدير التقارير تتطلب صلاحية عرض تقرير واحد على الأقل.");
        }
    }
}
