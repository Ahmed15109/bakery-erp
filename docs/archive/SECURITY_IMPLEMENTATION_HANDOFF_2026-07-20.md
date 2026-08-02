> Superseded by `SECURITY_IMPLEMENTATION_FINAL_2026-07-20.md`, which records the completed continuation and final verification.

# Users, Roles, Permissions, and Security — Implementation Handoff

Date: 2026-07-20  
Workspace: `C:\Users\Ahmed\OneDrive\Desktop\bakery`

## Verification status

- Release solution build: **passed**, 0 errors.
- Full integration suite after the final code changes: **142 passed, 0 failed, 0 skipped**.
- Security-focused suite: **70 passed, 0 failed**.
- EF Core model verification: **no pending model changes**.
- Migrations generated for persistent security roles, the Working Day open-day constraint, and the audit-history index.
- Backup, Restore, Recovery, Database Maintenance, and System Reset were intentionally not changed.

The remaining build warnings are NU1701 compatibility warnings for OpenTK 3.3.1, OpenTK.GLWpfControl 3.3.0, and SkiaSharp.Views.WPF 3.119.0. EF also reports pre-existing enum-default sentinel warnings for `Safe.Type` and `SafeMovement.Origin`.

## Completed tasks

### Authentication and session security

- Removed pre-authentication account enumeration from the login screen and backend lookup flow.
- Added generic login failures, failed-attempt tracking, lockout fields, security stamps, and forced first-login password changes.
- Password minimum increased to 12 characters.
- Removed production hardcoded bootstrap credentials. Empty installations now require `BAKERY_BOOTSTRAP_ADMIN_USERNAME` and `BAKERY_BOOTSTRAP_ADMIN_PASSWORD`.
- Added runtime security-stamp validation; deleted, disabled, or permission-modified users lose their active session.
- Added immediate current-session invalidation when the current user's assigned role changes.
- Added double-submit protection to login and close-day commands.

### Users

- User create/update/delete/activate/deactivate/password reset/password change paths are service-authorized.
- Username normalization and filtered uniqueness are persisted.
- User updates are serializable and RowVersion-aware.
- Direct permissions, persistent roles, branch assignments, and safe ACLs are saved atomically.
- Protected self-delete, self-deactivation, self safe-ACL mutation, and last-effective-administrator scenarios.
- Password reset forces password change at next login.
- User list displays assigned roles.

### Persistent roles

- Added persistent `Role`, `RolePermission`, and `UserRole` entities and migrations.
- Added create/edit/delete/search role service with permission hierarchy validation, RowVersion concurrency, audit history, protected roles, duplicate-name validation, assigned-role deletion protection, and last-administrator protection.
- Added Arabic RTL role list and grouped permission editor UI.
- Added role assignment to the user editor while preserving role/safe assignments when the caller lacks permission to manage them.

### Permission model

- Centralized dependency enforcement in `PermissionPolicyCatalog`; the UI consumes the same dependency source.
- Added business permissions for cancellation, product cost visibility, payroll visibility/management, report production/print/export, Working Day view, password reset, roles, and audit history.
- Separated invoice view/create/edit/delete/cancel/print permissions.
- Added parent dependencies for treasury manual cash operations and accounting ledger operations.
- Completed Arabic permission names, descriptions, and categories.
- Preserved the legacy `Employees.Salaries` key for compatibility while moving enforcement to `Employees.ViewSalary` and `Employees.ManagePayroll`.

### Service and data authorization

- Added or tightened service checks across items, units, inventory, parties, employees, payroll, recipes, production, invoices, treasury, reports, branches, settings, Working Day, users, and roles.
- Party balances and summaries are now authorized according to the actual party type; employee compensation requires salary visibility.
- Product purchase cost, stock valuation, and production cost aggregates are redacted unless cost/report permission allows them.
- Working Day entity lookup now requires an operational permission; internal lifecycle calls use a private core query.
- Safe-level access is default-deny for non-super-admin users.
- Hidden safe balances remain hidden to the client while internal withdrawal validation uses the real ledger balance.
- Balance-sensitive safe withdrawals, transfers, manual movements, reversals, settlements, and party payments use serializable transaction boundaries where implemented in this batch.
- Authenticated authorization denials create durable `AuthorizationDenied` audit rows using an isolated DbContext so unrelated tracked changes cannot be committed accidentally.
- Audit writes validate stale actor/branch IDs and fall back safely instead of breaking the primary operation.

### Navigation, commands, reports, and UI

- Added centralized navigation policy and enforced it in direct/deep navigation.
- Sidebar reacts to authorization/session changes.
- Dashboard summary data is permission-redacted; zero-permission users do not fetch sensitive summary data.
- Dashboard quick-action commands now expose permission-aware CanExecute states.
- Report categories are filtered by report permission.
- Report view, print, and export are independently service-authorized.
- Invoice printing loads the exact selected invoice through a print-authorized service path.
- Added Arabic RTL audit-history viewer and CSV export.
- Added forced password-change dialog, role management screens, permission grouping, loading states, validation, and empty-state behavior.

### Working Day and database integrity

- Preserved transactional close/open-next-day behavior and stale-request/idempotency protections already present in the reviewed Working Day implementation.
- Reopen logic avoids leaving an invalid empty successor active.
- Updated the one-open-day filtered unique index to exclude soft-deleted rows.
- Added `{BranchId, OccurredAt}` audit-history index.
- Added matching join query filters for soft-deleted Users, Roles, Permissions, and Branches.

## Tests added or strengthened

- Role create/update/delete persistence and audit lifecycle.
- Audit query permission enforcement and branch isolation.
- Durable audit record for authenticated authorization denial.
- Pre-authentication user enumeration prevention.
- Hidden-balance withdrawal using the actual financial balance.
- Two-context concurrent withdrawal test proving the safe cannot be overdrawn.
- Updated user/password/bootstrap tests for 12-character passwords and environment-only bootstrap credentials.
- Updated branch, payroll, safe ACL, and service authorization tests to use the new exact permissions.

Test artifacts:

- `Bakery.IntegrationTests\TestResults\security-final.trx`: 70/70 passed.
- `Bakery.IntegrationTests\TestResults\full-final-after-audit.trx`: 142/142 passed.

## Files modified

### Application and domain

- `Bakery.Application/DTOs/AuditDtos.cs`
- `Bakery.Application/DTOs/AuthDtos.cs`
- `Bakery.Application/DTOs/UserManagementDtos.cs`
- `Bakery.Application/Interfaces/IAuditQueryService.cs`
- `Bakery.Application/Interfaces/IInvoiceService.cs`
- `Bakery.Application/Interfaces/IPermissionService.cs`
- `Bakery.Application/Interfaces/IUserManagementService.cs`
- `Bakery.Application/Interfaces/IUserSessionService.cs`
- `Bakery.Application/Security/PermissionCatalog.cs`
- `Bakery.Application/Security/PermissionPolicyCatalog.cs`
- `Bakery.Domain/Entities/SecurityEntities.cs`

### Infrastructure and reporting

- `Bakery.Infrastructure/Configurations/CoreConfigurations.cs`
- `Bakery.Infrastructure/Configurations/SecurityConfigurations.cs`
- `Bakery.Infrastructure/Data/BakeryDbContext.cs`
- `Bakery.Infrastructure/Security/PasswordHasher.cs`
- `Bakery.Infrastructure/Seeders/DefaultDataSeeder.cs`
- `Bakery.Infrastructure/Services/AuditQueryService.cs`
- `Bakery.Infrastructure/Services/AuditService.cs`
- `Bakery.Infrastructure/Services/AuthService.cs`
- `Bakery.Infrastructure/Services/BranchService.cs`
- `Bakery.Infrastructure/Services/DependencyInjection.cs`
- `Bakery.Infrastructure/Services/EmployeeService.cs`
- `Bakery.Infrastructure/Services/EmployeeWageService.cs`
- `Bakery.Infrastructure/Services/InventoryService.cs`
- `Bakery.Infrastructure/Services/ItemService.cs`
- `Bakery.Infrastructure/Services/JobRoleService.cs`
- `Bakery.Infrastructure/Services/PartyPaymentService.cs`
- `Bakery.Infrastructure/Services/PartyService.cs`
- `Bakery.Infrastructure/Services/PermissionService.cs`
- `Bakery.Infrastructure/Services/ProductionService.cs`
- `Bakery.Infrastructure/Services/PurchaseInvoiceService.cs`
- `Bakery.Infrastructure/Services/RecipeService.cs`
- `Bakery.Infrastructure/Services/RoleManagementService.cs`
- `Bakery.Infrastructure/Services/SafeService.cs`
- `Bakery.Infrastructure/Services/SaleInvoiceService.cs`
- `Bakery.Infrastructure/Services/SettlementService.cs`
- `Bakery.Infrastructure/Services/StockCalculationService.cs`
- `Bakery.Infrastructure/Services/UnitService.cs`
- `Bakery.Infrastructure/Services/UserManagementService.cs`
- `Bakery.Infrastructure/Services/UserSafePermissionService.cs`
- `Bakery.Infrastructure/Services/UserSessionService.cs`
- `Bakery.Infrastructure/Services/WorkingDayService.cs`
- `Bakery.Reporting/Services/InventoryReportService.cs`
- `Bakery.Reporting/Services/ReportPdfGenerator.cs`
- `Bakery.Shared/Helpers/Loc.cs`

### Migrations

- `Bakery.Infrastructure/Migrations/20260719220500_ProductionSecurityRoles.cs` and designer
- `Bakery.Infrastructure/Migrations/20260720073312_WorkingDayOpenIndexSoftDelete.cs` and designer
- `Bakery.Infrastructure/Migrations/20260720080227_AuditLogBranchOccurredAtIndex.cs` and designer
- `Bakery.Infrastructure/Migrations/BakeryDbContextModelSnapshot.cs`

### WPF

- `Bakery.WPF/App.xaml.cs`
- `Bakery.WPF/Authorization/NavigationAuthorizationPolicy.cs`
- `Bakery.WPF/Authorization/PermissionAssist.cs`
- `Bakery.WPF/LoginWindow.xaml`
- `Bakery.WPF/MainWindow.xaml`
- `Bakery.WPF/Services/DialogService.cs`
- `Bakery.WPF/Services/INavigationService.cs`
- `Bakery.WPF/Services/NavigationService.cs`
- `Bakery.WPF/Services/Print/PrintServices.cs`
- `Bakery.WPF/ViewModels/AuditLogViewModel.cs`
- `Bakery.WPF/ViewModels/DashboardViewModel.cs`
- `Bakery.WPF/ViewModels/InvoiceWorkspaceViewModel.cs`
- `Bakery.WPF/ViewModels/LoginViewModel.cs`
- `Bakery.WPF/ViewModels/MainViewModel.cs`
- `Bakery.WPF/ViewModels/NavigationItemViewModel.cs`
- `Bakery.WPF/ViewModels/OperationsViewModels.cs`
- `Bakery.WPF/ViewModels/ReportDetailsViewModel.cs`
- `Bakery.WPF/ViewModels/RoleManagementViewModels.cs`
- `Bakery.WPF/ViewModels/UserManagementViewModels.cs`
- `Bakery.WPF/Views/AuditLogView.xaml` and code-behind
- `Bakery.WPF/Views/ChangePasswordDialog.xaml` and code-behind
- `Bakery.WPF/Views/InvoiceWorkspaceView.xaml`
- `Bakery.WPF/Views/RoleFormDialog.xaml` and code-behind
- `Bakery.WPF/Views/RolesView.xaml` and code-behind
- `Bakery.WPF/Views/UserFormDialog.xaml`
- `Bakery.WPF/Views/UsersView.xaml`

### Tests

- `Bakery.IntegrationTests/BranchSessionWorkflowTests.cs`
- `Bakery.IntegrationTests/DatabaseFixture.cs`
- `Bakery.IntegrationTests/SecurityProductionHardeningTests.cs`
- `Bakery.IntegrationTests/ServiceSecurityAuditTests.cs`
- `Bakery.IntegrationTests/UserManagementAndSecurityTests.cs`
- `Bakery.IntegrationTests/UserSafePermissionTests.cs`

## Remaining tasks

1. Apply the three new migrations to a production-like copy of the real database and verify existing users, direct permissions, role assignments, soft-deleted Working Days, and audit history.
2. Perform a manual Arabic RTL walkthrough of login, forced password change, users, roles, audit history, branch switching, dashboard actions, every report category, print/export, Working Day close/reopen, and treasury operations.
3. Add focused tests for effective permissions across multiple assigned roles, role deletion/soft-deletion during active sessions, and simultaneous role/user edits from separate DbContexts.
4. Decide and document whether low-level DI services are an internal trust boundary or must enforce operator permission themselves.
5. Review and replace the legacy OpenTK/SkiaSharp packages with versions that explicitly support the target frameworks.
6. Resolve EF enum sentinel warnings for `Safe.Type` and `SafeMovement.Origin` after confirming the intended database defaults.
7. In a separately authorized task, audit and implement Backup, Restore, Recovery, Database Maintenance, and System Reset security. Those modules remain unchanged.

## Pending Critical issues

- **None known inside the implemented scope after the passing Release build and 142-test integration run.**
- No production-readiness assertion is made for the explicitly excluded Backup/Restore/Recovery/Database Maintenance/System Reset subsystem.

## Pending High issues

- **Deferred subsystem security:** Backup, Restore, Recovery, Database Maintenance, and System Reset remain intentionally untouched and must receive their own production security review.
- **Production migration rehearsal:** the migrations are generated and model-complete, but have not been applied to a copy of the user's real production database in this batch.
- **Low-level service trust boundary:** `IBranchProvisioningService`, `ISystemSafeService`, and `IAttachmentStorageService` are publicly resolvable through DI and rely on authorized higher-level callers. Direct resolution is a potential bypass unless these interfaces are formally internalized or protected with an internal capability.
- **Financial request idempotency:** serializable balance protection prevents overdrafts, but manual deposits/withdrawals, transfers, and party payments do not all accept a caller-supplied idempotency key. A duplicated UI submission can therefore create two individually valid movements.

## Pending Medium issues

- Add cross-DbContext concurrency tests for role edits, user edits, party payments, and manual reversal races beyond the withdrawal concurrency test added here.
- A serializable treasury conflict can surface as a failed operation requiring retry; there is no general retry policy for deadlock victims.
- Authorization-denied auditing is synchronous and opens an isolated connection. This preserves data safety but should be load-tested before high-volume deployment.
- The legacy `Employees.Salaries` permission remains for compatibility and should be formally deprecated/migrated after confirming no external references.
- No automated WPF interaction test covers every CanExecute state or Arabic RTL visual state; service tests and compilation pass, but a manual UI acceptance pass is still required.
- NU1701 third-party package compatibility warnings remain.
- EF enum sentinel warnings remain for `Safe.Type` and `SafeMovement.Origin`.

## Known risks

- Deployment without the bootstrap environment variables will intentionally fail on an empty database rather than create a known administrator password.
- Existing custom users may need role/permission cleanup after migration because new dependency rules reject inconsistent permission sets during subsequent edits.
- Default-deny safe ACL behavior may remove access from non-super-admin users who previously relied on the insecure implicit-allow fallback; administrators must explicitly assign safe access.
- Production migration and manual workflow verification are still required before release approval.
- The excluded recovery subsystem prevents declaring the entire application production-ready even though the in-scope test suite is green.
- The incomplete-code marker scan found only existing `ConvertBack` methods that intentionally throw `NotImplementedException` in one-way WPF converters; no partial implementation marker was found in the services or screens added by this batch.

## Recommended next implementation order

1. Clone the real database and rehearse all three migrations; validate data counts and rollback scripts.
2. Execute the Arabic RTL manual acceptance matrix for Users, Roles, Audit, Navigation, Reports, Working Day, and Treasury.
3. Close the low-level DI authorization boundary (`BranchProvisioning`, `SystemSafe`, attachment storage) without touching recovery code.
4. Add idempotency keys and duplicate-submit integration tests for manual treasury and party-payment commands.
5. Add remaining cross-context role/user/payment concurrency tests.
6. Resolve enum sentinel and third-party package warnings.
7. Start a separately approved Backup/Restore/Recovery security batch.

## Commands used for final verification

```powershell
dotnet build .\BakeryERP.sln -c Release --no-restore
dotnet test .\Bakery.IntegrationTests\Bakery.IntegrationTests.csproj -c Release --no-build --logger "trx;LogFileName=full-final-after-audit.trx"
dotnet ef migrations has-pending-model-changes --project .\Bakery.Infrastructure\Bakery.Infrastructure.csproj --startup-project .\Bakery.WPF\Bakery.WPF.csproj
```
