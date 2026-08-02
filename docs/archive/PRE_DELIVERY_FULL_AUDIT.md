# Bakery ERP — Complete End-to-End Production-Readiness Audit

**Audit date:** 2026-07-21–22 (Africa/Cairo)  
**Audit type:** Read-only first pass; no product source fixes made  
**Repository:** `C:\Users\Ahmed\OneDrive\Desktop\bakery`  
**Target:** Bakery ERP `1.0.0`, Windows x64, .NET 8 WPF, SQL Server LocalDB/Express, Inno Setup  
**Auditor:** Codex  
**Final recommendation:** **BLOCK DELIVERY**

> This report is based on commands executed during this audit, isolated runtime tests, direct database queries, and current-source inspection. Historical audit reports and old test results in the repository were not used as evidence for the conclusions below.

---

## A. Executive summary

The current Bakery ERP release must not be delivered. The documented, self-contained single-file release artifact compiles successfully but terminates before showing any window. Windows Event Log confirms an unhandled `InvalidOperationException` from Serilog single-file configuration at `Bakery.WPF/App.xaml.cs:116`; the process exits with `-532462766` (`0xE0434352`). The newly compiled installer packages that same non-starting executable.

Even when a diagnostic multi-file publish is used to get past that crash, a clean database cannot complete first-run initialization unless two process environment variables containing bootstrap administrator credentials are already set. The installer neither asks for nor sets those values and does not install SQL Server LocalDB/Express. Therefore, setup alone cannot produce a usable clean installation.

There is also a critical authorization defect in working-day close: anyone with the ordinary `WorkingDay.Close` permission can select the UI's “administrative override” checkbox and bypass every end-of-day blocker. No super-administrator or separate override permission is required. This can bypass unfinished operational documents, stock-count, negative-safe, and financial-integrity blockers.

The source audit additionally found high-severity inventory-unit, reporting, backup-confidentiality, printing, deployment-path, restore-safety, and production-summary defects. These are not cosmetic polish issues; several can produce incorrect stock or financial output.

### Severity count

| Classification | Count | Meaning in this report |
|---|---:|---|
| Critical | 2 | Confirmed release blocker or critical authorization/integrity defect |
| High | 9 | Confirmed by execution or deterministic current-source path |
| Medium | 16 | Material reliability, security, deployment, quality, or maintainability defect |
| Low | 5 | Polish, packaging efficiency, consistency, or hygiene defect |
| Potential risk | 8 | Code-supported risk not reproduced end-to-end in this environment |

### What passed

- Forced, no-cache restore completed.
- Debug and Release solution builds completed with zero errors.
- The full integration suite passed: **189 passed, 0 failed, 0 skipped**.
- A second coverage run also passed 189/189.
- No known vulnerable NuGet packages were reported by `dotnet list package --vulnerable --include-transitive`.
- The exact documented `win-x64`, self-contained, single-file publish completed after RID assets were restored.
- Inno Setup 6.7.3 compiled `BakeryERP.iss` successfully.
- A diagnostic multi-file publish applied all **36** migrations to an isolated LocalDB database.
- The isolated database had 44 tables, 102 foreign keys, 312 non-heap indexes, and passed `DBCC CHECKDB` with no reported errors.
- With audit-only bootstrap environment variables, the diagnostic multi-file build reached an Arabic, RTL login window at the host's actual 125% display scaling.
- Current backup integration tests exercised backup validation, restore, retention, automatic queue behavior, and related paths successfully.

### Why those passes do not clear the release

Build success does not mean the packaged executable starts. The test fixture supplies bootstrap environment variables and replaces multiple production FluentValidation validators with null validators. Measured branch coverage is only **34.66%**; WPF line coverage is **17.95%** and Reporting line coverage is **0%**. The tests therefore do not exercise the actual documented single-file startup, clean installer provisioning, most UI behavior, real printing, or live Google Drive authorization/upload.

---

## B. Confirmed critical release blockers

### C-01 — The prescribed and installed single-file executable cannot start

**Severity:** Critical  
**Status:** Reproduced twice; confirmed by process exit and Windows Event Log

The deployment guide prescribes:

```powershell
dotnet publish Bakery.WPF/Bakery.WPF.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true
```

That publish completed. Its main executable is:

- Path: `publish\Bakery.WPF.exe`
- Size: 188,942,721 bytes
- SHA-256: `FE3A598E9A5378B48684EEBA7BA066CC45EB50873B1F1A352B27C9CAE07B2494`
- Version: `1.0.0.0` / product version `1.0.0`
- Signature: `NotSigned`

An identical-hash copy was launched from an isolated directory with a distinct audit database name and without bootstrap variables. It exited with code `-532462766`. The `.NET Runtime` event (Application log, event ID 1026) states:

```text
System.InvalidOperationException: No Serilog:Using configuration section is defined
and no Serilog assemblies were found. This is most likely because the application
is published as single-file.
```

The stack identifies:

- `Bakery.WPF.App.CreateHostBuilder` → `Bakery.WPF/App.xaml.cs:116`
- `Bakery.WPF.App.OnStartup` → `Bakery.WPF/App.xaml.cs:54`

`ConfigureGlobalExceptionHandling()` is not called until `App.xaml.cs:57`, after `_host = CreateHostBuilder(...).Build()` and `StartAsync()`. Consequently, this startup failure is not caught or logged by the application's own handlers.

**Impact:** Every customer receiving the documented publish or the compiled installer receives an application that terminates before login. No functional acceptance test can pass on the actual delivery artifact.

**Required before reassessment:** Correct the single-file-compatible logging configuration or change and document the supported publish model, then rebuild and repeat installed-artifact startup tests on a clean machine.

### C-02 — “Administrative override” has no administrative authorization boundary

**Severity:** Critical  
**Status:** Deterministic current-source defect

`Bakery.Infrastructure/Services/WorkingDayService.cs:333` checks only `PermissionKeys.WorkingDayClose`. The request's plain `AdminOverride` Boolean then bypasses blockers at lines 408 and 439. The only extra requirement is an override reason. There is no `IsSuperAdmin` check and no separate override permission.

The ordinary close dialog exposes this directly to the current user:

- `Bakery.WPF/CloseDayDialog.xaml:174` — checkbox `تجاوز إداري`
- `Bakery.WPF/CloseDayDialog.xaml:175-178` — free-text reason

The bypass covers the blockers calculated by `ValidateCloseBlockersAsync`, including outstanding workflow and financial integrity conditions. The override is recorded, which helps auditing, but recording an unauthorized bypass does not authorize it.

**Impact:** A user permitted to close a day can force closure across conditions intended to protect stock and financial integrity. Closure can be irreversible in normal operations and can affect subsequent business dates and carry-over.

**Required before reassessment:** Introduce and enforce a dedicated privileged override authorization at the service boundary, keep the UI policy consistent with it, and add negative integration tests proving ordinary close-authorized users cannot override.

---

## C. Confirmed high-severity defects

### H-01 — A clean database cannot bootstrap from the installer

`DefaultDataSeeder.SeedBootstrapUserAsync` returns when any user exists, but when no user exists it requires both `BAKERY_BOOTSTRAP_ADMIN_USERNAME` and `BAKERY_BOOTSTRAP_ADMIN_PASSWORD`, with a password of at least 12 characters (`Bakery.Infrastructure/Seeders/DefaultDataSeeder.cs:177-203`). The installer has no credential page, `[Code]` provisioning, or environment-variable setup.

Reproduction with a diagnostic multi-file publish and isolated database:

- All 36 migrations applied.
- Default branch created.
- `Users = 0`.
- Startup logged a fatal `InvalidOperationException` instructing the operator to set the two variables.
- The process did not present the normal login window.

When audit-only variables were supplied, startup succeeded and created one active super-administrator, proving the blocker is specifically provisioning rather than migration failure.

**Impact:** A fresh setup is unusable without undocumented manual process-environment intervention.

### H-02 — The setup executable does not install or validate its database prerequisite

`appsettings.json` targets `(localdb)\MSSQLLocalDB`. `BakeryERP.iss` only copies the publish directory and creates shortcuts; it contains no SQL Server LocalDB/Express prerequisite, bootstrapper, detection, install, or actionable prerequisite failure path.

The deployment guide says LocalDB or SQL Express is required, but setup itself does not provide it. This host already had LocalDB 15 installed, so a true no-SQL-machine failure was not executed.

**Impact:** On a clean non-developer PC without LocalDB/Express, setup can complete while the application cannot create or open its database.

### H-03 — Standard-user runtime writes target protected Program Files paths

The installer uses `{autopf}\Bakery ERP` and requires elevation, but later launches normally as a standard desktop application. Several runtime paths are rooted at the executable directory:

- Logs: relative `Logs/bakery-erp-.log` in `Bakery.WPF/appsettings.json:21`
- Attachments: `AppDomain.CurrentDomain.BaseDirectory\Attachments` in `AttachmentStorageService.cs:32`
- Report previews: `BaseDirectory\TempReports` in `ReportDetailsViewModel.cs:290`
- Recovery log viewer: `BaseDirectory\Logs` in `RecoveryViewModel.cs:118`
- Backup external content: executable-base `Attachments`, `Documents`, `Templates`, and `Logos` in `BackupService.cs:521-525`

Standard users normally cannot create or replace files under Program Files. Logging may silently disappear or fail; attachments and report previews can throw; backup content may omit expected directories.

**Impact:** Common installed-client workflows depend on permissions they will not normally have.

### H-04 — Restore mixes a database transaction with unsafe protected-directory replacement

`BackupRestoreService.RestoreExternalContent` replaces `Attachments`, `Documents`, `Templates`, and `Logos` under `AppContext.BaseDirectory` (`BackupRestoreService.cs:284-299`). Under the installer layout this is Program Files.

The database restore and external-file restore cannot be one atomic transaction. A standard-user failure after database replacement can trigger database rollback attempts while filesystem replacement has already partly moved/copied directories. `ReplaceDirectory` has a local recovery attempt, but that does not make the full database-plus-files operation atomic.

**Impact:** Restore can report failure after materially changing the database or external content, and it can leave a partial file state. This is especially dangerous during disaster recovery.

### H-05 — Item-unit conversion is stored but not applied; the UI accepts unrelated units

`ItemUnit.ConversionFactorToBaseUnit` is configured, saved, and listed, but current operational stock calculations sum raw `InventoryMovement.Quantity` values without applying the factor (`StockCalculationService.cs:20-54`). A repository-wide production-source search found no conversion-factor use in sale, purchase, production, waste, adjustment, or valuation calculations.

The inventory adjustment dialog loads every global unit (`InventoryViewModels.cs:424-432`), not the selected item's allowed units. Its validator checks only positive quantity and a reason (`InventoryValidators.cs:28-34`). `InventoryService.AdjustStockAsync` records the selected `UnitId` and raw quantity (`InventoryService.cs:36-64`) without validating the item/unit relationship or converting to the base unit.

**Impact:** A normal adjustment can label a quantity with an unrelated or non-base unit while the stock ledger treats it as base quantity, corrupting available stock, low-stock alerts, and valuation.

### H-06 — Daily financial reports do not use the working-day business date

`AccountingReportService.GetDailySalesAsync` and `GetDailyPurchasesAsync` filter `InvoiceDate` between naive local `DateOnly` boundaries (`AccountingReportService.cs:33-50`). `GetCashMovementSummaryAsync` similarly filters `CreatedAt` (`AccountingReportService.cs:102-121`). Operational timestamps are generally recorded in UTC, while the application has an explicit `WorkingDay.BusinessDate` model.

At Egypt UTC+3, transactions around local midnight can fall into the preceding UTC calendar date. Reports can also diverge from an explicitly opened business date.

**Impact:** Daily sales, purchase, and cash reports can disagree with the business day that accounting users closed and audited.

### H-07 — Backup archives are unencrypted and the password parameter is ignored

`BackupService.CreateBackupAsync` accepts `string? password` at line 70 but never uses it. `CreateArchiveAsync` writes a normal `ZipArchive` containing the SQL `.bak`, metadata, attachments, documents, templates, logos, and grid settings (`BackupService.cs:463-503`). No archive encryption is applied.

**Impact:** Anyone who obtains a local, USB, network, or cloud backup can read the database and external business data. Employee, payroll, customer, supplier, and financial records are exposed at rest.

### H-08 — Thermal invoice printing serializes the DTO instead of rendering invoice lines

`InvoiceWorkspaceViewModel.PrintInvoiceAsync` loads an `InvoicePrintDto` and passes it to `ThermalPrintService` (`InvoiceWorkspaceViewModel.cs:515-530`). `ThermalPrintService` prints `documentData.ToString()` (`PrintServices.cs:38-43`). The DTO is a C# record whose default `ToString()` includes property labels and a collection type representation; it does not lay out all invoice lines, quantities, unit prices, totals, taxes, or a production-quality receipt.

No physical printer was available, but this is a deterministic rendering path.

**Impact:** Customer receipts can be incomplete or unusable even after the application starts.

### H-09 — Production summary cost and value omit unloaded child collections

`ProductionService.GetProductionSummaryAsync` loads completed orders using only `ToListAsync()` and no `Include` for `ConsumedItems` or `ProducedItems` (`ProductionService.cs:107-129`). Lazy-loading proxies are not configured. It then sums those navigation collections for total cost and produced value.

**Impact:** On a fresh service context, production cost and value totals are expected to be zero or incomplete despite completed orders, misleading dashboard/report users.

---

## D. Medium- and low-severity defects

### Medium

#### M-01 — Sales-by-item reporting is an empty stub

`AccountingReportService.GetSalesByItemAsync` always returns an empty list (`AccountingReportService.cs:53-56`). There is no query.

#### M-02 — Invoice numbering is device-date- and row-count-based

Sale numbers use `DateTime.Today` plus `SaleInvoices.CountAsync() + 1` (`SaleInvoiceService.cs:347`); purchases use the same pattern in `PurchaseInvoiceService.cs:328`. This ignores the active business date, can reuse a number after deletions, and lets concurrent draft creation calculate the same suffix. The unique index prevents silent duplicates but one user's save can fail; no retry/sequence allocation was found.

#### M-03 — Installer lifecycle safeguards are incomplete

`BakeryERP.iss` has no `AppMutex`, explicit close-applications strategy, locked-file handling policy, upgrade-state migration, repair validation, or versioned configuration-preservation logic. `[Files]` uses `ignoreversion`, and `appsettings.json` is copied like any other file. In-place upgrade, downgrade, reinstall, repair, and uninstall-with-data scenarios were not executed because this audit process was not elevated and no clean VM was available.

#### M-04 — Application and installer are unsigned

`Get-AuthenticodeSignature` reports `NotSigned` for both `publish\Bakery.WPF.exe` and `BakeryERP_Setup_v1.0.exe`. This increases Windows SmartScreen/reputation friction and removes publisher identity and tamper verification.

#### M-05 — Restore/build carry .NET Framework compatibility warnings

Every restore/build/publish emitted `NU1701` for transitive packages `OpenTK 3.3.1`, `OpenTK.GLWpfControl 3.3.0`, and `SkiaSharp.Views.WPF 3.119.0`, restored using .NET Framework assets rather than the project target `net8.0-windows`. The build succeeds, but compatibility on all runtime paths is not guaranteed.

#### M-06 — Dependency lifecycle debt remains despite zero reported vulnerabilities

The deprecated-package report identified `xunit 2.9.2` as legacy and reported deprecated transitive identity/system packages, including Azure.Identity 1.11.4, Microsoft.Identity.Client 4.61.3, IdentityModel 6.35.0-family packages, and System.Text.Json 4.7.2. The outdated report contained numerous updates. These do not prove a current vulnerability, but they increase maintenance and future support risk.

#### M-07 — Commercial license eligibility is unresolved

The test run printed a Fluent Assertions warning stating that commercial use requires a paid subscription. Inno Setup 6.7.3 printed `Non-commercial use only`. The application explicitly selects the QuestPDF Community license (`ReportPdfGenerator.cs:22-25`), whose organization eligibility was not established by repository evidence. Legal/license fit must be confirmed before commercial delivery.

#### M-08 — Daily log size is unbounded

Serilog retains 30 daily files but configures no `fileSizeLimitBytes` and no `rollOnFileSizeLimit`. A busy or failing day can produce one very large file. The path problem is separately covered by H-03.

#### M-09 — Forced password-change state is disabled and ignored at login

The bootstrap administrator is created with `MustChangePassword = false` (`DefaultDataSeeder.cs:202`). `AuthService` does not select that column into `LoginCredential` and hardcodes the returned DTO flag to `false` (`AuthService.cs:198-206`). User-management password resets also clear the flag. The schema has a forced-change field, but the authentication flow does not enforce it.

#### M-10 — The test fixture bypasses production validators and clean bootstrap

`DatabaseFixture.cs:33-34` preloads bootstrap environment variables. Lines 54-65 register null validators for sale, purchase, working-day open/close, units, parties, inventory adjustments, stock count, login, and branches. Tests are valuable for service/transaction paths but do not prove production validation wiring or a no-variable first run.

#### M-11 — Startup integrity checking is shallow and has an N+1 pattern

`IntegrityCheckService` checks a limited set of open-day/orphan conditions. It does not reconcile stock movement totals, safe balances, party ledgers, invoice totals, production consumption/output, payroll, or backup history. It also repeats the same orphan-safe-movement query while iterating safes (`IntegrityCheckService.cs:60-74`). A “healthy” startup result is therefore not a comprehensive financial integrity assertion.

#### M-12 — Only one configured backup destination is supported

The implementation offers a single local destination plus optional Google Drive behavior and retains five successful backups. There is no first-class policy for multiple simultaneous local/network/USB destinations, independent retention policies, or quorum verification.

#### M-13 — Build SDK selection is not pinned

There is no `global.json`. This audit built `net8.0-windows` with .NET SDK 9.0.305 and .NET 8.0.20 runtimes. A different installed SDK can alter analyzers, restore, publish, and single-file behavior.

#### M-14 — Error handling can expose internal messages and keep a damaged UI session alive

Several service catch blocks return `ex.Message` to the UI, including invoice and working-day paths. Database/provider details can therefore reach users. The dispatcher handler marks exceptions handled and advises restart, which can allow execution to continue after an unexpected UI exception instead of failing closed.

#### M-15 — Deployment config contains a plaintext OAuth client value and is overwritten on update

`Bakery.WPF/appsettings.json:5-8` contains a Google OAuth client ID and client-secret-shaped value in plaintext. Desktop OAuth client secrets are not a reliable confidentiality boundary, but the value still should not be treated as secret or environment-specific configuration. The installer overwrites `appsettings.json` during an update, which can also replace a customer's connection string or deployment-specific settings.

The literal credential value is intentionally not reproduced in this report.

#### M-16 — Every sale posting depends on a full safety database backup

`SaleInvoiceService.PostAsync` calls `CreateSafetySnapshotAsync` before beginning its invoice database transaction (`SaleInvoiceService.cs:144-150`). This makes routine sales posting dependent on backup destination permissions, disk space, SQL backup performance, and operation-gate availability. On a large production database, it can become a material latency and availability bottleneck.

### Low

#### L-01 — The win-x64 publish contains irrelevant native assets

The publish has 69 files totaling 382,793,215 bytes, including 6 Linux `.so` files, 6 macOS `.dylib` files, win-arm/win-arm64/win-x86 native binaries, and 18 Lato font files. The compiler log confirms all of those are compressed into the Windows x64 setup. The resulting installer is 112,501,294 bytes.

#### L-02 — Formatting verification fails broadly

`dotnet format BakeryERP.sln --verify-no-changes --no-restore --verbosity minimal` exited 1 after 101.8 seconds with numerous `WHITESPACE` diagnostics across production and test projects. No formatting changes were applied.

#### L-03 — “Excel” export is CSV

`ExcelExportService` writes comma-separated text, the dialog filters `*.csv`, but the success message says the data was exported to Excel. The output may open in Excel, but it is not an `.xlsx` workbook and has no worksheet typing/styling.

#### L-04 — Deployment documentation and installer language are inconsistent with the artifact

The deployment guide calls the publish self-contained yet still lists the .NET 8 Desktop Runtime as a client prerequisite. The installer UI includes English only (`BakeryERP.iss:29-30`) although the application UI is Arabic.

#### L-05 — Several services are oversized and query inefficiently

Examples include `WorkingDayService` and `SafeService` at well over one thousand lines, dynamic repository-to-DbContext casts in production services, repeated per-safe permission/balance queries, synchronous WPF file operations, and inconsistent cancellation-token support. These are maintainability/performance debt rather than immediate blockers by themselves.

---

## E. Package, runtime, and framework assessment

### Framework baseline

| Item | Observed |
|---|---|
| Target framework | `net8.0-windows` |
| UI | WPF |
| ORM/database | EF Core 8.0.20 / SQL Server LocalDB or Express |
| Audit SDK | .NET SDK 9.0.305 |
| Installed runtime | Microsoft.NETCore.App 8.0.20; Microsoft.WindowsDesktop.App 8.0.20 |
| Publish RID | `win-x64` |
| Publish mode | Self-contained, single-file |
| Installer compiler | Inno Setup 6.7.3 |

EF Core 8 is still on the supported .NET 8 line; this report does not recommend a major-version upgrade merely because newer majors exist. The priority is resolving incompatible/deprecated transitive dependencies and validating any upgrade with the full database and UI suite.

### Package-check results

| Command | Result |
|---|---|
| `dotnet list BakeryERP.sln package --vulnerable --include-transitive` | PASS — no vulnerable packages reported |
| `dotnet list BakeryERP.sln package --deprecated --include-transitive` | ATTENTION — legacy/deprecated direct and transitive packages reported |
| `dotnet list BakeryERP.sln package --outdated --include-transitive` | ATTENTION — numerous updates available |
| Forced no-cache restore | PASS with repeated `NU1701` compatibility warnings |

### Test coverage

Coverage artifact: `artifacts/audit-coverage-20260721/7941bbc8-4cc2-4d32-a0e1-2ecd88d0942b/coverage.cobertura.xml`

| Scope | Line coverage | Branch coverage |
|---|---:|---:|
| Aggregate | 89.59% | 34.66% |
| Bakery.Application | 58.12% | 40.35% |
| Bakery.Domain | 73.61% | 34.88% |
| Bakery.Infrastructure | 93.78% | 53.83% |
| Bakery.Reporting | 0.00% | 0.00% |
| Bakery.Shared | 41.92% | 35.78% |
| Bakery.WPF | 17.95% | 14.25% |

The high aggregate line rate is not a reliable production-readiness score. Infrastructure includes generated migrations executed during fixture setup, while the UI and reporting layers have low or zero measured coverage. Branch coverage exposes the more important gap.

---

## F. Potential failure risks not fully reproduced

These findings are supported by current-source behavior but were not reproduced with a controlled end-to-end failure during this audit. They are not included in the confirmed severity counts above.

### P-01 — Concurrent stock posting can oversell

Sale posting checks current stock and later inserts negative movements in a normal transaction (`SaleInvoiceService.cs:150-174`). It does not lock an item/aggregate row, use serializable isolation, or update a stock row with a concurrency token. Two concurrent postings can both observe sufficient stock and both commit. Inventory adjustments, waste, and production prechecks have similar check-then-write shapes. Existing tests cover many concurrency cases but no controlled concurrent-sale oversell test was found.

### P-02 — Post-commit summary failure can report failure and skip automatic backup

Working-day close commits at `WorkingDayService.cs:526`, then builds the final summary at line 528, then queues the automatic backup. If summary building throws after commit, the surrounding catch path can attempt rollback on an already committed transaction and the backup is never queued, even though the day is closed.

### P-03 — SQL Express may not be able to write user-profile backup paths

LocalDB runs under the interactive user and passed backup tests here. A SQL Express service account can lack access to `%LOCALAPPDATA%` or a user-selected folder used by `BACKUP DATABASE`. No SQL Express service-account matrix was executed.

### P-04 — Upgrade/reinstall/uninstall may encounter locked files or configuration loss

No installed lifecycle test was possible without elevation and a clean VM. The `.iss` lacks explicit running-app coordination and copies configuration with `ignoreversion`, so locked DLLs and customer config replacement are credible risks.

### P-05 — Live Google Drive consent, refresh, offline retry, and quota behavior may fail

The service correctly requests the narrow `drive.file` scope and protects tokens with DPAPI CurrentUser. Tests use a fake cloud service. Real OAuth consent, token refresh/revocation, large upload, rate limiting, quota exhaustion, proxy, TLS interception, and offline retry were not executed.

### P-06 — Device clock/time-zone changes can alter business identifiers and defaults

Several workflows use `DateTime.Today` or `DateTime.UtcNow` independently of `WorkingDay.BusinessDate`, including first-day defaulting, invoice numbering, dashboard/report defaults, and production numbers. Clock skew or a time-zone change can cause inconsistent dates and collisions.

### P-07 — Parallel failed logins can lose lockout increments

The authentication flow reads `FailedLoginCount`, adds one in memory, then performs an unconditional `ExecuteUpdateAsync`. Parallel failures can write the same next count and delay lockout. Unknown, disabled, and locked users also skip password hashing, which creates a timing difference usable for account-state inference in a local threat model.

### P-08 — Power loss, low disk, network loss, and antivirus interference remain unqualified

The audit did not inject process termination or storage/network faults during migrations, posting, day close, backup, restore, export, or update. One diagnostic multi-file publish did emit `MSB3061` because an output DLL was temporarily in use, plausibly by another process such as antivirus; the publish still completed in a separate output directory.

---

## G. Items not tested and why

| Scenario | Status | Reason |
|---|---|---|
| Actual setup install to Program Files | NOT TESTED | Audit token was medium-integrity/non-administrator; setup requires elevation. Triggering UAC unattended was not appropriate. |
| Fresh physical/VM Windows 10 install | NOT TESTED | No clean Windows 10 VM or machine supplied. |
| Fresh physical/VM Windows 11 install | NOT TESTED | Host was not a clean machine and already had SDKs, runtimes, LocalDB, and development tools. |
| Upgrade from a previous production version | NOT TESTED | No trusted prior installer/database baseline supplied. |
| Repair/reinstall/downgrade | NOT TESTED | Requires installed baseline and elevation. |
| Uninstall and data-preservation verification | NOT TESTED | Requires installed baseline and elevation. Source indicates LocalDB/AppData are not explicitly deleted, but this was not executed. |
| App start from actual single-file payload | FAILED | Reproduced unhandled Serilog single-file exception before any window. |
| Full UI walkthrough on actual delivery payload | BLOCKED | Delivery executable cannot start. |
| Diagnostic multi-file login window | PARTIAL PASS | Reached Arabic RTL login at 125% scaling after audit-only bootstrap. No full operator workflow was performed. |
| DPI 100%, 150%, 200% | NOT TESTED | Only the host's actual 125% scale was available. |
| Small display / 1366×768 | NOT TESTED | Host logical desktop was 1536×864 at 125%; no display matrix automation. |
| Multi-monitor and RTL mixed-DPI moves | NOT TESTED | No controlled monitor matrix. |
| Physical thermal/A4 printers | NOT TESTED | No printer hardware/driver matrix; source rendering defect found. |
| Microsoft Print to PDF | NOT TESTED | Delivery startup blocked; QuestPDF direct export not interactively exercised. |
| Live Google Drive | NOT TESTED | No authorization/consent to use external credentials or cloud storage; tests use a fake. |
| SQL Server Express | NOT TESTED | Only LocalDB 15 was installed/configured. |
| Multiple concurrent desktop clients | NOT TESTED | No multi-process workload harness was present; source race risks documented. |
| Large production dataset | NOT TESTED | No representative anonymized dataset or acceptance thresholds supplied. |
| Power interruption / forced crash | NOT TESTED | Destructive fault-injection environment not supplied. |
| Low disk / read-only disk / USB removal | NOT TESTED | No isolated fault-injection volume. |
| Network backup destination outage | NOT TESTED | No network share/test endpoint supplied. |
| Antivirus/EDR application-control matrix | NOT TESTED | Only the host's current security environment was available. |
| Screen reader, keyboard-only full workflow, color contrast | NOT TESTED | No accessibility automation or manual acceptance matrix. |

---

## H. Architecture and code-quality observations

### Positive controls observed

- Domain money values generally use `decimal`; database mappings consistently use fixed precision (commonly `decimal(18,2)` for money and `decimal(18,3)` for quantity).
- EF Core migrations are present and all 36 applied successfully to the isolated audit database.
- `BaseEntity` row versions are configured broadly, and working-day close/open tests cover several concurrency and idempotency cases.
- Financial posting paths commonly use explicit database transactions.
- Unique indexes protect branch-scoped invoice numbers, item codes/barcodes, safes, business-day/open-day rules, reversal identity, and other critical identities.
- Password hashing uses PBKDF2-SHA256, random 16-byte salts, 100,000 iterations, 32-byte keys, and fixed-time comparison.
- Login lockout is configured for five attempts and 15 minutes.
- Permission checks exist at service boundaries across most audited services, not only in view visibility.
- Session security stamps are rechecked by permission enforcement and changed on user/role/safe-permission updates.
- Backup creation uses SQL `BACKUP DATABASE ... COPY_ONLY, INIT, CHECKSUM`, validates with `RESTORE VERIFYONLY ... CHECKSUM`, and promotes a partial file only after validation.
- Restore validates archive paths against traversal and creates a safety backup before destructive database replacement.
- Backup retention is conservative when files and history disagree or an archive cannot be verified.
- Google Drive uses `drive.file`; tokens are DPAPI-protected for the current Windows user.

### Structural concerns

- Business-date policy is not centralized. Services and VMs independently use device local time, UTC time, and active working-day date.
- Inventory has two concepts—movement unit and base quantity—without one enforced conversion invariant.
- `WorkingDayService` and `SafeService` combine policy, querying, posting, auditing, backup coordination, mapping, and presentation-result construction in very large classes.
- Several services cast repositories dynamically to reach a DbContext, weakening compile-time contracts and testability.
- Report querying is split between Reporting services and WPF view models; some reports are real queries while another is an empty stub.
- Application files are split between proper user-profile paths and executable-relative paths, so deployment behavior depends on which feature is used.
- Audit action names are free-form strings rather than a single stable action catalog, increasing reporting/analytics drift.

---

## I. Performance and resource observations

### Measured timings

| Operation | Result | Observed duration |
|---|---|---:|
| Debug build, no restore | PASS | 39.2 s wall; MSBuild reported 37.67 s |
| Release build, no restore | PASS | 25.2 s wall; MSBuild reported 23.44 s |
| Full Release test run | PASS, 189/189 | 195.7 s wall; test duration 3.1691 min |
| Coverage Release test run | PASS, 189/189 | 173.1 s wall; test runner reported 1 min 13 s |
| Exact single-file publish | PASS | 22.6 s |
| Inno installer compile | PASS | 108.3 s wall; compiler reported 106.469 s |
| Fresh 36-migration diagnostic startup to bootstrap failure | FAIL as designed by missing bootstrap | About 7.7 s from first migration log to fatal seeder log |
| Seeded diagnostic startup to integrity-pass/login stage | PARTIAL PASS | About 4.8 s from process start log to integrity-pass log |
| Format verification | FAIL | 101.8 s |

### Unmeasured or data-dependent performance concerns

- No production-size data set was available, so dashboard, report, grid, stock valuation, history, and backup scaling are unknown.
- `SafeService.ListSafesAsync` and reporting permission checks perform repeated per-safe calls.
- `IntegrityCheckService` repeats a query in a safe loop.
- Routine sale posting creates a database safety snapshot before each invoice.
- Some report queries materialize full lists then filter/aggregate in memory.
- Publish and installer contain native assets for platforms/architectures that cannot run this WPF x64 target.
- WPF coverage is low, so memory retention from view/scoped-service lifetime and repeated navigation was not qualified.

No defensible peak-memory, CPU, database-size, backup-throughput, or large-grid threshold can be reported from this audit. The diagnostic login process was observed at roughly 297 MB working set, but a single idle observation is not a load benchmark.

---

## J. Logging and auditing assessment

### Logging

- Application logs are structured through Serilog with daily rolling and 30-file retention.
- The release artifact currently crashes while configuring those logs, before the app can record its own startup failure.
- The configured relative log path resolves under the installation directory.
- No daily file-size ceiling is configured.
- Global WPF exception handlers are registered after host construction/start, leaving early startup outside their protection.
- Several user-facing failure paths expose raw exception messages.
- No evidence was found that passwords or Google tokens are logged. Usernames and entity IDs appear in authentication/audit records, which is expected but should be covered by retention/access policy.

### Audit trail

- Audit writes exist for authentication failures, inventory adjustments, posting, working-day operations, backup actions, user/role changes, and many other sensitive actions.
- Audit queries and export are permission-protected.
- Working-day override reason and blockers are recorded.
- Audit action identifiers are inconsistent free-form English/human labels rather than centralized immutable codes.
- Startup integrity checking is not a financial reconciliation and should not be represented as one in operational procedures.
- Log/audit retention, privacy, and regulatory requirements are not documented in the repository evidence reviewed.

---

## K. Commands and evidence

### Build, test, dependency, publish, and installer commands

| Command | Exit/result | Evidence summary |
|---|---|---|
| `dotnet restore BakeryERP.sln --force --no-cache --verbosity minimal` | 0 / PASS | All projects restored; recurring NU1701 warnings for OpenTK/Skia WPF transitive assets |
| `dotnet build BakeryERP.sln -c Debug --no-restore` | 0 / PASS | 0 errors, 9 warnings |
| `dotnet build BakeryERP.sln -c Release --no-restore` | 0 / PASS | 0 errors, 9 warnings |
| `dotnet test BakeryERP.sln -c Release --no-build --logger "console;verbosity=normal"` | 0 / PASS | 189 passed, 0 failed, 0 skipped |
| `dotnet test BakeryERP.sln -c Release --no-build --collect:"XPlat Code Coverage" ...` | 0 / PASS | 189 passed; Cobertura coverage generated |
| `dotnet list BakeryERP.sln package --vulnerable --include-transitive` | 0 / PASS | No vulnerable packages reported |
| `dotnet list BakeryERP.sln package --deprecated --include-transitive` | 0 / ATTENTION | Deprecated/legacy packages reported |
| `dotnet list BakeryERP.sln package --outdated --include-transitive` | 0 / ATTENTION | Numerous updates reported |
| Documented publish plus `--no-restore` | 1 / EXPECTED PRECONDITION FAILURE | `NETSDK1047`: assets lacked `win-x64`; repeated without `--no-restore` |
| `dotnet publish Bakery.WPF\Bakery.WPF.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o publish --verbosity minimal` | 0 / PASS | Exact single-file release built; same NU1701 warnings |
| Multi-file diagnostic publish with `PublishSingleFile=false` | 0 / PASS WITH WARNING | Used only to isolate later startup/database behavior; one MSB3061 in-use output warning |
| `ISCC.exe BakeryERP.iss` | 0 / PASS | Inno Setup 6.7.3; successful compile in 106.469 s |
| `dotnet format BakeryERP.sln --verify-no-changes --no-restore --verbosity minimal` | 1 / FAIL | Numerous whitespace diagnostics; no files changed |

### Runtime and database evidence

| Check | Result |
|---|---|
| Single-file isolated launch | FAIL — process exit `-532462766`; .NET Runtime event 1026 identifies Serilog single-file exception |
| Fresh multi-file isolated database without bootstrap variables | FAIL — 36 migrations applied, zero users, fatal bootstrap exception |
| Same database with audit-only bootstrap variables | PARTIAL PASS — one active super-admin created; Arabic RTL login window shown |
| `DBCC CHECKDB ('BakeryERP_Audit_Multifile_NoBootstrap_20260721') WITH NO_INFOMSGS` | PASS — no error output, sqlcmd exit 0 |
| Schema counts | 44 tables; 102 foreign keys; 312 non-heap indexes; 36 EF migrations |
| Seed counts | 1 user; 5 roles; 84 permissions |
| Database size | 16.00 MB after fresh migration/seed |
| Host display used | 125% DPI (120 DPI); logical 1536×864 |

### Artifact identities

| Artifact | Size | SHA-256 | Signature |
|---|---:|---|---|
| `publish\Bakery.WPF.exe` | 188,942,721 bytes | `FE3A598E9A5378B48684EEBA7BA066CC45EB50873B1F1A352B27C9CAE07B2494` | NotSigned |
| `BakeryERP_Setup_v1.0.exe` | 112,501,294 bytes | `E9DD3DD1A5598B87AAB6D5E687A237E2B2202C6C352C3D96B063763E3C452403` | NotSigned |

### Audit artifacts created

- `PRE_DELIVERY_FULL_AUDIT.md` — this report
- `artifacts/audit-runtime-20260721-no-bootstrap/` — isolated single-file launch copy with audit-only database-name configuration
- `artifacts/audit-runtime-20260721-multifile/` — diagnostic multi-file publish, startup logs, and isolated config
- `artifacts/audit-multifile-login-window-125pct.png` — diagnostic Arabic RTL login evidence at 125%
- `artifacts/audit-coverage-20260721/.../coverage.cobertura.xml` — measured coverage
- LocalDB audit database `BakeryERP_Audit_Multifile_NoBootstrap_20260721`

No application/domain/infrastructure/reporting/WPF source fix was made. The normal build/publish/installer outputs and audit-only artifacts above were generated as requested.

---

## L. Production-readiness checklist

| Area | Status | Notes |
|---|---|---|
| Forced clean restore | PASS WITH WARNINGS | NU1701 compatibility warnings |
| Debug build | PASS WITH WARNINGS | 0 errors, 9 warnings |
| Release build | PASS WITH WARNINGS | 0 errors, 9 warnings |
| Full integration tests | PASS | 189/189 |
| Branch coverage | FAIL | 34.66% aggregate; WPF 14.25%, Reporting 0% |
| Vulnerable package scan | PASS | None reported |
| Deprecated/outdated package posture | FAIL | Lifecycle debt and legacy dependencies |
| Formatting verification | FAIL | Numerous whitespace diagnostics |
| Exact documented publish | PASS TO BUILD | Artifact builds but fails runtime startup |
| Exact artifact startup | FAIL — BLOCKER | Unhandled Serilog single-file exception |
| Installer compilation | PASS | Inno Setup 6.7.3 |
| Installer signature | FAIL | Not signed |
| Application signature | FAIL | Not signed |
| Fresh installer usability | FAIL BY DESIGN/SOURCE | No DB prerequisite and no bootstrap credential provisioning |
| Clean database migration | PASS | 36/36 applied in diagnostic multi-file build |
| Database structural integrity | PASS | DBCC CHECKDB; 102 FKs; 312 indexes |
| Fresh first-run login | FAIL ON DELIVERY PATH | Multi-file only succeeds with external audit variables |
| Login/lockout/password hashing | PARTIAL PASS | Good hash/lockout basics; concurrency/timing/forced-change gaps |
| Service permission enforcement | PARTIAL PASS | Broad coverage; critical close-override authorization missing |
| Working-day normal close/open atomicity | PASS IN TESTS | Extensive tests passed |
| Working-day administrative override | FAIL — BLOCKER | Ordinary close permission can bypass all blockers |
| Sale/purchase transaction atomicity | PARTIAL PASS | Core transactions present; concurrency numbering/stock risks |
| Inventory unit correctness | FAIL | Conversion factor not applied; unrelated units accepted |
| Stock concurrency safety | NOT PROVEN | Check-then-write race risk |
| Party/safe/accounting ledgers | PARTIAL PASS | Tests pass; no production reconciliation/data-scale test |
| Employee/payroll/settlement | PARTIAL PASS | Automated paths pass; no full UI/print/export acceptance |
| Production summary correctness | FAIL | Child collections not loaded for totals |
| Daily business-date reporting | FAIL | Timestamp calendar filters instead of WorkingDay.BusinessDate |
| Sales-by-item report | FAIL | Empty stub |
| Thermal invoice printing | FAIL BY SOURCE PATH | DTO `ToString()` instead of invoice layout |
| PDF generation | PARTIAL PASS | Code present; Reporting measured 0% coverage; license eligibility unknown |
| CSV export | PASS WITH LABELING ISSUE | Not a true Excel workbook |
| Logging startup | FAIL — BLOCKER | Causes single-file crash |
| Logging path/rotation | FAIL | Program Files-relative; no size cap |
| Audit logging | PARTIAL PASS | Broad writes; action vocabulary/retention policy gaps |
| Manual backup | PASS IN INTEGRATION TESTS | Real LocalDB test path; no installed-client permission test |
| Backup validation/atomic promotion | PASS IN TESTS/CODE | CHECKSUM + VERIFYONLY + partial promotion |
| Backup confidentiality | FAIL | Plain ZIP; ignored password |
| Backup retention | PASS IN TESTS | Latest five with conservative deletion |
| Multiple backup destinations | FAIL | Not supported as requested |
| Automatic close backup | PARTIAL PASS | Queue tests pass; post-commit gap risk |
| Restore database validation | PASS IN TESTS | Safety snapshot and validation present |
| Restore installed-file safety | FAIL | Protected Program Files external content replacement |
| Google Drive scope/token storage | PASS BY CODE/TEST | `drive.file`, DPAPI CurrentUser |
| Live Google Drive upload/recovery | NOT TESTED | No real consent/cloud authorization |
| Standard-user installed paths | FAIL | Attachments/logs/temp/external restore under executable directory |
| Upgrade/reinstall/repair | NOT TESTED / NOT READY | Missing lifecycle safeguards; no elevated clean baseline |
| Uninstall/data preservation | NOT TESTED | No elevated installed baseline |
| Windows 10 compatibility | PARTIAL | Audit host reports Windows 10 Home 64-bit build 26200; not clean |
| Windows 11 compatibility | NOT TESTED | No clean Windows 11 target |
| Arabic RTL UI | PARTIAL PASS | Login screenshot in diagnostic multi-file build at 125% |
| DPI/small-screen matrix | NOT TESTED | Only 125%, logical 1536×864 |
| Keyboard/accessibility | NOT TESTED | No full acceptance matrix |
| Large-data performance | NOT TESTED | No representative dataset/thresholds |
| Power loss/low disk/network loss | NOT TESTED | No isolated fault-injection environment |

---

## M. Final delivery recommendation

# BLOCK DELIVERY

This is not a borderline decision. The actual prescribed release executable does not start, and the compiled installer contains that executable. A clean setup also lacks both database prerequisite provisioning and first-administrator provisioning. In addition, the current working-day administrative override crosses a critical authorization boundary, and high-severity stock, restore, backup confidentiality, reporting, and printing defects remain.

Minimum exit criteria for a new independent audit:

1. Produce a signed release artifact that starts from the exact installed payload on clean Windows 10 and Windows 11 machines.
2. Make setup install/detect prerequisites and provide a secure, documented first-administrator flow with forced credential change where appropriate.
3. Enforce privileged working-day override authorization in the service layer and prove it with negative tests.
4. Enforce item/unit relationships and base-unit conversion for every stock-producing/consuming path.
5. Serialize or otherwise concurrency-protect stock availability and number generation.
6. Move all mutable application data to supported user/program-data locations and make database-plus-external restore recoverable.
7. Encrypt sensitive backups or explicitly remove the misleading password surface; test LocalDB and SQL Express service-account destinations.
8. Correct business-date reporting, production summary loading, sales-by-item reporting, and receipt rendering.
9. Resolve signature, commercial-license, package-compatibility, and configuration-preservation concerns.
10. Repeat the full clean restore/build/test/coverage/publish/installer/install/upgrade/uninstall/runtime/UI/backup/restore/cloud/fault-injection matrix and retain evidence from the exact candidate installer.

Until those conditions are met and independently re-verified, the application is **not safe to deliver to real bakery users**.
