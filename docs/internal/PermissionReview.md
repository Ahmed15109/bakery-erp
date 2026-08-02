# Security & Permission Audit Review Report

This report presents a comprehensive security audit of the Bakery ERP authorization system, focusing on permissions, data visibility, and query-level isolation.

---

## 1. Current Strengths

* **Centralized Permission Definition**: The `PermissionCatalog` and `PermissionKeys` are well-structured, providing a unified location for all general permission keys in the application.
* **UI Declarative Hiding**: The use of `auth:PermissionAssist.Required` in WPF XAML allows declarative hiding of menu items, tabs, and buttons, maintaining clean UI code.
* **Branch Isolation (EF Core Query Filters)**: `IBranchScoped` entities successfully enforce branch isolation via Global Query Filters, ensuring that data from Branch A is completely inaccessible to users logged into Branch B.
* **WPF Session Scoping**: The application successfully establishes a scoped DI container per login session, ensuring services and context are isolated between users and branches.
* **Core Command Validation**: Major write operations (e.g., `CreateProductionOrder`, `OpenWorkingDay`, `SaveParty`) properly call `EnsurePermission` at the service layer, preventing execution of commands by unauthorized users.

---

## 2. Weaknesses & Security Holes

### A. The "hasConfigured" Fallback Security Hole (Root Cause of Private Safe Leakage)
In [`UserSafePermissionService.cs`](../../Bakery.Infrastructure/Services/Security/UserSafePermissionService.cs), `CheckPermissionAsync` is implemented as follows:
```csharp
private async Task<bool> CheckPermissionAsync(int userId, int safeId, Func<UserSafePermission, bool> predicate, CancellationToken cancellationToken)
{
    if (userId <= 0) return false;

    var hasConfigured = await _db.UserSafePermissions.AnyAsync(p => p.UserId == userId, cancellationToken);
    if (!hasConfigured) return true; // Default fallback to Full Access!

    var perm = await _db.UserSafePermissions
        .FirstOrDefaultAsync(p => p.UserId == userId && p.SafeId == safeId, cancellationToken);
    return perm != null && perm.CanAccess && predicate(perm);
}
```
**The Security Hole**:
1. Because `UserSafePermission` is an `IBranchScoped` entity, the EF Core Global Query Filter limits `_db.UserSafePermissions` to the user's *current branch*.
2. If an administrator configures safe permissions for a user in **Branch 1**, a record is saved for Branch 1.
3. When that user logs into **Branch 2**, the query filter restricts the permissions database check to `BranchId == 2`.
4. As a result, `_db.UserSafePermissions.AnyAsync(p => p.UserId == userId)` returns `false` (no records configured *for Branch 2*).
5. The method hits the fallback: `if (!hasConfigured) return true;`
6. Consequently, the user is granted **Full Access** to all safes in Branch 2, including the **Private Safe** of Branch 2!
7. even if the admin configures permissions for Branch 1, if they fail to explicitly save safe permissions for Branch 2, the user gets unrestricted access in Branch 2.

### B. Unsecured Core Queries (Data Exposure in Services)
Several read/query methods in application services load data from the database without checking if the user has the required view permissions. If a user bypasses the UI (or calls the service API directly), they can retrieve unauthorized records:
1. **Item Service**:
   * `SearchAsync` in [`ItemService.cs`](../../Bakery.Infrastructure/Services/Inventory/ItemService.cs) searches items and retrieves stock levels without checking `ProductsView` or `InventoryView`.
   * `GetByIdAsync` retrieves item details without permission checks.
   * `GetCurrentStockAsync` retrieves raw stock values without permission checks.
2. **Party Service**:
   * `SearchAsync` in [`PartyService.cs`](../../Bakery.Infrastructure/Services/Accounting/PartyService.cs) searches customers, suppliers, and employees without verifying permissions.
   * `GetBalanceAsync`, `GetPartySummaryAsync`, and `GetStatsAsync` retrieve balances and summaries of parties without validating permissions.
3. **Working Day Service**:
   * `GetCurrentDaySummaryAsync`, `GetClosingReportAsync`, and `CalculateExpectedClosingCashAsync` retrieve daily expected cash, total sales, expenses, and wages without verifying permissions.
4. **Recipe Service**:
   * `GetRecipeByProducedItemIdAsync` in [`RecipeService.cs`](../../Bakery.Infrastructure/Services/Production/RecipeService.cs) retrieves full recipe details (ingredients, quantities) without verifying `ProductionView`.
5. **Stock Calculation Service**:
   * All methods in [`StockCalculationService.cs`](../../Bakery.Infrastructure/Services/Inventory/StockCalculationService.cs) (`GetCurrentStockAsync`, `GetLowStockItemsAsync`, `GetStockValuationAsync`, `HasAvailableStockAsync`) run without any permission validation.

### C. UI-Only Hiding without Real Authorization
1. **Reports Workspace**:
   * [`ReportsViewModel.cs`](../../Bakery.WPF/ViewModels/Reports/ReportsViewModel.cs) initializes four static report categories (Sales, Production, Inventory, Financial).
   * It displays all four categories to any user who has access to the Reports tab. It does not check if the user has specific report permissions (e.g. `ReportsSales` or `ReportsFinancial`). A user with only `ReportsSales` permission can see and click on "Financial Reports" and "Inventory Reports".
2. **Dashboard Metrics**:
   * [`DashboardViewModel.cs`](../../Bakery.WPF/ViewModels/Dashboard/DashboardViewModel.cs) retrieves the active Working Day summary (`_workingDayService.GetCurrentDaySummaryAsync()`) before checking permissions.
   * If the day summary is returned, it aggregates `ExpectedCash` and `TotalSales` into local properties.
   * While the UI metrics cards check permissions (e.g. `PermissionKeys.TreasuryView` or `PermissionKeys.SalesView`) to show/hide the values, the underlying data has already been fetched.
   * More importantly, the **Expected Cash** represents the sum of cash in all daily cash registers and daily safes. A user who is restricted from viewing the safe balance can still see the expected daily cash balance on the dashboard through the daily summary.

### D. Missing Security Audit Trails
* When a service method throws an `UnauthorizedAccessException`, it is silently bubbled up to the UI (which shows a warning box).
* There is no audit logging for failed authorization attempts. A malicious user attempting to access sensitive features (such as deleting users or withdrawing from private safes) will not leave an audit trail in the `AuditLogs` table.

---

## 3. Potential New Permissions Feasibility Study

Evaluating the user's provided list of potential new permissions:

| Permission Category | Potential Permission / Key | Feasibility & Impact | Recommendation |
| :--- | :--- | :--- | :--- |
| **Safe-level** | `CanAccess`, `CanViewBalance`, `CanViewLedger`, `CanCashIn`, `CanCashOut`, `CanTransferFrom`, `CanReceiveTransfer` | **Highly Feasible**: Structurally supported in the database but suffers from the `hasConfigured` branch bypass. | **Must Fix**: Retain these permissions but fix the query-filter bypass and enforce them strictly across all accounting/reporting services. |
| **Production** | Split into `Recipes.Manage` and `Production.Execute` | **Feasible**: Separates the definition of formulas/recipes (sensitive cost and proportion data) from the daily creation of production orders. | **Recommended**: Split to prevent baker-level users from seeing raw recipes costs or editing formulas. |
| **Waste** | `Waste.View`, `Waste.Create`, `Waste.Edit` | **Feasible**: Currently, waste adjustments are recorded under general inventory adjustments (`Inventory.StockAdjustments`). Adding a specific Waste permission ensures managers can restrict spoilage/waste logging. | **Recommended**: Add dedicated waste permissions to distinguish from general stock adjustments. |
| **Inventory** | `Inventory.ValuationView` | **Feasible**: Restricts viewing the monetary value of total inventory, which is a sensitive business metric. | **Recommended**: Keep basic stock quantity visibility but restrict valuation to managers. |
| **Employees** | `Wages.View`, `Wages.Process` | **Feasible**: Separates viewing basic employee directories from managing wages, bonuses, deductions, and salary settlements. | **Critical**: Splitting this prevents general HR users from seeing wages and financial details of other employees. |
| **Reports** | Category-specific report permissions | **Feasible**: Currently defined (`Reports.Sales`, `Reports.Inventory`, `Reports.Financial`) but not implemented in the Reports menu ViewModel. | **Must Fix**: Implement dynamic filtering of report options in the UI view model. |
| **Dashboard** | Card-specific data visibility | **Feasible**: Needs to filter the raw daily summary query based on user permissions. | **Must Fix**: Ensure no aggregated totals are calculated or exposed if the user lacks the parent view permissions. |
| **Working Day** | `WorkingDay.Override` | **Feasible**: Allows closing shifts when draft invoices exist or when cash difference is high. | **Recommended**: Keep the existing structure but restrict override to Admin. |

---

## 4. Recommendations for Refactoring

### Recommendation 1: Fix the Safe Permission Fallback
Modify `CheckPermissionAsync` in `UserSafePermissionService` to ignore the query filter when checking if the user has any configured permissions. If any records exist for the user *in any branch*, the fallback to full access is disabled.
*Better yet*, change the default behavior: if a user has no configured permissions, **default to Deny Access** (`return false`) rather than Allow Access.

### Recommendation 2: Strict Service-Layer Security Checks
Implement `EnsurePermission` checks in the following query methods:
* `ItemService.SearchAsync` and `GetByIdAsync` → require `PermissionKeys.ProductsView` or `PermissionKeys.InventoryView`.
* `PartyService.SearchAsync` and `GetPartySummaryAsync` → require matching view permissions based on party type (`CustomersView`, `PurchasesView`, or `EmployeesView`).
* `StockCalculationService.GetCurrentStockAsync` and `GetStockValuationAsync` → require `PermissionKeys.InventoryView`.
* `RecipeService.GetRecipeByProducedItemIdAsync` → require `PermissionKeys.ProductionView`.

### Recommendation 3: Enforce UI Authorization
* **Reports**: In `ReportsViewModel`, filter the `ReportCategories` collection dynamically based on the current user's active permissions.
* **Dashboard**: In `DashboardViewModel`, hide metrics cards and do not fetch details for cards the user has no permissions to view.

### Recommendation 4: Auditing Authorization Failures
Create an aspect or interception mechanism (or wrap service operations in a try-catch block) to log all instances of `UnauthorizedAccessException` to the audit table with description details:
* *"User [X] attempted unauthorized action: [ActionName] on [Entity] in branch [BranchName]."*

### Recommendation 5: Predefined Roles (Role-Based Access Control)
Instead of forcing managers to manually check 30+ separate checkboxes for every user, introduce predefined roles:
* **System Administrator**: All permissions.
* **Branch Manager**: Working day management, employee management, sales, purchases, treasury view, and basic safes.
* **Accountant**: Financial reports, ledger entries, supplier/customer management, all safes.
* **Cashier**: Sales create, cash-in/out for daily safe, view daily safe balance.
* **Baker / Production Supervisor**: Production view/create, recipe view.

These roles will be mapped to lists of permission keys automatically, greatly simplifying the user creation and management workflow.
