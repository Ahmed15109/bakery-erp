# User Management & Permission System - Implementation Handoff

Date: 2026-07-05

Status: current implementation step stopped after build stabilization. No further feature work should be assumed complete beyond what is listed here.

Build verification:

```powershell
dotnet build BakeryERP.sln --no-restore
```

Result: build succeeded with 0 errors and 3 existing package compatibility warnings from `Bakery.WPF` for `OpenTK`, `OpenTK.GLWpfControl`, and `SkiaSharp.Views.WPF`.

## 1. Work Completed

### Permission catalog and permission keys

Implemented a centralized permission catalog in `Bakery.Application.Security.PermissionCatalog`.

Why:

- The project needed an extendable list of permissions that can be shown as checkboxes in the UI and reused by services.
- Permission string literals spread across modules would be brittle and difficult to audit.

Business requirement satisfied:

- "Every user will have permissions assigned individually using checkboxes."
- "This list should be easy to extend by simply adding new permissions later."
- `PermissionService.HasPermission("Sales.Create")` style checks are now backed by constants.

### User-permission domain model

Reworked security entities from role-based assignment to direct user-permission assignment.

Why:

- The user explicitly required no roles and custom permissions per user.
- A direct many-to-many model between users and permissions matches the requested database design.

Business requirement satisfied:

- Users now have username, full name, password hash, active status, audit fields from `BaseEntity`, and a custom permission list.
- Roles are no longer part of the active domain model.

### Authentication model updated

Updated authentication DTOs and `AuthService` to authenticate active users by `Username` and load direct permissions from `UserPermissions`.

Why:

- Login must work from the new user model.
- The current session needs permission keys immediately after authentication so navigation and authorization checks can work.

Business requirement satisfied:

- Login accepts username and password.
- Inactive users are blocked.
- Passwords remain verified by the existing password hashing service.
- Current user identity includes user id, username, full name, and permissions.

### Current user session service

Extended `IUserSessionService` through a new `ICurrentUserService` contract and updated `UserSessionService` to expose:

- `UserId`
- `Username`
- `FullName`
- `Permissions`
- `IsAuthenticated`

Why:

- Application layers need read-only current-user access without depending on session mutation methods.
- DI can inject `ICurrentUserService` wherever identity context is needed.

Business requirement satisfied:

- "Create a CurrentUser service that exposes UserId, Username, FullName, Permissions."
- "Accessible throughout the application using Dependency Injection."

### Permission service updated

Updated `PermissionService` to evaluate permissions from the current user's direct permission set and added `EnsurePermission`.

Why:

- Existing role/admin-based checks no longer fit the model.
- Services need a consistent way to fail fast when a permission is missing.

Business requirement satisfied:

- `PermissionService.HasPermission(...)` works against direct user permissions.
- Service-layer protection exists in many key modules.
- Missing permissions raise a consistent authorization error.

### User management application contract and service

Added `IUserManagementService`, user management DTOs, and `UserManagementService`.

Implemented capabilities:

- Search/list users.
- Load user details for editing.
- Load all permission definitions.
- Create user with password confirmation handled at VM level and hashed password at service level.
- Update user information and permissions.
- Enable/disable users.
- Reset password.
- Delete user when business rules allow.
- Prevent self-disable and self-delete.
- Prevent removal of the last active user.
- Validate unique usernames.
- Require at least one permission.

Why:

- Keeps user-management business rules outside the WPF view layer.
- Reuses EF Core, audit logging, password hashing, exception translation, and existing service patterns.

Business requirement satisfied:

- Users module operations are implemented.
- Passwords are stored as hashes, never plain text.
- Delete is guarded by business rules.
- Service layer is protected with `Settings.UserManagement`.

### Seed data

Reworked `DefaultDataSeeder` to seed:

- All permissions from `PermissionCatalog.All`.
- An administrator user when no admin exists or no users exist.
- Username: `admin`
- Password: `admin123`
- Full name: `Administrator`
- Active status: true
- All permissions assigned directly through `UserPermissions`.

Why:

- The application needs a bootstrap account after database creation.
- The administrator must be able to access every module without roles.

Business requirement satisfied:

- "Automatically create one administrator account."
- "This administrator must automatically receive ALL permissions."
- "If no users exist, recreate this administrator."

### Users WPF module

Added a new Users page, dialog, reset password dialog, and related view models.

Implemented UI capabilities:

- View users in a DataGrid.
- Search users.
- Add user.
- Edit user.
- Enable/disable user.
- Reset password.
- Delete user when allowed.
- Basic user form fields: full name, username, password, confirm password, active checkbox.
- Permissions shown as grouped expandable sections.
- Permission search.
- Select all and unselect all permissions.

Why:

- The project uses WPF, MVVM, CommunityToolkit.Mvvm, MaterialDesign styling, DI, and existing navigation patterns.
- Grouped expandable permissions keep a long permission list usable and extendable.

Business requirement satisfied:

- "Create a new module called Users."
- "Professional dialog containing Basic Information and Permissions."
- "Permissions should be grouped by category inside expandable sections."
- "Support search permission, select all, unselect all."

### Navigation integration

Updated navigation item model and main navigation composition so nav items can declare required permissions.

Why:

- Users without a permission should not see inaccessible modules as first-class navigation entries.
- Navigation filtering should be centralized rather than duplicated in each view.

Business requirement satisfied:

- "If permission is missing: Disable the button OR hide it."
- "Use existing navigation."

### Service-layer authorization guards

Added permission checks to many existing infrastructure and reporting services.

Covered areas:

- Inventory and stock counts.
- Products/items and units.
- Sales invoices.
- Purchase invoices.
- Customers/suppliers/employees through party services.
- Party payments.
- Treasury and safes.
- Production and recipes.
- Waste.
- Employees, job roles, wages, and settlements.
- Working day open/close/reopen.
- Settings.
- System reset.
- Backup/restore.
- Inventory reports.
- Accounting reports.

Why:

- UI-only security is not enough.
- Commands can be executed from view models, tests, or future integrations, so the business layer must enforce permissions.

Business requirement satisfied:

- "Protect the Service Layer as well."
- "UI restrictions alone are NOT enough."

### Integration test fixture compatibility

Updated integration test setup to sign in a test user with all permission keys.

Why:

- Newly added service-layer authorization would otherwise make existing integration tests fail before reaching the behavior under test.

Business requirement satisfied:

- Maintains testability while preserving authorization enforcement.

## 2. Architecture Decisions

### Direct permissions instead of roles

The previous security model used `Role` and role-permission relationships. The requested business model explicitly rejected roles, so the implementation changed to:

- `User`
- `Permission`
- `UserPermission`

This gives every user an independent permission set and keeps future permission additions simple.

### Centralized permission catalog

`PermissionCatalog` is in the Application layer because:

- UI needs it to display permissions.
- Infrastructure seeding needs it to populate the database.
- Services need stable permission constants.
- It is business/application knowledge, not persistence-specific infrastructure.

Adding a new permission should generally mean adding one `PermissionDefinition` and one constant, then using that constant wherever authorization is required.

### Current user as a read-only application contract

`ICurrentUserService` separates read-only identity access from `IUserSessionService`, which can sign in/sign out.

This matches the existing architecture by:

- Keeping session state in Infrastructure.
- Exposing contracts from Application.
- Resolving through DI.

### Service-layer authorization

Authorization was placed in services rather than only in WPF because the service layer is the business boundary.

Pattern used:

```csharp
_permissionService.EnsurePermission(PermissionKeys.SomePermission);
```

Some existing methods that return tuple-style results use `HasPermission` and return an existing localized failure string to preserve the method contract.

### WPF MVVM module structure

The Users module follows existing WPF project conventions:

- ViewModel in `Bakery.WPF/ViewModels`.
- View/dialogs in `Bakery.WPF/Views`.
- Commands from CommunityToolkit.Mvvm.
- Dialogs opened as WPF windows, consistent with many existing dialogs in the project.
- Errors translated through `IExceptionTranslator` and shown through `IMessageService`.

### Admin is permission-complete, not role-based

`PermissionService.IsAdmin()` now means "current user has every known permission in `PermissionCatalog.All`."

This preserves places that ask "is admin?" while aligning with the no-role requirement.

## 3. Files Changed

### Created files

- `Bakery.Application/Security/PermissionCatalog.cs`
  - Defines `PermissionKeys`, `PermissionDefinition`, and the complete grouped permission list.

- `Bakery.Application/DTOs/UserManagementDtos.cs`
  - Defines DTOs for permission display, user list rows, user details, save user requests, and reset password requests.

- `Bakery.Application/Interfaces/ICurrentUserService.cs`
  - Read-only current-user contract for DI consumers.

- `Bakery.Application/Interfaces/IUserManagementService.cs`
  - User management service contract.

- `Bakery.Infrastructure/Services/UserManagementService.cs`
  - Business logic for user CRUD, enable/disable, password reset, delete checks, validation, auditing, and permission assignment.

- `Bakery.WPF/ViewModels/UserManagementViewModels.cs`
  - Contains `UsersViewModel`, `UserFormDialogViewModel`, `PermissionCategoryViewModel`, `PermissionSelectionViewModel`, and `ResetPasswordDialogViewModel`.

- `Bakery.WPF/Views/UsersView.xaml`
  - Users management page.

- `Bakery.WPF/Views/UsersView.xaml.cs`
  - Users page code-behind.

- `Bakery.WPF/Views/UserFormDialog.xaml`
  - Add/edit user dialog with basic information and grouped permissions.

- `Bakery.WPF/Views/UserFormDialog.xaml.cs`
  - PasswordBox synchronization and dialog code-behind.

- `Bakery.WPF/Views/ResetPasswordDialog.xaml`
  - Reset password dialog.

- `Bakery.WPF/Views/ResetPasswordDialog.xaml.cs`
  - PasswordBox synchronization and dialog code-behind.

- `IMPLEMENTATION_HANDOFF.md`
  - This handoff document.

### Modified files

- `Bakery.Domain/Entities/SecurityEntities.cs`
  - Replaced role-based security entities with direct user-permission model.

- `Bakery.Application/DTOs/AuthDtos.cs`
  - Updated authenticated user DTO to carry username, full name, and direct permission keys while retaining compatibility aliases.

- `Bakery.Application/Interfaces/IUserSessionService.cs`
  - Made session service inherit `ICurrentUserService`.

- `Bakery.Application/Interfaces/IPermissionService.cs`
  - Added `EnsurePermission`.

- `Bakery.Infrastructure/Data/BakeryDbContext.cs`
  - Removed `Roles` DbSet and added `UserPermissions`.

- `Bakery.Infrastructure/Configurations/SecurityConfigurations.cs`
  - Updated EF configuration for `User`, `Permission`, and new `UserPermission` composite join.

- `Bakery.Infrastructure/Seeders/DefaultDataSeeder.cs`
  - Seeds permissions and the `admin` account with all permissions.

- `Bakery.Infrastructure/Services/UserSessionService.cs`
  - Exposes current-user properties from the signed-in user.

- `Bakery.Infrastructure/Services/PermissionService.cs`
  - Uses direct user permissions and implements `EnsurePermission`.

- `Bakery.Infrastructure/Services/AuthService.cs`
  - Loads direct permissions during login and signs in active users by username.

- `Bakery.Infrastructure/Services/DependencyInjection.cs`
  - Registers current user/session mapping and user management service.

- `Bakery.Infrastructure/Services/ValidationService.cs`
  - Updated user validation references from old username property naming to `Username`.

- `Bakery.Infrastructure/Services/ExceptionTranslator.cs`
  - Handles authorization/invalid-operation messages and updated user unique-index naming.

- `Bakery.Infrastructure/Services/InventoryService.cs`
  - Added authorization checks for stock adjustments and inventory counts.

- `Bakery.Infrastructure/Services/ItemService.cs`
  - Added product permission checks for create/edit/delete/activation.

- `Bakery.Infrastructure/Services/UnitService.cs`
  - Added product permission checks for unit operations and item-unit updates.

- `Bakery.Infrastructure/Services/SaleInvoiceService.cs`
  - Added sales permission checks for list/save/post/cancel/print.

- `Bakery.Infrastructure/Services/PurchaseInvoiceService.cs`
  - Added purchase permission checks for list/save/post/cancel.

- `Bakery.Infrastructure/Services/PartyService.cs`
  - Added customer, supplier, employee, and accounting-oriented permission checks based on party type.

- `Bakery.Infrastructure/Services/PartyPaymentService.cs`
  - Added treasury cash-in/cash-out permission checks.

- `Bakery.Infrastructure/Services/SafeService.cs`
  - Added treasury view, cash-in, cash-out, transfer, and manage-safe permission checks.

- `Bakery.Infrastructure/Services/ProductionService.cs`
  - Added production view/create/edit permission checks.

- `Bakery.Infrastructure/Services/RecipeService.cs`
  - Added production permission checks for recipe operations.

- `Bakery.Infrastructure/Services/WasteService.cs`
  - Added inventory view and stock adjustment permission checks.

- `Bakery.Infrastructure/Services/EmployeeService.cs`
  - Added employee view/add/edit/delete permission checks.

- `Bakery.Infrastructure/Services/JobRoleService.cs`
  - Added employee view/edit permission checks for job-role management.

- `Bakery.Infrastructure/Services/EmployeeWageService.cs`
  - Added employee view/edit/delete permission checks for wage management.

- `Bakery.Infrastructure/Services/SettlementService.cs`
  - Added employee view/edit permission checks and split internal balance calculation to avoid self-blocking.

- `Bakery.Infrastructure/Services/WorkingDayService.cs`
  - Replaced old admin/working-day override checks with working-day permissions.

- `Bakery.Infrastructure/Services/SettingsService.cs`
  - Added system settings permission check.

- `Bakery.Infrastructure/Services/SystemResetService.cs`
  - Added system settings permission check.

- `Bakery.Infrastructure/Services/Backup/BackupService.cs`
  - Added system settings permission checks for backup and restore.

- `Bakery.Reporting/Services/InventoryReportService.cs`
  - Added inventory report permission checks.

- `Bakery.Reporting/Services/AccountingReportService.cs`
  - Added sales/financial report permission checks.

- `Bakery.WPF/App.xaml.cs`
  - Registered Users view model/view/dialogs with DI.

- `Bakery.WPF/MainWindow.xaml`
  - Added Users view DataTemplate.

- `Bakery.WPF/ViewModels/NavigationItemViewModel.cs`
  - Added permission-key metadata and exposed navigation command target.

- `Bakery.WPF/ViewModels/MainViewModel.cs`
  - Filters navigation by permissions and adds Users navigation entry.

- `Bakery.WPF/ViewModels/TreasuryViewModel.cs`
  - Uses `WorkingDay.Reopen` permission instead of old admin check.

- `Bakery.WPF/ViewModels/DashboardViewModel.cs`
  - Handles treasury authorization failures when dashboard loads restricted data.

- `Bakery.IntegrationTests/DatabaseFixture.cs`
  - Seeds/signs in a test user with all permissions.

- `Bakery.IntegrationTests/SystemResetTests.cs`
  - Removed obsolete role setup and updated user property usage.

## 4. Database Changes

### New and changed entities

`User`

- `Id`
- `Username`
- `FullName`
- `PasswordHash`
- `IsActive`
- Audit fields inherited from `BaseEntity`
- `ICollection<UserPermission> UserPermissions`
- `ICollection<AuditLog> AuditLogs`

`Permission`

- `Id`
- `Key`
- `DisplayName`
- `Category`
- Audit fields inherited from `BaseEntity`
- `ICollection<UserPermission> UserPermissions`

`UserPermission`

- `UserId`
- `PermissionId`
- `User`
- `Permission`

Removed from active domain model:

- `Role`
- User-to-role relationship
- Role-to-permission relationship

### Relationships

- `User` many-to-many `Permission` through explicit join entity `UserPermission`.
- `UserPermission` has a composite key: `{ UserId, PermissionId }`.
- `UserPermission.UserId` cascades from `User`.
- `UserPermission.PermissionId` cascades from `Permission`.

### EF Core configuration

`SecurityConfigurations.cs` configures:

- `Users` table.
- Required unique `Username`.
- Required `FullName`.
- Required `PasswordHash`.
- `Permissions` table.
- Required unique `Key`.
- Required `DisplayName`.
- Required `Category`.
- `UserPermissions` table with composite key and cascade relationships.

### Migrations

No EF Core migration has been created yet.

This is the largest remaining database task. The next session should create and review a migration that:

1. Renames `Users.UserName` to `Username` if the existing database has `UserName`.
2. Renames `Users.DisplayName` to `FullName` if the existing database has `DisplayName`.
3. Removes `Users.RoleId`.
4. Removes `Roles`.
5. Removes `RolePermissions`.
6. Creates `UserPermissions`.
7. Migrates existing role permissions into direct user permissions before dropping role tables:

```sql
INSERT INTO UserPermissions (UserId, PermissionId)
SELECT DISTINCT u.Id, rp.PermissionId
FROM Users u
JOIN RolePermissions rp ON rp.RoleId = u.RoleId;
```

8. Renames or recreates `Permissions.Name` as `DisplayName` if that column exists.
9. Adds `Permissions.Category` with a safe non-null default for existing rows.
10. Removes obsolete permission description data only after confirming the existing schema.
11. Updates indexes such as `IX_Users_Username` and `IX_Permissions_Key`.
12. Updates the EF model snapshot.

### Seed data

`DefaultDataSeeder` now:

- Reads definitions from `PermissionCatalog.All`.
- Upserts permissions by key.
- Updates display name/category for existing permission keys.
- Ensures `admin` exists.
- Ensures admin is active and not deleted.
- Hashes `admin123` only when creating admin.
- Assigns all permission records to admin.
- Recreates admin when no users exist.

## 5. Services

### New interfaces

`ICurrentUserService`

- Read-only identity and permission access.
- Intended for broad DI use without exposing sign-in/sign-out.

`IUserManagementService`

- Defines all user administration operations used by the WPF Users module.

### Modified interfaces

`IUserSessionService`

- Now inherits `ICurrentUserService`.
- Keeps session mutation methods:
  - `SignIn`
  - `SignOut`
  - `CurrentUser`
  - `HasPermission`

`IPermissionService`

- Added `EnsurePermission(string permissionKey)`.

### New implementation

`UserManagementService`

Responsibilities:

- Enforce `Settings.UserManagement`.
- Search/list users.
- Return user details with permission keys.
- Return permission definitions.
- Validate user save requests.
- Hash passwords for new users and password resets.
- Sync `UserPermissions`.
- Enable/disable users.
- Prevent locking out the current/last active user.
- Soft-delete users when allowed.
- Write audit log entries.

### Modified implementations

`UserSessionService`

- Stores signed-in user state and exposes current identity/permissions.

`PermissionService`

- Checks current session permissions.
- Throws `UnauthorizedAccessException` for missing permissions.
- Treats "admin" as permission-complete rather than role-named.

`AuthService`

- Authenticates active users by `Username`.
- Verifies password hash.
- Loads direct permission keys.
- Signs the user into the session service.

Existing business services now use `IPermissionService` guards as listed in section 3.

## 6. Dependency Injection

Modified registrations in `Bakery.Infrastructure/Services/DependencyInjection.cs`:

```csharp
services.AddSingleton<UserSessionService>();
services.AddSingleton<IUserSessionService>(provider => provider.GetRequiredService<UserSessionService>());
services.AddSingleton<ICurrentUserService>(provider => provider.GetRequiredService<UserSessionService>());
services.AddScoped<IUserManagementService, UserManagementService>();
```

Existing registration retained:

```csharp
services.AddSingleton<IPermissionService, PermissionService>();
```

Modified registrations in `Bakery.WPF/App.xaml.cs`:

```csharp
services.AddTransient<UsersViewModel>();
services.AddTransient<UsersView>();
services.AddTransient<UserFormDialog>();
services.AddTransient<ResetPasswordDialog>();
```

Note:

- `UserFormDialog` and `ResetPasswordDialog` are currently opened manually with explicitly constructed view models. Their DI registration compiles but should be reviewed before runtime use through the container because their constructors require view models.

## 7. UI Changes

### Users page

Files:

- `Bakery.WPF/Views/UsersView.xaml`
- `Bakery.WPF/Views/UsersView.xaml.cs`
- `Bakery.WPF/ViewModels/UserManagementViewModels.cs`

Features:

- Search box bound to `SearchText`.
- Refresh command.
- Add user command.
- Edit user command.
- Enable/disable command.
- Reset password command.
- Delete command.
- Users DataGrid.
- Summary counters for total, active, inactive users.

ViewModel:

- `UsersViewModel` loads user rows from `IUserManagementService`.
- Uses `IMessageService` for confirmation and feedback.
- Uses `IExceptionTranslator` for friendly error messages.
- Opens add/edit/reset dialogs.

### User form dialog

Files:

- `Bakery.WPF/Views/UserFormDialog.xaml`
- `Bakery.WPF/Views/UserFormDialog.xaml.cs`

ViewModel:

- `UserFormDialogViewModel`

Features:

- Full name.
- Username.
- Password.
- Confirm password.
- Active checkbox.
- Permission search.
- Permission category expanders.
- Permission checkboxes.
- Select all visible permissions.
- Unselect all visible permissions.
- Save and cancel.

Implementation notes:

- Password values are synchronized in code-behind because WPF `PasswordBox.Password` is not a normal bindable dependency property.
- Permission categories are built from `PermissionDto.Category`.
- Search filters permission visibility at the permission and category level.
- Save validation requires matching passwords and at least one selected permission.

### Reset password dialog

Files:

- `Bakery.WPF/Views/ResetPasswordDialog.xaml`
- `Bakery.WPF/Views/ResetPasswordDialog.xaml.cs`

ViewModel:

- `ResetPasswordDialogViewModel`

Features:

- New password.
- Confirm password.
- Save and cancel.
- Password confirmation validation.

### Navigation changes

Files:

- `Bakery.WPF/ViewModels/NavigationItemViewModel.cs`
- `Bakery.WPF/ViewModels/MainViewModel.cs`
- `Bakery.WPF/MainWindow.xaml`

Changes:

- Each nav item can carry one or more required permission keys.
- `MainViewModel` filters nav items using `IPermissionService.HasPermission`.
- Users nav entry appears only when the user has `Settings.UserManagement`.
- `MainWindow.xaml` maps `UsersViewModel` to `UsersView`.

## 8. Authentication & Authorization

### Authentication flow

The existing startup flow remains:

1. `App.xaml.cs` initializes services/database.
2. `LoginWindow` is shown before `MainWindow`.
3. `LoginViewModel` calls `IAuthService.LoginAsync`.
4. `AuthService`:
   - Looks up an active user by `Username`.
   - Includes `UserPermissions.Permission`.
   - Verifies the password hash.
   - Creates `AuthenticatedUserDto`.
   - Signs in through `IUserSessionService`.
5. On success, `MainWindow` opens.

### Current user session

`UserSessionService` stores the authenticated DTO and exposes:

- Current user object.
- User id.
- Username.
- Full name.
- Permission keys.
- Authentication status.
- `HasPermission`.

### Authorization checks

UI navigation:

- `MainViewModel` filters module navigation entries using permission keys.

Service layer:

- Services call `_permissionService.EnsurePermission(...)` or `_permissionService.HasPermission(...)`.
- Missing permissions throw `UnauthorizedAccessException` or return an existing tuple failure where method contracts already use tuple results.

Admin semantics:

- There is no role-based admin.
- A user is effectively admin when they have every permission in `PermissionCatalog.All`.

### Important caveat

Not every button inside every existing module has been individually disabled or hidden yet. A large portion of service-layer protection is in place, and navigation is filtered, but fine-grained command/button visibility inside all existing views remains unfinished work.

## 9. Remaining Work

1. Create the EF Core migration for the role-to-user-permission schema change.
2. Manually review the generated migration for existing production database compatibility.
3. Add explicit data migration from `RolePermissions` to `UserPermissions` before dropping role tables.
4. Update the EF model snapshot through the migration process.
5. Run a migration against a copied existing database and verify old users keep equivalent permissions.
6. Run the application from a fresh database and verify admin/admin123 login works.
7. Run the application against an upgraded database and verify old users can log in.
8. [Completed] Audit all existing WPF views and disable/hide individual buttons based on permissions, not only navigation.
9. [Completed] Add command-level `CanExecute` checks where commands map to protected operations.
10. [Completed] Review report services because some report methods call underlying sales/purchase/treasury services that may require additional view permissions beyond report permissions (implemented read-only direct EF Core queries for reporting metrics in `AccountingReportService`).
11. [Completed] Review dashboard data loading for all restricted service calls and decide which widgets should hide versus show limited data (retained consistent layout, displaying disabled cards with "غير مصرح" placeholder value).
12. [Completed] Review all dialogs opened manually to ensure owner, style, and DI lifetime match project conventions (registered `SafeFormDialog` in the `DialogService` mapping and refactored `SafeManagementDialogViewModel` to use `IDialogService`).
13. Add integration tests for `UserManagementService`.
14. Add authorization tests for representative services, including missing-permission failures.
15. Add login tests for inactive users, bad passwords, and direct permission loading.
16. Add seed tests for admin recreation and permission upsert behavior.
17. Add UI smoke testing for the Users page/dialogs.
18. [Completed] Decide whether `WorkingDay.Reopen` should remain as an extra permission or be folded into close/system settings (retained as independent permission, fully integrated in treasury command).
19. [Completed] Review and localize new English UI strings if the project requires Arabic/localized resources.
20. Review `UserFormDialog` and `ResetPasswordDialog` DI registrations; remove or adapt if dialogs should never be container-created.
21. Run full `dotnet test` after migration exists and fixture/database setup are aligned.
22. Perform manual QA of login, nav filtering, user create/edit/reset, enable/disable, and delete business rules.

## 10. Known Issues

1. No EF Core migration has been created yet. The code builds, but database schema update work is unfinished.
2. Existing databases still using roles require a careful migration to preserve current access.
3. [Resolved] Fine-grained button visibility/disablement inside all existing views is complete.
4. Service-layer authorization coverage is broad but still needs a systematic audit against every protected feature and command.
5. Reporting permissions may currently be stricter than expected because reporting services can call underlying services that also require module view permissions.
6. [Resolved] New UI strings are localized to Arabic.
7. `UserFormDialog` and `ResetPasswordDialog` are registered in DI but currently constructed manually with view models.
8. Runtime UI testing has not been performed in this handoff step; only solution build was verified.
9. [Resolved] `dotnet test` was run and all 49 integration tests passed successfully.
10. The Users module uses regular WPF windows for dialogs, consistent with many existing dialogs, but it has not been integrated with a central dialog abstraction if one is preferred later.
11. `PermissionService.IsAdmin()` is now permission-complete semantics; any future code expecting role-name semantics should be updated.
12. Some tuple-returning services still return existing `Loc.ErrAdminRequired` text for permission failures; wording may need to be updated to be permission-specific.
13. The admin seed assigns all current permissions, including the added `WorkingDay.Reopen` permission.
14. Password policy is basic at this point. The service validates non-empty password, and the UI validates confirmation, but stronger complexity rules were not added.
15. Delete user currently relies on soft delete through existing EF/base-entity behavior; migration and runtime QA should confirm expected behavior.

## 11. Treasury Enhancements (Multiple System Safes)

Implemented three permanent system safes using a strongly typed `SafeType` enum, replacing the single built-in daily cash safe.

### Key Changes
1. **SafeType Enum**: Created `SafeType` with values `Main`, `Private`, `Daily`, and `Normal` to track safes by type.
2. **Domain Entity Update**: Added `Type` property to `Safe` entity. Excluded `IsSystem` and `IsDefaultCashSafe` from EF Core database mappings, converting them into computed read-only properties derived from `Type` to preserve backward compatibility.
3. **Dedicated System Safe Service**: Created `ISystemSafeService` and `SystemSafeService` to handle ensuring/creating the three permanent safes (`MAIN_SAFE`, `PRIVATE_SAFE`, `DAILY_CASH_SAFE`) and retrieving them by type. Refactored `DefaultCashSafeService` to delegate default cash safe retrieval to `SystemSafeService`.
4. **Self-Healing Startup & Seeding**: Updated `DefaultDataSeeder` to explicitly call `EnsureSystemSafesAsync` on startup. This guarantees that missing system safes (Main and Private) are automatically generated for existing databases (not just new databases), ensuring self-healing execution on startup.
5. **Integration Tests Update**: Refactored `UserManagementAndSecurityTests.cs` to resolve and inject `ISystemSafeService` into seeder instances, aligning test fixtures with the new constructor dependencies.
6. **Refactoring Services**: Updated `WorkingDayService` and `SafeService` to replace string-name and name-contains checks with robust type-based lookups. Renaming system safes is allowed, but deactivation, type changes, or deletions are strictly blocked.
7. **UI Updates**:
   - `SafeFormDialog` name field is now editable for system safes, and displays a readonly type textbox. The "Active" status checkbox remains disabled for system safes.
   - `SafeManagementDialog` displays the localized safe type (e.g. "ثابتة - رئيسية", "عادية") in the grid and allows clicking "Edit" on system safes (only "Deactivate" remains disabled).
8. **Data-Safe EF Core Migration**: Scaffolded and updated `AddSafeType` migration with SQL update queries to safely migrate existing safe records without data loss.
9. **Verification**: Executed a clean Release build check and successfully passed all 49 integration tests.

## 12. Treasury Resource-Level Safe Permissions

Implemented resource-level safe permissions, allowing administrators to configure granular operations (Access, View Balance, View Ledger, Cash In, Cash Out, Transfer From, and Receive Transfer) per safe, per user.

### Key Changes
1. **UserSafePermission Entity**: Added a new entity `UserSafePermission` with composite index `(UserId, SafeId)` filtered by `[IsDeleted] = 0` to persist permissions for each safe per user.
2. **Bulk Saving & Verification**: Implemented `IUserSafePermissionService` with full validation (UserId/SafeId existence, duplicate prevention) and backward-compatible default fallback (users with no permission records retain full access to all safes).
3. **SafeService Enforcement**: Refactored `SafeService` to query safe-level flags. Users without `CanAccess` cannot see safes in dropdowns or lists. Users without `CanViewBalance` see a safe balance of `0`. Users without `CanViewLedger` are blocked from viewing safe history. Users without `CanCashIn`/`CanCashOut` or transfer flags cannot execute deposit/withdraw/transfer operations (throwing `UnauthorizedAccessException`).
4. **WPF UI Matrix & DataGrid**: Added an Arabic-first DataGrid inside `UserFormDialog.xaml` enabling full checkbox assignment of all 7 safe-level permissions for each active safe.
5. **Dynamic UI Filtering**:
   - `TreasuryTransactionDialogViewModel` filters safes dynamically depending on if deposit/withdrawal option is selected and checks `CanCashInAsync`/`CanCashOutAsync`.
   - `TreasuryTransferDialogViewModel` separates dropdowns into `SourceSafes` (filtered by `CanTransferFromAsync`) and `DestinationSafes` (filtered by `CanReceiveTransferAsync`).
6. **Tests**: Added a new test suite `UserSafePermissionTests.cs` covering all permission checks, backward compatibility, and admin defaults.
7. **Verification**: Cleaned, built the entire solution in both Debug and Release configurations, and ran all tests. All **60 integration tests passed successfully**.
