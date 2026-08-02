# Bakery ERP — Phase 3 Checkpoints

## Checkpoint 14 — Installer lifecycle safeguards

- Confirmed the installer had no application mutex, clean close policy, or explicit business-data preservation statement.
- Added a stable application mutex and single-instance guard.
- Configured Inno Setup to detect/close the running application, handle locked files, preserve the previous install location, and avoid restarting the app during setup.
- Added an Arabic uninstall confirmation that states program files are removed while business data is preserved.
- Added installer lifecycle source-contract tests.
- Verification: 2 focused tests passed; Inno Setup 6.7.3 compiled successfully.
- Phase 4 lifecycle evidence: per-user fresh install, upgrade from the Phase 1 installer, same-version reinstall, uninstall, and post-uninstall reinstall all exited successfully; installed and published executable hashes matched.
- The lifecycle run exposed an older-installer edge case where a registered legacy `appsettings.json` could be removed. `InitializeUninstall` now copies such a file to `%LOCALAPPDATA%\BakeryERP\appsettings.legacy-uninstall.json` and aborts removal if preservation fails. A byte-for-byte runtime preservation test passed, while all other user data and both isolated databases remained unchanged.
- Status: complete on the available Windows/LocalDB host. Default elevated Program Files and clean-VM OS matrices remain external acceptance items.

## Checkpoint 15 — Defaults and customer configuration separation

- Confirmed a real Google OAuth client secret was present in the shipped `appsettings.json`, and the installer would overwrite that file.
- Replaced it with immutable, non-secret `appsettings.defaults.json`.
- Added a writable per-user override at `%LOCALAPPDATA%\BakeryERP\appsettings.user.json`, created atomically and never overwritten.
- Added one-time migration of an existing installed `appsettings.json`, configuration precedence, JSON/connection-string/OAuth pair validation, and safe startup errors.
- Excluded both legacy and user configuration files from installer payload copying.
- Updated deployment documentation and regression tests.
- Verification: 8 focused tests passed; shipped defaults contain no OAuth credentials.
- Status: complete.

## Checkpoint 16 — Invoice number generation

- Confirmed sales and purchases used device date plus `CountAsync() + 1`, allowing duplicates under concurrency and reuse after deletion.
- Added `IInvoiceNumberAllocator` backed by the existing `TransactionNumberCounters` table.
- Numbers now use the active `WorkingDay.BusinessDate`, branch, document type, and date-specific counter.
- Allocation uses a transaction-owned SQL application lock and atomic counter update/insert; legacy and soft-deleted invoice numbers are included when establishing the next value.
- Refreshed the working-day concurrency token while the allocation lock is held, retaining day-close protection for concurrent terminals.
- Added coverage for business-date numbering, sale/purchase sequences, soft deletion, and controlled concurrent creation.
- Verification: allocation test passed; 10 related invoice/end-to-end/unit/standalone-context tests passed; Release build succeeded with 0 errors.
- Status: complete.

## Checkpoint 17 — Routine sale safety and performance

- Confirmed every normal sale post synchronously created a full database safety snapshot before opening the posting transaction.
- Measured the original LocalDB path: 1,348 ms total posting time, including 1,040 ms in the safety snapshot.
- Removed the routine snapshot dependency from sale posting; migration and restore safety backups remain unchanged.
- Retained the atomic database transaction, item-level database lock, working-day concurrency guard, and transactional audit write.
- Made repeat posting idempotent: an already-posted invoice returns success without duplicate stock, safe, party-ledger, or audit writes; a concurrent retry resolves a committed post after rollback.
- Measured the same scenario after the change: 390 ms total posting time and 0 ms in snapshots, a 958 ms (71%) observed reduction.
- Added sequential and controlled two-terminal retry tests that reconcile stock, party ledger, audit count, and backup invocation count.
- Verification: 4 focused sale reliability, stock-concurrency, and end-to-end tests passed.
- Status: complete.

## Checkpoint 18 — Logging and operator-safe errors

- Reconfirmed logs use the centralized writable user-data path; normal and bootstrap files roll daily, roll at 10 MiB, and retain 30 and 14 files respectively.
- Added a structured JSON formatter that redacts password, token, authorization, credential, client-secret, and connection-string fields before writing templates, nested properties, rendered messages, or exception text.
- Applied the same redactor to synchronous startup fallback and emergency recovery logs.
- Added a single operator error boundary: explicit validation/authorization/business messages may cross it, while provider and unexpected details are logged and replaced with a safe Arabic message.
- Removed raw unexpected/provider exception display from invoice, working-day, purchase, sale, treasury, report/export, recovery, production, health, employee-ledger, settings, and related UI paths.
- Added missing diagnostic logging to purchase posting, party payments, and first-run setup failures.
- Verification: 9 logging/startup/path tests and 24 affected workflow/DI tests passed; redacted output is parsed as valid structured JSON and contains none of the injected secrets.
- Status: complete.

## Checkpoint 19 — Stable audit action catalog

- Confirmed audit actions were supplied as scattered free-form strings, including generic verbs, compact identifiers, and human-readable English phrases.
- Added the immutable `AuditActionKeys` catalog and replaced every infrastructure audit write and action query with catalog references while preserving existing stored values and historical data compatibility.
- Added an `AuditService` boundary check that rejects new free-form identifiers, preventing vocabulary drift.
- Moved complete Arabic action labels into the separate `AuditActionArabicLocalizer`; the database continues to store only stable identifiers.
- Kept audit detail payloads as structured JSON and added persistence assertions for operation identifiers and decimal values.
- Added source-contract coverage proving infrastructure audit writes no longer embed action string literals.
- Verification: 4 catalog/persistence/localization/guard tests plus 5 affected lifecycle, first-run, and sale tests passed.
- Status: complete.

## Checkpoint 20 — Dependency warnings and SDK policy

- Traced NU1701 to LiveCharts → SkiaSharp.Views.WPF → legacy OpenTK/OpenTK.GLWpfControl, matching the upstream SkiaSharp defect report.
- Upgraded LiveCharts 2.0.2 → 2.0.5 and SkiaSharp WPF/HarfBuzz 3.119.0 → 3.119.4.
- Applied bounded patch-line updates found by the final scan: EF Core 8.0.20 to 8.0.29, CommunityToolkit.Mvvm 8.4.0 to 8.4.2, OpenTK 3.3.1 to 3.3.3, OpenTK.GLWpfControl 3.3.0 to 3.3.1, Serilog 4.3.0 to 4.3.1, coverlet 6.0.2 to 6.0.4, and xUnit 2.9.2 to 2.9.3.
- Proved removing OpenTK is unsafe: the executed WPF chart path failed loading `GLWpfControl`; restored and directly pinned the required runtime assemblies.
- Scoped NU1701 only to the three audited direct package references; no global warning suppression was added.
- Added `global.json` with SDK 9.0.305 minimum, latest-feature roll-forward, and prereleases disabled.
- Added runtime tests for WPF chart rendering, Skia native draw/encode, and QuestPDF generation; ran existing thermal printing, report, treasury print-routing, and real WPF login tests.
- Documented the evidence and upstream sources in `DEPENDENCY_DECISION.md`.
- Verification: forced no-cache restore completed with 0 warnings; Release build completed with 0 warnings and 0 errors; 15 presentation/runtime tests passed.
- The self-contained release is pinned to .NET 8 servicing runtime 8.0.29; final scans reported 0 known vulnerable entries and 0 outstanding top-level patch updates. The xUnit v2 and SQL-client transitive identity deprecations remain explicitly documented migration debt.
- Status: complete.
