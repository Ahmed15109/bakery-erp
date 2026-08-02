# Security Audit & Authorization Refactoring Proposal

This proposal outlines the strategy for resolving the safe authorization bugs, establishing a secure **Default Deny** model, identifying the source of truth for unrestricted administrative access, and centralizing permission checks at the service layer.

---

## Part 1: Source of Truth for Unrestricted Access (Super Admin)

We recommend implementing an explicit **`IsSuperAdmin`** flag directly on the `User` entity rather than relying on seeded permission rows or hardcoded usernames.

### Comparison of Options

| Option | Implementation | Pros | Cons | Recommendation |
| :--- | :--- | :--- | :--- | :--- |
| **A. `IsSuperAdmin` Flag on `User` Entity** | Add a `bool IsSuperAdmin` column to the `Users` table (non-nullable, default `false`). | • 100% independent of permission seed data.<br>• Secure schema-level flag.<br>• Easily transferable to DTOs and session context. | • Requires a simple EF Core database migration. | **Recommended** |
| **B. Built-in System Username Check** | Hardcode `Username == "admin"` in security checks. | • No database schema modifications needed. | • Very inflexible.<br>• Breaks if the default admin is renamed or if multiple admins are needed. | Not recommended |
| **C. Dedicated Role or Special Claim** | Introduce a `UserRole` role relation or a `Claims` table. | • Standard in large-scale apps. | • Over-engineered for the current single-user-permission design. | Not recommended |

### Proposed IsSuperAdmin Architecture
1. **Database Schema**: Add `IsSuperAdmin` (boolean) to the `User` domain model.
2. **Session / DTO Integration**: Add `IsSuperAdmin` to `AuthenticatedUserDto` and `IUserSessionService`.
3. **Bypass Checks**: Update `IPermissionService` and `IUserSafePermissionService` to check the `IsSuperAdmin` flag first:
   ```csharp
   if (_userSessionService.IsSuperAdmin) return true;
   ```
4. **Resiliency**: If a database restore or manual script deletes all user-permission mapping tables, the user with `IsSuperAdmin = true` remains a Super Admin and can log in to rebuild the permission catalog.

---

## Part 2: Complete Audit of Safe-Related Entry Points

Below is the complete list of safe-related database access points in the services and how they will be secured under the new model:

### 1. `SafeService.cs`
* **`ListSafesAsync`**:
  * *Current*: Queries `_db.Safes.Where(x => x.IsActive)`. Filters them in memory using `CanAccessSafeAsync`.
  * *Refactor*: Change fallback in `CanAccessSafeAsync` to **Default Deny** (returns `false` if no DB rows exist for that user/safe).
* **`GetBalanceAsync`**:
  * *Current*: Throws `UnauthorizedAccessException` if the user lacks `CanAccessSafeAsync`. Returns `0` if they lack `CanViewBalanceAsync`.
  * *Refactor*: Retain throwing on access violation, but ensure the UI only calls this for pre-filtered accessible safes.
* **`GetLedgerAsync`**:
  * *Current*: Lists movements. If `safeId` is null, it loops over all active safes, checks `CanViewLedgerAsync` for each, and restricts the query to allowed IDs.
  * *Refactor*: Keep this logic, but ensure the fallback in `CanViewLedgerAsync` is Default Deny.
* **`DepositAsync` / `WithdrawAsync`**:
  * *Current*: Checks `CanAccessSafeAsync` and `CanCashInAsync` / `CanCashOutAsync`.
  * *Refactor*: Unchanged; will naturally enforce Default Deny.
* **`TransferAsync`**:
  * *Current*: Checks `CanTransferFromAsync` for source, and `CanReceiveTransferAsync` for destination.
  * *Refactor*: Ensure any unconfigured user/safe pairing defaults to Deny.

### 2. `AccountingReportService.cs`
* **`GetCashMovementSummaryAsync`**:
  * *Current*: Fetches active safes, checks `CanAccessSafeAsync` and `CanViewBalanceAsync` in a loop, and filters movements.
  * *Refactor*: Fully secure, but will gain Default Deny protection once the permission service is updated.

### 3. `PartyPaymentService.cs`
* **`ProcessPaymentAsync`**:
  * *Current*: Checks `CanAccessSafeAsync`, then checks `CanCashInAsync` (receipt) or `CanCashOutAsync` (payment).
  * *Refactor*: Fully secure.

### 4. `PurchaseInvoiceService.cs` and `SaleInvoiceService.cs`
* **`PostAsync` / `PostInvoiceAsync`**:
  * *Current*: If payment is cash, they load the default safe ID and check `CanAccessSafeAsync` + `CanCashOutAsync` (purchase) or `CanCashInAsync` (sales).
  * *Refactor*: Centralized service checks are already present. Under Default Deny, this will prevent unauthorized users from posting cash invoices.

### 5. `SettlementService.cs`
* **`RecordSettlementAsync` / `AddTransactionAsync`**:
  * *Current*: If a `safeId` is passed for a wage/advance payment, it calls `_safeService.WithdrawAsync`.
  * *Refactor*: `WithdrawAsync` internally checks `CanAccessSafeAsync` and `CanCashOutAsync`, so this is fully secured by propagation.

---

## Part 3: centralizing UI Logic and Preventing Crashes

Instead of using `try/catch` blocks in WPF ViewModels, the ViewModels will dynamically align with only accessible safes:

### 1. `InvoiceDialogViewModel` (Sales/Purchases)
Instead of querying the default daily safe's balance directly and throwing a crash, the view model will:
1. Fetch all accessible safes: `var safes = await _safeService.ListSafesAsync();`
2. Find the default daily safe in that list.
3. If it is present, show its balance.
4. If it is not present, set `CurrentSafeBalance = 0` (meaning the user cannot cash out/in to the default safe and must choose credit payment type).

### 2. `SettlementViewModel` (Employee Wages)
Instead of defaulting to the system daily cash safe and crash-loading its balance:
1. Load accessible safes via `ListSafesAsync()`.
2. Check if the default safe exists in the retrieved accessible safes list.
3. If yes, pre-select it. If no, pre-select the first available safe (or `null`).
4. This ensures `SelectedSafe` is always a safe they have access to, and `UpdateSafeBalanceAsync` will never fail.

### 3. `PartyPaymentDialogViewModel` (Party Payments)
* Same as `SettlementViewModel`. Pre-select the default safe only if present in the pre-filtered `Safes` collection.

---

## Part 4: Proposed Schema Changes (IsSuperAdmin Migration)

```csharp
// Bakery.Domain/Entities/SecurityEntities.cs
public sealed class User : BaseEntity
{
    public string Username { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    public bool IsSuperAdmin { get; set; } = false; // New column
    // ...
}
```

```csharp
// Bakery.Application/DTOs/AuthResult.cs
public record AuthenticatedUserDto(
    int UserId, 
    string UserName, 
    string FullName, 
    string[] Permissions,
    bool IsSuperAdmin); // New field
```
