# Security and Permissions

This document describes the authorization model implemented by Bakery ERP. It is intended for maintainers extending services, screens, roles, or resource scopes.

## Authentication

- The first-run workflow creates exactly one initial Super Administrator when the database has no users. Creation runs in a serializable transaction guarded by a SQL application lock, and there is no hard-coded production credential.
- New and reset passwords must contain at least 12 characters. Passwords are stored with PBKDF2-HMAC-SHA-256 using a random 16-byte salt, a 32-byte derived key, and 100,000 iterations; verification uses a fixed-time comparison.
- Five failed sign-in attempts produce a 15-minute lockout. Invalid usernames, passwords, inactive accounts, deleted accounts, and locked accounts return the same user-facing credential error.
- A successful sign-in loads only active branch assignments and calculates effective permissions from direct grants and assigned roles.

The core implementation is in [`AuthService`](../../Bakery.Infrastructure/Services/Security/AuthService.cs), [`FirstRunSetupService`](../../Bakery.Infrastructure/Services/Security/FirstRunSetupService.cs), and [`PasswordHasher`](../../Bakery.Infrastructure/Security/PasswordHasher.cs).

## Authorization model

Effective authorization is the union of direct user permissions and permissions inherited from roles. A Super Administrator bypasses permission-key and safe-level checks. The seeded roles are System Administrator, Branch Manager, Treasury Clerk, Inventory Controller, and Auditor; maintainers can also create custom roles.

The canonical keys and display metadata live in [`PermissionCatalog`](../../Bakery.Application/Security/PermissionCatalog.cs). [`PermissionPolicyCatalog`](../../Bakery.Application/Security/PermissionPolicyCatalog.cs) defines parent requirements—for example, an action permission cannot remain effective without its corresponding view permission. User and role management validate these dependencies before saving an assignment.

Changing a user’s roles, direct permissions, branch assignments, safe permissions, password, or active state rotates the user’s security stamp. The next protected service call compares the session stamp with the database, invalidates a stale session, and requires the user to sign in again.

## Enforcement boundary

Service-layer permission checks are the security boundary. WPF navigation, visibility helpers, and command availability provide the same policy at the interface for usability, but they do not replace checks in infrastructure services.

[`PermissionService`](../../Bakery.Infrastructure/Services/Security/PermissionService.cs) validates that the current session is still active and then enforces the requested key. A denied call remains denied even if audit persistence fails. New write operations must call the appropriate permission check before mutation and must not depend only on a hidden button or navigation rule.

## Branch and safe scope

Users sign in only to assigned active branches. Domain records implementing `IBranchScoped` receive an Entity Framework Core global query filter for the active branch and, for `BaseEntity` records, soft-delete state. Branch switching creates a new branch-scoped service session rather than reusing tracked data from the previous branch.

Safe access is a separate resource-level policy. For each user and safe it can grant:

- access to the safe;
- balance visibility;
- ledger visibility;
- cash-in and cash-out;
- transfer-source and transfer-destination access.

Non-Super Administrators are denied when no matching safe-permission row exists. Granting an operation without base safe access is rejected. Updating safe permissions also rotates the affected user’s security stamp. See [`UserSafePermissionService`](../../Bakery.Infrastructure/Services/Security/UserSafePermissionService.cs) and the branch filter in [`BakeryDbContext`](../../Bakery.Infrastructure/Data/BakeryDbContext.cs).

## Audit behavior

Security-sensitive operations use stable action keys from [`AuditActionKeys`](../../Bakery.Shared/Auditing/AuditActionKeys.cs). Audit records can include the branch, user, entity, old and new values, machine name, IP address when available, and UTC occurrence time.

Authorization denials are written with the `AuthorizationDenied` action through an isolated database context so a rejected or rolled-back business operation does not erase the denial record. Permission, role, branch, safe-access, authentication, backup, restore, and system-reset actions also produce dedicated audit events. Viewing and exporting the audit log require separate permissions.

## Extending security safely

When adding a protected capability:

1. add or reuse a stable permission key and catalog entry;
2. declare any required parent permission in `PermissionPolicyCatalog`;
3. decide whether built-in roles should receive the permission;
4. enforce it in every service entry point before state changes;
5. mirror the rule in navigation and command visibility;
6. add tests for allowed, denied, Super Administrator, stale-session, branch-scope, and safe-scope behavior as applicable;
7. record a stable audit action for sensitive mutations or denials.

Do not introduce a UI-only permission, a permissive fallback for missing configuration, or a cross-branch query without an explicit administrative reason and targeted tests.
