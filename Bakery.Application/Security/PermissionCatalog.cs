namespace Bakery.Application.Security;

public sealed record PermissionDefinition(string Key, string DisplayName, string Category);

public static class PermissionKeys
{
    public const string SalesView = "Sales.View";
    public const string SalesCreate = "Sales.Create";
    public const string SalesEdit = "Sales.Edit";
    public const string SalesDelete = "Sales.Delete";
    public const string SalesCancel = "Sales.Cancel";
    public const string SalesPrint = "Sales.Print";

    public const string CustomersView = "Customers.View";
    public const string CustomersAdd = "Customers.Add";
    public const string CustomersEdit = "Customers.Edit";
    public const string CustomersDelete = "Customers.Delete";

    public const string ProductsView = "Products.View";
    public const string ProductsAdd = "Products.Add";
    public const string ProductsEdit = "Products.Edit";
    public const string ProductsDelete = "Products.Delete";
    public const string ProductsViewCost = "Products.ViewCost";

    public const string PurchasesView = "Purchases.View";
    public const string PurchasesCreate = "Purchases.Create";
    public const string PurchasesEdit = "Purchases.Edit";
    public const string PurchasesDelete = "Purchases.Delete";
    public const string PurchasesCancel = "Purchases.Cancel";
    public const string PurchasesPrint = "Purchases.Print";

    public const string ProductionView = "Production.View";
    public const string ProductionCreate = "Production.Create";
    public const string ProductionEdit = "Production.Edit";
    public const string ProductionCancel = "Production.Cancel";
    public const string ProductionWaste = "Production.Waste";

    public const string InventoryView = "Inventory.View";
    public const string InventoryStockAdjustments = "Inventory.StockAdjustments";
    public const string InventoryCount = "Inventory.Count";

    public const string TreasuryView = "Treasury.View";
    public const string TreasuryCashIn = "Treasury.CashIn";
    public const string TreasuryCashOut = "Treasury.CashOut";
    public const string TreasuryTransfer = "Treasury.Transfer";
    public const string TreasuryManageSafes = "Treasury.ManageSafes";

    public const string CashDeposit = "Cash.Deposit";
    public const string CashWithdraw = "Cash.Withdraw";
    public const string CashReverseManualTransaction = "Cash.ReverseManualTransaction";
    public const string TreasuryReversePartyPayment = "Treasury.ReversePartyPayment";
    public const string CashViewAllTransactions = "Cash.ViewAllTransactions";

    public const string AccountingView = "Accounting.View";
    public const string AccountingJournalEntries = "Accounting.JournalEntries";
    public const string AccountingCustomerLedger = "Accounting.CustomerLedger";
    public const string AccountingSupplierLedger = "Accounting.SupplierLedger";

    public const string EmployeesView = "Employees.View";
    public const string EmployeesAdd = "Employees.Add";
    public const string EmployeesEdit = "Employees.Edit";
    public const string EmployeesDelete = "Employees.Delete";
    public const string EmployeesSalaries = "Employees.Salaries";
    public const string EmployeesAdvances = "Employees.Advances";
    public const string EmployeesViewSalary = "Employees.ViewSalary";
    public const string EmployeesManagePayroll = "Employees.ManagePayroll";

    public const string ReportsSales = "Reports.Sales";
    public const string ReportsInventory = "Reports.Inventory";
    public const string ReportsFinancial = "Reports.Financial";
    public const string ReportsProduction = "Reports.Production";
    public const string ReportsPrint = "Reports.Print";
    public const string ReportsExport = "Reports.Export";

    public const string WorkingDayOpen = "WorkingDay.Open";
    public const string WorkingDayView = "WorkingDay.View";
    public const string WorkingDayClose = "WorkingDay.Close";
    public const string WorkingDayOverrideCloseBlockers = "WorkingDay.OverrideCloseBlockers";
    public const string WorkingDayReopen = "WorkingDay.Reopen";

    public const string SettingsSystem = "Settings.System";
    public const string SettingsBranchManagement = "Settings.BranchManagement";
    public const string SettingsResetSystem = "Settings.ResetSystem";

    public const string UsersView = "Users.View";
    public const string UsersAdd = "Users.Add";
    public const string UsersEdit = "Users.Edit";
    public const string UsersDelete = "Users.Delete";
    public const string UsersChangePermissions = "Users.ChangePermissions";
    public const string UsersResetPassword = "Users.ResetPassword";

    public const string RolesView = "Roles.View";
    public const string RolesAdd = "Roles.Add";
    public const string RolesEdit = "Roles.Edit";
    public const string RolesDelete = "Roles.Delete";
    public const string RolesAssign = "Roles.Assign";

    public const string AuditView = "Audit.View";
    public const string AuditExport = "Audit.Export";

    public const string BranchesSwitch = "Branches.Switch";

    public const string BackupViewStatus = "Backup.ViewStatus";
    public const string BackupCreateManual = "Backup.CreateManual";
    public const string BackupRestore = "Backup.Restore";
    public const string BackupDelete = "Backup.Delete";
    public const string BackupManageSettings = "Backup.ManageSettings";
    public const string BackupConnectGoogleDrive = "Backup.ConnectGoogleDrive";
    public const string BackupDisconnectGoogleDrive = "Backup.DisconnectGoogleDrive";
}

public static class PermissionCatalog
{
    public static IReadOnlyList<PermissionDefinition> All { get; } =
    [
        new(PermissionKeys.SalesView, "View Sales", "Sales"),
        new(PermissionKeys.SalesCreate, "Create Sales Invoice", "Sales"),
        new(PermissionKeys.SalesEdit, "Edit Sales Invoice", "Sales"),
        new(PermissionKeys.SalesDelete, "Delete Sales Invoice", "Sales"),
        new(PermissionKeys.SalesCancel, "Cancel Sales Invoice", "Sales"),
        new(PermissionKeys.SalesPrint, "Print Invoice", "Sales"),

        new(PermissionKeys.CustomersView, "View Customers", "Customers"),
        new(PermissionKeys.CustomersAdd, "Add Customer", "Customers"),
        new(PermissionKeys.CustomersEdit, "Edit Customer", "Customers"),
        new(PermissionKeys.CustomersDelete, "Delete Customer", "Customers"),

        new(PermissionKeys.ProductsView, "View Products", "Products"),
        new(PermissionKeys.ProductsAdd, "Add Product", "Products"),
        new(PermissionKeys.ProductsEdit, "Edit Product", "Products"),
        new(PermissionKeys.ProductsDelete, "Delete Product", "Products"),
        new(PermissionKeys.ProductsViewCost, "View Product Cost", "Products"),

        new(PermissionKeys.PurchasesView, "View Purchases", "Purchases"),
        new(PermissionKeys.PurchasesCreate, "Create Purchase", "Purchases"),
        new(PermissionKeys.PurchasesEdit, "Edit Purchase", "Purchases"),
        new(PermissionKeys.PurchasesDelete, "Delete Purchase", "Purchases"),
        new(PermissionKeys.PurchasesCancel, "Cancel Purchase", "Purchases"),
        new(PermissionKeys.PurchasesPrint, "Print Purchase", "Purchases"),

        new(PermissionKeys.ProductionView, "View Production", "Production"),
        new(PermissionKeys.ProductionCreate, "Create Production", "Production"),
        new(PermissionKeys.ProductionEdit, "Edit Production", "Production"),
        new(PermissionKeys.ProductionCancel, "Cancel Production", "Production"),
        new(PermissionKeys.ProductionWaste, "Waste Management", "Production"),

        new(PermissionKeys.InventoryView, "View Inventory", "Inventory"),
        new(PermissionKeys.InventoryStockAdjustments, "Stock Adjustments", "Inventory"),
        new(PermissionKeys.InventoryCount, "Inventory Count", "Inventory"),

        new(PermissionKeys.TreasuryView, "View Treasury", "Treasury"),
        new(PermissionKeys.TreasuryCashIn, "Cash In", "Treasury"),
        new(PermissionKeys.TreasuryCashOut, "Cash Out", "Treasury"),
        new(PermissionKeys.TreasuryTransfer, "Transfer Between Safes", "Treasury"),
        new(PermissionKeys.TreasuryManageSafes, "Manage Safes", "Treasury"),

        new(PermissionKeys.CashDeposit, "Deposit Manual Cash", "Cash Operations"),
        new(PermissionKeys.CashWithdraw, "Withdraw Manual Cash", "Cash Operations"),
        new(PermissionKeys.CashReverseManualTransaction, "Reverse Manual Transaction", "Cash Operations"),
        new(PermissionKeys.TreasuryReversePartyPayment, "Reverse Customer/Supplier Payment", "Cash Operations"),
        new(PermissionKeys.CashViewAllTransactions, "View All Manual Cash Transactions", "Cash Operations"),

        new(PermissionKeys.AccountingView, "View Accounts", "Accounting"),
        new(PermissionKeys.AccountingJournalEntries, "Journal Entries", "Accounting"),
        new(PermissionKeys.AccountingCustomerLedger, "Customer Ledger", "Accounting"),
        new(PermissionKeys.AccountingSupplierLedger, "Supplier Ledger", "Accounting"),

        new(PermissionKeys.EmployeesView, "View Employees", "Employees"),
        new(PermissionKeys.EmployeesAdd, "Add Employee", "Employees"),
        new(PermissionKeys.EmployeesEdit, "Edit Employee", "Employees"),
        new(PermissionKeys.EmployeesDelete, "Delete Employee", "Employees"),
        new(PermissionKeys.EmployeesSalaries, "Calculate Salaries", "Employees"),
        new(PermissionKeys.EmployeesAdvances, "Disburse Advances", "Employees"),
        new(PermissionKeys.EmployeesViewSalary, "View Salary and Compensation", "Employees"),
        new(PermissionKeys.EmployeesManagePayroll, "Manage Payroll", "Employees"),

        new(PermissionKeys.ReportsSales, "Sales Reports", "Reports"),
        new(PermissionKeys.ReportsInventory, "Inventory Reports", "Reports"),
        new(PermissionKeys.ReportsFinancial, "Financial Reports", "Reports"),
        new(PermissionKeys.ReportsProduction, "Production Reports", "Reports"),
        new(PermissionKeys.ReportsPrint, "Print Reports", "Reports"),
        new(PermissionKeys.ReportsExport, "Export Reports", "Reports"),

        new(PermissionKeys.WorkingDayView, "View Working Day", "Working Day"),
        new(PermissionKeys.WorkingDayOpen, "Open Working Day", "Working Day"),
        new(PermissionKeys.WorkingDayClose, "Close Working Day", "Working Day"),
        new(PermissionKeys.WorkingDayOverrideCloseBlockers, "Override Working Day Close Blockers", "Working Day"),
        new(PermissionKeys.WorkingDayReopen, "Reopen Working Day", "Working Day"),

        new(PermissionKeys.SettingsSystem, "System Settings", "Settings"),
        new(PermissionKeys.SettingsBranchManagement, "Branch Management", "Settings"),
        new(PermissionKeys.SettingsResetSystem, "Reset System Data", "Settings"),

        new(PermissionKeys.UsersView, "View Users", "Users"),
        new(PermissionKeys.UsersAdd, "Add User", "Users"),
        new(PermissionKeys.UsersEdit, "Edit User", "Users"),
        new(PermissionKeys.UsersDelete, "Delete User", "Users"),
        new(PermissionKeys.UsersChangePermissions, "Manage Permissions", "Users"),
        new(PermissionKeys.UsersResetPassword, "Reset User Password", "Users"),

        new(PermissionKeys.RolesView, "View Security Roles", "Roles"),
        new(PermissionKeys.RolesAdd, "Add Security Role", "Roles"),
        new(PermissionKeys.RolesEdit, "Edit Security Role", "Roles"),
        new(PermissionKeys.RolesDelete, "Delete Security Role", "Roles"),
        new(PermissionKeys.RolesAssign, "Assign Security Roles", "Roles"),

        new(PermissionKeys.AuditView, "View Audit History", "Audit"),
        new(PermissionKeys.AuditExport, "Export Audit History", "Audit"),

        new(PermissionKeys.BranchesSwitch, "Switch Branch", "Branches"),

        new(PermissionKeys.BackupViewStatus, "View Backup Status", "Backup"),
        new(PermissionKeys.BackupCreateManual, "Create Manual Backup", "Backup"),
        new(PermissionKeys.BackupRestore, "Restore Backup", "Backup"),
        new(PermissionKeys.BackupDelete, "Delete Backup", "Backup"),
        new(PermissionKeys.BackupManageSettings, "Manage Backup Settings", "Backup"),
        new(PermissionKeys.BackupConnectGoogleDrive, "Connect Google Drive", "Backup"),
        new(PermissionKeys.BackupDisconnectGoogleDrive, "Disconnect Google Drive", "Backup")
    ];
}
