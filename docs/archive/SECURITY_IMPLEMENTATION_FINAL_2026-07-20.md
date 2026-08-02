# Users, Roles, Permissions, and Authorization — Final In-Scope Report

Date: 2026-07-20

## Stabilization and verification

- Release solution build: passed with 0 errors.
- Full integration suite: 165 passed, 0 failed, 0 skipped.
- Security-focused suite: 76 passed, 0 failed, 0 skipped.
- Financial concurrency/idempotency suite: 8 passed, 0 failed.
- EF Core model check: no pending model changes.
- Current-batch incomplete-marker scan: no TODO/FIXME/placeholder implementations found.

## Completed continuation work

- Separated ordinary user-profile editing from permission, role, branch, safe-access, and password-reset authority. Omitted assignment collections now preserve existing assignments.
- Centralized sidebar and deep-link navigation authorization on the same navigation policy.
- Added stable request idempotency keys for manual deposits, manual withdrawals, transfers, and party payments, with UI duplicate-submit guards and loading states.
- Added a branch-scoped filtered unique index for financial idempotency keys.
- Added a filtered unique index preventing more than one active reversal for the same original safe movement. The migration performs a duplicate-data preflight before creating the index.
- Added cross-DbContext tests for users, roles, manual cash, transfers, party payments, withdrawals, and reversal races.
- Verified effective permission union across multiple roles and immediate invalidation when an assigned role changes.
- Verified deletion of an assigned active-session role is rejected.
- Protected direct DI mutation entry points for branch provisioning, explicit system-safe provisioning, and attachment storage.
- Protected low-level party lookup, party-statement, employee-statement, and uniqueness-validation services.
- Employee financial statements now require `Employees.ViewSalary`, not only general employee visibility; the UI command matches this rule.
- Kept deterministic system-safe self-healing used by established operational flows, while protecting the explicit provisioning entry point.
- Fixed Working Day open/reopen result construction so summary generation completes before transaction commit; failures can no longer attempt to roll back an already committed transaction.
- Explicitly configured invalid enum value `0` as the EF sentinel for `Safe.Type` and `SafeMovement.Origin`, removing the enum-default warnings without changing the database schema.

## Permissions

- No new permission keys were required in this continuation.
- Enforcement was tightened for `Users.Edit`, `Users.ChangePermissions`, `Users.ResetPassword`, `Roles.Assign`, `Settings.BranchManagement`, `Treasury.ManageSafes`, `Cash.Deposit`, `Cash.Withdraw`, and `Employees.ViewSalary`.
- Existing centralized permission dependency and Arabic localization catalogs remain the source of truth.

## Migrations

- `20260720093329_FinancialOperationIdempotency`
- `20260720100657_ManualReversalUniqueness`
- `BakeryDbContextModelSnapshot` updated and verified.

## Files changed in this continuation

### Application/domain

- `Bakery.Application/DTOs/Accounting/AccountingDtos.cs`
- `Bakery.Application/DTOs/UserManagementDtos.cs`
- `Bakery.Application/Interfaces/IPartyPaymentService.cs`
- `Bakery.Application/Interfaces/ISafeService.cs`
- `Bakery.Domain/Entities/SafeEntities.cs`

### Infrastructure

- `Bakery.Infrastructure/Configurations/SafeConfigurations.cs`
- `Bakery.Infrastructure/Seeders/DefaultDataSeeder.cs`
- `Bakery.Infrastructure/Services/AttachmentStorageService.cs`
- `Bakery.Infrastructure/Services/BranchProvisioningService.cs`
- `Bakery.Infrastructure/Services/EmployeeStatementProvider.cs`
- `Bakery.Infrastructure/Services/PartyLookupService.cs`
- `Bakery.Infrastructure/Services/PartyPaymentService.cs`
- `Bakery.Infrastructure/Services/PartyStatementProvider.cs`
- `Bakery.Infrastructure/Services/SafeService.cs`
- `Bakery.Infrastructure/Services/StatementService.cs`
- `Bakery.Infrastructure/Services/SystemSafeService.cs`
- `Bakery.Infrastructure/Services/UserManagementService.cs`
- `Bakery.Infrastructure/Services/ValidationService.cs`
- `Bakery.Infrastructure/Services/WorkingDayService.cs`
- The two migrations above and their designers/snapshot.

### WPF

- `Bakery.WPF/ViewModels/AccountingViewModels.cs`
- `Bakery.WPF/ViewModels/MainViewModel.cs`
- `Bakery.WPF/ViewModels/NavigationItemViewModel.cs`
- `Bakery.WPF/ViewModels/PartyPaymentDialogViewModel.cs`
- `Bakery.WPF/ViewModels/TreasuryTransactionDialogViewModel.cs`
- `Bakery.WPF/ViewModels/TreasuryTransferDialogViewModel.cs`
- `Bakery.WPF/ViewModels/UserManagementViewModels.cs`
- `Bakery.WPF/Views/PartiesView.xaml`
- `Bakery.WPF/Views/UserFormDialog.xaml`
- `Bakery.WPF/TreasuryTransactionDialog.xaml`
- `Bakery.WPF/TreasuryTransferDialog.xaml`

### Tests

- `Bakery.IntegrationTests/FinancialIdempotencyTests.cs`
- `Bakery.IntegrationTests/NavigationAuthorizationTests.cs`
- `Bakery.IntegrationTests/SecurityProductionHardeningTests.cs`
- `Bakery.IntegrationTests/ServiceSecurityAuditTests.cs`
- `Bakery.IntegrationTests/TreasurySelectionViewModelTests.cs`
- `Bakery.IntegrationTests/UserAuthorizationBoundaryTests.cs`
- `Bakery.IntegrationTests/UserManagementViewModelTests.cs`

## Remaining acceptance and deployment work

- Apply all pending migrations to a production-like copy of the real database. Resolve any duplicate active `OriginalTransactionId` values reported by the new preflight before deployment.
- Manually verify Arabic RTL screens and keyboard/mouse workflows for Users, Roles, permissions, statements, navigation, treasury operations, reports, and Working Day.
- Rehearse migration backup/rollback procedures in the deployment environment without changing the postponed application subsystems.
- Load-test synchronous authorization-denial auditing under expected production concurrency.

## Known deferred risks and technical debt

- NU1701 compatibility warnings remain for OpenTK 3.3.1, OpenTK.GLWpfControl 3.3.0, and SkiaSharp.Views.WPF 3.119.0; no risky package upgrade was made.
- The local EF CLI is 8.0.0 while the runtime is 8.0.20; model verification still passes.
- SQL deadlock victims may require a retry. Idempotency keys make retries safe, but there is no application-wide transient retry policy.
- The legacy `Employees.Salaries` compatibility permission remains intentionally supported.
- WPF visual/interaction coverage is manual; service, ViewModel, build, and integration verification are automated.

## Scope exclusion

Backup, Restore, Recovery, Database Maintenance, and System Reset were not modified. Their pending work remains intentionally separate.

## Readiness conclusion

The implemented Users/Roles/Permissions/Authorization and related in-scope financial hardening are stable and ready for manual acceptance and production-like migration rehearsal. Production deployment should wait for those two checks. No known Critical or High code defect remains inside this completed scope.
