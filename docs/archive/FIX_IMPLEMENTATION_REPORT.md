# Bakery ERP — Production Fix Implementation Report

Date: 2026-07-22 (Africa/Cairo)  
Source audit: `PRE_DELIVERY_FULL_AUDIT.md`  
Implementation checkpoints: `PHASE1_VERIFICATION_REPORT.md`, `PHASE2_VERIFICATION_REPORT.md`, `PHASE2_CHECKPOINTS.md`, and `PHASE3_CHECKPOINTS.md`

## Fixed Critical Issues

### C-01 — Single-file startup crash

- **Audit ID:** C-01.
- **Root cause:** Serilog configuration assembly discovery depended on a dependency context that is unavailable in a bundled single-file application. Host construction failed before the old global exception handlers and application logger existed.
- **Files changed:** `Bakery.WPF/App.xaml.cs`, `Bakery.WPF/Logging/RedactingJsonFormatter.cs`, `Bakery.WPF/appsettings.defaults.json`, `Bakery.IntegrationTests/StartupLoggingTests.cs`, and `Bakery.IntegrationTests/LoggingSafetyTests.cs`.
- **Fix implemented:** Explicitly supplied the file-sink assembly to Serilog configuration, created structured bootstrap logging before host construction, added a synchronous redacted fallback for failures before the host exists, and retained structured rolling file logs.
- **Tests added:** Startup configuration/source contracts, valid structured JSON output, early fallback logging, redaction, and writable-path tests.
- **Runtime evidence:** The exact final win-x64 self-contained single-file executable opened the first-run/login window and later the initialized dashboard. The final published run and installed run each exited 0 with 0 new Error/Fatal events.
- **Status:** Fixed.

### C-02 — Unauthorized Working Day administrative override

- **Audit ID:** C-02.
- **Root cause:** `WorkingDay.Close` plus a client-provided Boolean could bypass close blockers; no distinct privileged policy existed at the service boundary.
- **Files changed:** `Bakery.Application/Security/PermissionCatalog.cs`, `Bakery.Application/Security/PermissionPolicyCatalog.cs`, `Bakery.Infrastructure/Seeders/DefaultDataSeeder.cs`, `Bakery.Infrastructure/Services/WorkingDayService.cs`, `Bakery.WPF/CloseDayDialog.xaml`, `Bakery.WPF/ViewModels/CloseDayDialogViewModel.cs`, `Bakery.Shared/Helpers/Loc.cs`, `Bakery.IntegrationTests/WorkingDayOverrideAuthorizationTests.cs`, and `Bakery.IntegrationTests/CloseDayDialogViewModelTests.cs`.
- **Fix implemented:** Added `WorkingDay.OverrideCloseBlockers`, enforced it inside `WorkingDayService`, retained mandatory reasons/auditing, and hid/reset the UI override for unauthorized users.
- **Tests added:** Negative service test for a close-only user, positive privileged override test, and UI authorization-state tests.
- **Runtime evidence:** Controlled integration scenarios prove close-only users are denied and privileged users can perform an audited override.
- **Status:** Fixed.

## Fixed High Issues

### H-01 — Clean database cannot provision its first administrator

- **Audit ID:** H-01.
- **Root cause:** Production startup required undocumented process environment variables and otherwise left a fresh database with zero users.
- **Files changed:** `Bakery.Application/DTOs/UserManagementDtos.cs`, `Bakery.Application/Interfaces/IFirstRunSetupService.cs`, `Bakery.Infrastructure/Services/FirstRunSetupService.cs`, `Bakery.Infrastructure/Services/DependencyInjection.cs`, `Bakery.WPF/App.xaml.cs`, `Bakery.WPF/FirstRunSetupWindow.xaml`, `Bakery.WPF/FirstRunSetupWindow.xaml.cs`, `Bakery.WPF/ViewModels/FirstRunSetupViewModel.cs`, and `Bakery.IntegrationTests/FirstRunSetupTests.cs`.
- **Fix implemented:** Added an interactive first-run administrator flow requiring a policy-compliant chosen password, a serializable transaction plus SQL application lock, one-administrator enforcement, role/branch assignment, and password-free audit details.
- **Tests added:** Fresh database, weak-password rejection, exactly-one administrator, duplicate/concurrent setup rejection, role/branch, hash, and audit assertions.
- **Runtime evidence:** The exact final fresh installation showed `إعداد مسؤول النظام`, accepted a chosen password, transitioned to login, opened the main window, and persisted exactly 1 super-administrator and 1 `FirstRunAdministratorCreated` audit across restart/reinstall.
- **Status:** Fixed.

### H-02 — Installer does not validate the database prerequisite

- **Audit ID:** H-02.
- **Root cause:** Setup copied the application without detecting the LocalDB engine used by the shipped defaults.
- **Files changed:** `BakeryERP.iss`, `Bakery.IntegrationTests/InstallerPrerequisiteContractTests.cs`, and deployment documentation.
- **Fix implemented:** Setup detects supported x64 LocalDB registry/file installations and blocks installation with actionable Arabic instructions and an official Microsoft download link when absent.
- **Tests added:** Deterministic installer source-contract tests for detection and the blocking path.
- **Runtime evidence:** The final installer passed prerequisite detection on Microsoft SQL Server LocalDB 15.0.4382.1. A true no-LocalDB machine was unavailable.
- **Status:** Fixed on the available host; clean no-LocalDB VM execution remains an external acceptance item.

### H-03 — Mutable runtime paths under Program Files

- **Audit ID:** H-03.
- **Root cause:** Logs, attachments, previews, documents, templates, logos, grid settings, and restore staging were rooted beside the executable.
- **Files changed:** `Bakery.Application/Interfaces/IApplicationPathService.cs`, `Bakery.Infrastructure/Services/ApplicationPathService.cs`, `AttachmentStorageService.cs`, backup/restore services, `Bakery.WPF/Helpers/DataGridPersistence.cs`, `RecoveryViewModel.cs`, `ReportDetailsViewModel.cs`, `App.xaml.cs`, and `Bakery.IntegrationTests/ApplicationPathServiceTests.cs`.
- **Fix implemented:** Centralized all mutable paths under `%LOCALAPPDATA%\BakeryERP`, while preserving configured external backup destinations.
- **Tests added:** Path/source contracts plus real write tests for logs, attachments, restore work, and application data directories.
- **Runtime evidence:** Installed runs wrote to `%LOCALAPPDATA%\BakeryERP`; no new mutable content was written beside the executable.
- **Status:** Fixed.

### H-04 — Unsafe cross-resource restore

- **Audit ID:** H-04.
- **Root cause:** Database and external directories could be replaced in partially committed order, with protected-directory writes and incomplete rollback semantics.
- **Files changed:** `Bakery.Infrastructure/Services/Backup/BackupRestoreService.cs`, `BackupValidationService.cs`, `BackupInfrastructure.cs`, `BackupService.cs`, application path interfaces/services, and `Bakery.IntegrationTests/BackupSystemTests.cs`.
- **Fix implemented:** Fully validates/extracts selected and safety archives before replacement, stages external content, uses exact replacement semantics, rolls database/files back from the validated safety archive, and retains `recovery-required.json` plus staging when rollback itself fails.
- **Tests added:** Database failure, partial external replacement failure, rollback failure, manifest retention, and manual recovery tests.
- **Runtime evidence:** Isolated LocalDB restore/failure-injection scenarios restored the original database, attachment, and grid state; retained recovery state was usable after a forced rollback failure.
- **Status:** Fixed.

### H-05 — Inventory unit corruption

- **Audit ID:** H-05.
- **Root cause:** Operational movements stored raw selected-unit quantities, did not verify item/unit membership, and later summed them as base units.
- **Files changed:** `Bakery.Application/Interfaces/IItemUnitConversionService.cs`, `Bakery.Infrastructure/Services/ItemUnitConversionService.cs`, inventory, stock, sale, purchase, waste, recipe, production and working-day services, `ProductionPostingEngine.cs`, WPF inventory view models, and `Bakery.IntegrationTests/InventoryUnitConversionTests.cs`.
- **Fix implemented:** Enforced item/unit relationships, converted new movement quantity/cost to base units, normalized legacy rows for balances/history/valuation, and prevented unsafe base-unit/factor edits after ledger history exists.
- **Tests added:** Base/non-base conversion, unrelated-unit rejection, purchase, sale, return/cancellation, adjustment, count, waste, production input/output, history, low stock, and valuation tests.
- **Runtime evidence:** The end-to-end LocalDB workflow reconciled signed base-unit movement totals directly to reported stock.
- **Status:** Fixed.

### P-01 — Stock check/write race

- **Audit ID:** P-01 (confirmed during Phase 2 reproduction).
- **Root cause:** Availability was checked before writes without a shared database-level serialization invariant.
- **Files changed:** `Bakery.Application/Interfaces/IStockMutationLock.cs`, `Bakery.Infrastructure/Services/StockMutationLock.cs`, sale, purchase, inventory, waste and production posting services, dependency injection, and `Bakery.IntegrationTests/SafeContextAndWorkspaceTests.cs`.
- **Fix implemented:** Added transaction-owned SQL application locks per branch/item, acquired in stable item order, with availability checks and movement writes inside the same transaction/lock.
- **Tests added:** Controlled two-DbContext concurrent oversell regression.
- **Runtime evidence:** Two simultaneous attempts to consume 10 available units produced exactly one post, one rejected draft, and a database balance of 0 rather than -10.
- **Status:** Fixed.

### H-06 — Reports use calendar timestamps instead of business date

- **Audit ID:** H-06.
- **Root cause:** Daily queries filtered UTC timestamps through naive date boundaries instead of the assigned `WorkingDay.BusinessDate`.
- **Files changed:** `Bakery.Application/Interfaces/IBusinessDateService.cs`, `Bakery.Infrastructure/Services/BusinessDateService.cs`, `Bakery.Reporting/Services/AccountingReportService.cs`, production/waste services, and `Bakery.IntegrationTests/BusinessDateReportingTests.cs`.
- **Fix implemented:** Centralized branch-scoped business-date resolution and filtered sales, purchases, cash, dashboard/trend, production, and waste through working-day identity.
- **Tests added:** Adjacent business days and Egypt local-midnight/UTC-boundary regressions.
- **Runtime evidence:** Transactions timestamped on a neighboring UTC calendar date stayed in their assigned Egypt business day and did not leak into the adjacent day.
- **Status:** Fixed.

### H-09 — Production summary omits unloaded children

- **Audit ID:** H-09.
- **Root cause:** The service summed unloaded navigation collections with lazy loading disabled.
- **Files changed:** `Bakery.Infrastructure/Services/ProductionService.cs` and production/end-to-end integration tests.
- **Fix implemented:** Replaced tracked-header navigation access with database-side aggregates over completed consumed/produced rows.
- **Tests added:** Fresh-DbContext test with multiple completed production orders.
- **Runtime evidence:** Two orders returned the expected order count, consumed cost, and produced value; end-to-end values matched direct child-row aggregates.
- **Status:** Fixed.

### H-10 / M-01 — Sales By Item report is empty

- **Audit ID:** M-01 in the audit; tracked as H-10 in the Phase 2 plan.
- **Root cause:** `GetSalesByItemAsync` returned an unconditional empty list.
- **Files changed:** `Bakery.Application/DTOs/Accounting/AccountingDtos.cs`, `Bakery.Reporting/Interfaces/IAccountingReportService.cs`, `Bakery.Reporting/Services/AccountingReportService.cs`, `Bakery.IntegrationTests/SalesByItemReportTests.cs`, and `EndToEndSystemTests.cs`.
- **Fix implemented:** Added quantity, gross, discount, return, net quantity, and net sales aggregation with branch/business-date scope and base-unit conversion.
- **Tests added:** Posted sale, returned/cancelled sale, excluded draft, non-base unit, missing date, and adjacent-day cases.
- **Runtime evidence:** End-to-end database reconciliation returned quantity 20 and gross/net 300 for the posted product.
- **Status:** Fixed.

### H-08 — Thermal receipt prints `DTO.ToString()`

- **Audit ID:** H-08.
- **Root cause:** Printer dispatch serialized the DTO record rather than rendering invoice lines.
- **Files changed:** invoice print DTO/query paths, `Bakery.WPF/Services/Print/ThermalReceiptRenderer.cs`, print dispatch, DI in `App.xaml.cs`, and `Bakery.IntegrationTests/ThermalReceiptRendererTests.cs`.
- **Fix implemented:** Separated deterministic receipt rendering from printer dispatch and included business header, invoice/date/cashier/customer, lines, quantities, units, prices, subtotal, discount, tax, paid, remaining, audit date, and footer.
- **Tests added:** Full receipt content and no-record/collection-serialization assertions.
- **Runtime evidence:** Renderer/dispatch tests and sale/purchase print-data workflows pass. Physical printer output is still a hardware acceptance item.
- **Status:** Fixed in software.

### H-07 — Backup confidentiality

- **Audit ID:** H-07.
- **Root cause:** Archives were readable ZIP files and the password argument was ignored.
- **Files changed:** `Bakery.Infrastructure/Services/Backup/BackupEncryptionService.cs`, `BackupService.cs`, `BackupValidationService.cs`, `BackupRestoreService.cs`, backup infrastructure/path components, `Bakery.IntegrationTests/BackupSystemTests.cs`, and `BACKUP_ENCRYPTION_FORMAT.md`.
- **Fix implemented:** Added a versioned authenticated `.berpbackup` envelope using AES-256-CBC plus HMAC-SHA-256; portable password mode uses PBKDF2-HMAC-SHA-256 (210,000 iterations), unattended mode uses a DPAPI-protected per-user master key, and legacy ZIP restore remains supported.
- **Tests added:** Password/device-key encryption, wrong/missing password, tamper rejection, no password bytes in artifact/log/audit, legacy ZIP, retention, automatic backup, restore, and rollback tests.
- **Runtime evidence:** 17 backup-focused tests and isolated restore scenarios pass; authenticated tampering fails before ZIP/SQL processing.
- **Status:** Fixed.

### M-03 / Phase 3 item 14 — Installer lifecycle safeguards

- **Audit ID:** M-03.
- **Root cause:** No mutex/close policy existed, configuration was treated as ordinary payload, and no lifecycle run had been performed.
- **Files changed:** `BakeryERP.iss`, `Bakery.WPF/Services/SingleInstanceGuard.cs`, `Bakery.WPF/App.xaml.cs`, and `Bakery.IntegrationTests/InstallerLifecycleTests.cs`.
- **Fix implemented:** Added application/setup mutexes, single-instance enforcement, close/locked-file behavior, previous-directory retention, explicit Arabic uninstall data warning, and legacy configuration preservation to `%LOCALAPPDATA%` with uninstall abort on preservation failure.
- **Tests added:** Source contracts for mutex, close behavior, no data-root deletion, and legacy configuration copy.
- **Runtime evidence:** Fresh install, upgrade from the Phase 1 installation, same-version reinstall, uninstall, and post-uninstall reinstall all completed. Published/installed hashes matched. User data and both isolated databases survived. A benign legacy configuration was preserved byte-for-byte in place and at `appsettings.legacy-uninstall.json`.
- **Status:** Fixed on the per-user test path.

### M-15 / Phase 3 item 15 — Defaults mixed with writable customer configuration

- **Audit ID:** M-15.
- **Root cause:** A source-controlled `appsettings.json` contained customer/credential-shaped values and was overwritten by setup.
- **Files changed:** `Bakery.WPF/appsettings.defaults.json`, `Bakery.WPF/Services/ApplicationConfiguration.cs`, `Bakery.WPF/App.xaml.cs`, `BakeryERP.iss`, `Bakery.IntegrationTests/ApplicationConfigurationTests.cs`, and deployment documentation.
- **Fix implemented:** Shipped immutable non-secret defaults, added atomic `%LOCALAPPDATA%\BakeryERP\appsettings.user.json`, deterministic precedence, legacy migration/preservation, JSON/connection/OAuth-pair validation, and installer exclusions for writable settings.
- **Tests added:** First creation, non-overwrite, precedence, legacy migration, invalid JSON/connection/OAuth pairing, payload exclusion, and secret scan.
- **Runtime evidence:** Final publish contains only `appsettings.defaults.json`; fresh install contains no user/legacy config; upgrade/reinstall preserved the customer file hash.
- **Status:** Fixed.

### M-02 / Phase 3 item 16 — Invoice number generation

- **Audit ID:** M-02.
- **Root cause:** Sale/purchase drafts used device date plus `CountAsync() + 1`, allowing races and deleted-number reuse.
- **Files changed:** `Bakery.Application/Interfaces/IInvoiceService.cs`, `Bakery.Infrastructure/Services/InvoiceNumberAllocator.cs`, sale/purchase invoice services, dependency injection, and `Bakery.IntegrationTests/InvoiceNumberAllocationTests.cs`.
- **Fix implemented:** Allocates by active business date/branch/document type using the existing counter table, a transaction-owned SQL application lock, and atomic update/insert; legacy and soft-deleted numbers seed the next value.
- **Tests added:** Business-date, sale/purchase sequence, deleted/legacy number, and controlled concurrent allocation tests.
- **Runtime evidence:** Concurrent creation produces unique numbers and no reuse after deletion.
- **Status:** Fixed.

### M-16 / Phase 3 item 17 — Full backup before every sale

- **Audit ID:** M-16.
- **Root cause:** Every routine sale synchronously created a full database safety snapshot before beginning the posting transaction.
- **Files changed:** `Bakery.Infrastructure/Services/SaleInvoiceService.cs` and `Bakery.IntegrationTests/SalePostingReliabilityTests.cs`.
- **Fix implemented:** Removed routine sale snapshots while retaining atomic transaction, stock lock, working-day guard, audit writes, and restore/migration safety backups. Added repeat/concurrent idempotency.
- **Tests added:** Sequential retry and controlled two-terminal retry with stock, safe, party ledger, audit, and backup-count reconciliation.
- **Runtime evidence:** Same LocalDB scenario improved from 1,348 ms (1,040 ms snapshot) to 390 ms with no routine snapshot, a measured 71% reduction.
- **Status:** Fixed.

### M-08 / M-14 / Phase 3 item 18 — Logging and operator-safe errors

- **Audit ID:** M-08 and M-14.
- **Root cause:** Sensitive structured values/exceptions were not centrally redacted, some provider messages crossed into UI, and startup fallback paths were inconsistent.
- **Files changed:** `Bakery.Shared/Security/SensitiveDataRedactor.cs`, `Bakery.WPF/Logging/RedactingJsonFormatter.cs`, `OperatorErrorHandler.cs`, `Bakery.Application/UserErrorMessages.cs`, `App.xaml.cs`, recovery logging, affected WPF view models, and `Bakery.IntegrationTests/LoggingSafetyTests.cs`.
- **Fix implemented:** Added structured recursive redaction, 10 MiB size rolling, retained daily files, writable log paths, early-startup logging, one operator error boundary, and diagnostic logging for formerly silent paths.
- **Tests added:** Valid JSON, nested property/message/exception redaction, startup fallback, path, and affected workflow tests.
- **Runtime evidence:** Injected passwords/tokens/connection strings do not appear in parsed logs. The final installed run produced no Error/Fatal event and exposed no Phase 4 database name or test credential.
- **Status:** Fixed.

### Phase 3 item 19 — Stable audit action keys

- **Audit ID:** Requested operational reliability item; related to audit consistency/maintainability findings.
- **Root cause:** Infrastructure writes and queries used scattered free-form action strings.
- **Files changed:** `Bakery.Shared/Auditing/AuditActionKeys.cs`, `AuditActionArabicLocalizer.cs`, infrastructure audit call sites, `AuditService.cs`, `Loc.cs`, and `Bakery.IntegrationTests/AuditActionCatalogTests.cs`.
- **Fix implemented:** Added one stable identifier catalog, rejected new unknown identifiers at the service boundary, preserved historical stored values, and separated Arabic display localization.
- **Tests added:** Catalog, JSON persistence, localization, source-contract, and unknown-free-form rejection tests.
- **Runtime evidence:** Existing workflows persist catalog values and structured numeric/operation details without a data migration.
- **Status:** Fixed.

### M-05 / M-13 / Phase 3 item 20 — Dependency/runtime policy

- **Audit ID:** M-05 and M-13; M-06 partially addressed.
- **Root cause:** LiveCharts' WPF path required legacy OpenTK assets, all restores emitted NU1701, the SDK was unpinned, and several existing package patch lines were behind.
- **Files changed:** `Bakery.WPF/Bakery.WPF.csproj`, `Bakery.Infrastructure/Bakery.Infrastructure.csproj`, `Bakery.IntegrationTests/Bakery.IntegrationTests.csproj`, `global.json`, `Bakery.IntegrationTests/PresentationDependencyRuntimeTests.cs`, and `DEPENDENCY_DECISION.md`.
- **Fix implemented:** Upgraded LiveCharts to 2.0.5, SkiaSharp to 3.119.4, bounded current-line patches including EF Core 8.0.29, pinned required OpenTK runtime assemblies with package-local NU1701 handling only, pinned SDK policy, and pinned self-contained releases to .NET 8.0.29.
- **Tests added:** Real STA WPF chart measure/layout/render, Skia native draw/PNG, QuestPDF generation, plus existing thermal/report/treasury/login tests.
- **Runtime evidence:** 23 focused presentation/printing/login tests passed after package changes; forced restore and both builds emit 0 warnings. Final scan: 0 known vulnerable entries and 0 outstanding top-level patch updates.
- **Status:** Fixed for the audited compatibility warning and supported SDK/runtime policy. Deprecated transitive debt remains below.

### NEW-01 — Dashboard startup shared-DbContext concurrency

- **Audit ID:** Phase 1 residual runtime observation, confirmed again during final installed-artifact testing.
- **Root cause:** `MainViewModel` fired branch count, safe count, and dashboard refresh concurrently in one session scope sharing one `BakeryDbContext`.
- **Files changed:** `Bakery.WPF/ViewModels/MainViewModel.cs`, `DashboardViewModel.cs`, `Bakery.WPF/App.xaml.cs`, and `Bakery.IntegrationTests/StartupConcurrencyContractTests.cs`.
- **Fix implemented:** Added one sequential `InitializationTask`, awaited branch and safe queries, awaited dashboard refresh, and kept the login window visible until initialization completes.
- **Tests added:** Source contract preventing the three fire-and-forget calls and requiring the awaited login transition.
- **Runtime evidence:** Rebuilt exact publish and final installed upgrade both completed login/dashboard/exit with 0 Error/Fatal and 0 EF “second operation” events.
- **Status:** Fixed.

## Remaining Medium/Low Issues

| Audit ID | Current state | Risk / next action |
|---|---|---|
| M-04 | Final EXE and installer remain `NotSigned`. | SmartScreen/reputation and publisher/tamper identity remain unresolved. Obtain a trusted code-signing certificate and sign both artifacts in CI. |
| M-06 | No known vulnerability and no top-level patch update remains, but 17 unique deprecated entries remain: xUnit v2 plus the EF/SQL-client transitive Azure.Identity/IdentityModel/System.Text.Json chain. | Perform dedicated xUnit v3 and supported SQL-client dependency migrations; do not force unrelated major versions into this release. |
| M-07 | Commercial eligibility for Fluent Assertions, Inno Setup, and QuestPDF Community is not established by repository evidence. | Obtain owner/legal confirmation or paid licenses before commercial distribution. |
| M-09 | `MustChangePassword` remains ineffective for administrator resets/legacy bootstrap paths. The new first-run owner chooses their own password, so H-01 is not dependent on it. | Implement an enforced post-login password-change workflow and reset semantics. |
| M-10 | The broad `DatabaseFixture` still substitutes null validators, although fresh setup and several WPF tests use production wiring. | Add a full production-DI validation suite and remove validator bypasses incrementally. |
| M-11 | Startup integrity checking is still shallow and retains query-efficiency debt; the new end-to-end reconciliation is test-only. | Expand production reconciliation for stock, safes, parties, invoices, production, payroll, and backups; eliminate N+1 queries. |
| M-12 | Only one first-class local backup destination plus optional Google Drive is supported. | Add multiple destination/retention/quorum policy after a business continuity decision. |
| L-01 | Cross-platform/foreign-architecture native files no longer appear in the final win-x64 payload, but all 18 Lato fonts remain. | Optional publish-size/font-subsetting optimization. |
| L-02 | Repository-wide formatting verification debt remains. | Schedule a formatting-only change to avoid mixing mechanical churn with production fixes. |
| L-03 | “Excel” export remains CSV rather than `.xlsx`. | Rename the feature accurately or implement a typed workbook export. |
| L-04 | Installer is English while the application is Arabic; documentation still needs a final prerequisite/runtime wording pass. | Add Arabic Inno messages and align the delivery guide with self-contained .NET 8.0.29. |
| L-05 | Oversized services, repeated queries, synchronous file UI operations, and cancellation inconsistencies remain. | Refactor in measured, separately tested slices. |

## Items Not Fixed

### Code signing

- **Reason:** No signing certificate, protected signing key, timestamping account, or publisher decision was available.
- **Risk:** Windows cannot verify publisher identity or artifact integrity; SmartScreen friction is likely.
- **Recommended next action:** Acquire a trusted certificate, sign `publish/Bakery.WPF.exe` and `BakeryERP_Setup_v1.0.exe`, timestamp them, and verify signatures on a clean machine.

### Commercial license eligibility

- **Reason:** Organization/revenue/use eligibility is a legal/business fact not present in source.
- **Risk:** Commercial distribution could violate Fluent Assertions, Inno Setup, or QuestPDF terms.
- **Recommended next action:** Obtain written owner/legal approval or the required paid licenses before release.

### Full clean-machine and hardware/cloud acceptance matrix

- **Reason:** This host is non-administrative, already has LocalDB 15, has no physical thermal printer, and no live Google Drive test account or clean Windows 10/11 VM was supplied.
- **Risk:** Default elevated Program Files behavior, real no-LocalDB blocking UI, printer margins/cutting, SQL Express service-account backup access, OAuth upload/download, DPI, antivirus, and OS-specific behavior are not proven.
- **Recommended next action:** Run the final signed installer on clean Windows 10 and 11 VMs and the target printer/cloud environments.

### Exact-installed UI business workflow UAT

- **Reason:** The exact installer was driven through fresh setup, login, dashboard, restart, upgrade, reinstall, uninstall, data preservation, and post-uninstall login. Purchase, production, sale, treasury, restricted permissions, close-day, automatic backup, and restore were exercised against real LocalDB through integration/service/database workflows rather than manually through every installed UI dialog.
- **Risk:** A UI binding/navigation defect could remain despite the passing domain/service/database paths.
- **Recommended next action:** Execute a scripted operator UAT on the clean release VM using a representative catalog and reconcile its database afterward.

### Deprecated dependency migrations and forced password-change workflow

- **Reason:** Both require bounded migrations beyond safe patch updates; forcing them into the production-fix pass would be a blind major change.
- **Risk:** Future maintenance/support debt and weaker reset-password hygiene.
- **Recommended next action:** Plan separate xUnit v3/SQL-client and forced-password-change work items with migration-specific regressions.

## Build and Test Results

| Gate | Final result |
|---|---|
| `dotnet restore BakeryERP.sln --force --no-cache --verbosity minimal` | PASS; 0 restore warnings |
| Debug build | PASS; 0 warnings, 0 errors |
| Release build | PASS; 0 warnings, 0 errors |
| Full Release suite | PASS; 229 passed, 0 failed, 0 skipped |
| Presentation/chart/native/PDF/thermal/report/login gate | PASS; 23 selected tests |
| Vulnerable package scan | PASS; 0 entries across all 7 projects |
| Deprecated package scan | ATTENTION; 17 unique direct/transitive entries |
| Highest-patch outdated scan | PASS for top-level packages; 0 entries |
| Exact self-contained publish | PASS; clean 39-file output; only `appsettings.defaults.json` |
| Inno Setup 6.7.3 build | PASS; final compile successful with no compiler warning |

Final artifact identity:

- `publish/Bakery.WPF.exe`: 189,011,607 bytes; SHA-256 `51F1ABBA7CA0B84F594BB1105153DBA48521E7419A73B92AAEFEFDB01AE6B4F8`; product version 1.0.0; `NotSigned`.
- Bundled WPF servicing runtime: .NET 8.0.29.
- `BakeryERP_Setup_v1.0.exe`: 67,140,160 bytes; SHA-256 `AA8090BB9E8B32BAED31342FC368ADFD4E2F7012CAD539EED98302B03DEC8DCD`; `NotSigned`.
- Final installed executable hash equals the published executable hash.

## Installer Verification

| Scenario | Evidence | Result |
|---|---|---|
| Prerequisite detection | Host LocalDB 15 detected; missing-engine branch covered by installer contract | PASS on host / clean missing-engine VM pending |
| Fresh install | Per-user clean path; installer exit 0; exact hash; defaults only | PASS |
| First-run admin setup | Real installed UI; chosen policy password; 1 admin and 1 setup audit | PASS |
| Login/dashboard | Real installed UI; initialized main window; clean exit | PASS |
| Restart persistence | Second launch went directly to login; same admin/database | PASS |
| Upgrade | Upgraded older Phase 1 installation; exact EXE hash; config/user-data hashes preserved | PASS |
| Same-version reinstall | Exit 0; exact EXE; customer/user data unchanged | PASS |
| Uninstall | Explicit Arabic and standard confirmation; program/registration removed; database and user-data manifest preserved | PASS |
| Legacy config preservation | Benign sentinel copied byte-for-byte outside install tree; uninstall aborts if copying fails | PASS |
| Reinstall after uninstall | Exit 0; persistent database login opened the dashboard | PASS |
| Purchase/production/sale/treasury/open-close day | Real LocalDB end-to-end service/database reconciliation | PASS below installed-UI layer |
| Restricted permissions | Service and WPF view-model authorization regressions | PASS below full installed-UI UAT |
| Automatic encrypted backup | Backup/working-day integration coverage | PASS below external destination UAT |
| Restore in isolation | Real LocalDB, encrypted archive, staged rollback/failure injection | PASS below installed-UI UAT |

## Final Recommendation

**READY AFTER REMAINING REQUIRED FIXES**

The confirmed Critical and High source/runtime defects are fixed, the exact final artifact starts and survives its installer lifecycle, and the final build/test/security scan gates pass. Commercial delivery should wait for code signing, license eligibility confirmation, and the clean-machine installed-UI/hardware/cloud acceptance matrix described above.
