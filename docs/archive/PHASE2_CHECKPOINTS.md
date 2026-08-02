# Bakery ERP — Phase 2 Checkpoints

## H-03 — Mutable runtime paths under Program Files

Status: **Fixed and checkpointed — 2026-07-22**

- Confirmed the audited executable-directory writes in attachment storage, report previews, recovery log access, backup content collection, and restore replacement.
- Added one `IApplicationPathService` implementation rooted at `%LOCALAPPDATA%\BakeryERP`.
- Centralized Logs, Attachments, TempReports, Documents, Templates, Logos, grid settings, restore work, and cloud-backup download staging.
- Updated backup/archive and restore consumers to use the same content locations.
- Kept configured external backup destinations supported by `BackupPathProvider`.
- Release build passed.
- Focused path/logging/backup tests passed: 14/14, including the final source-contract assertion.
- Runtime proof passed: the current Release app opened normally, created all mutable directories under `%LOCALAPPDATA%\BakeryERP`, wrote its current log there, and did not create or update mutable content beside the executable. A stale executable-side log from 2026-07-20 remained untouched.

## H-04 — Recoverable restore workflow

Status: **Fixed and checkpointed — 2026-07-22**

- Confirmed the existing safety backup and database rollback, then proved the remaining cross-file gap: directory replacements were committed independently and grid settings were not restored when absent from the safety archive.
- Selected and safety archives now extract fully into one per-operation staging directory before replacement.
- External content and optional grid settings now use exact replace semantics, including restoring absence rather than leaving selected data behind.
- Any replacement failure rolls the database and all external content back from the validated safety archive.
- If rollback itself fails, selected/safety staging is retained with `recovery-required.json`; the `RestoreResult` exposes that recovery directory and the validated safety archive remains available.
- Added deterministic failure injection at database, partial external-content, and rollback checkpoints.
- Release build passed with 0 errors.
- Backup/restore regression tests passed: 14/14. This includes a partial external failure that restored the prior database, attachment, and grid settings, plus a forced rollback failure that preserved a manifest and was then manually recovered from the recorded safety archive.

## H-05 — Inventory unit correctness

Status: **Fixed and checkpointed — 2026-07-22**

- Confirmed that conversion factors were persisted but unused and that the adjustment UI exposed every branch unit.
- Added one item/unit conversion service that treats the item's base unit as factor 1, validates every non-base relationship, and converts quantities and unit costs in batches.
- New inventory movements from purchases, sales, adjustments, stock counts, waste, production consumption, and production output are now stored in the item's base unit. Cancellation/return movements reverse those base-unit snapshots exactly.
- Draft invoice validation, waste, adjustments, recipes, production validation/posting, and stock counts reject unrelated units in the service layer.
- The adjustment dialog now loads only the selected item's base/allowed units. Unit conversion factors and item base units cannot be changed after relevant ledger history would make reinterpretation unsafe.
- Existing non-base inventory movements remain readable without a destructive data rewrite: balances, movement history, waste stock-after, low-stock state, and valuation normalize them dynamically.
- Stock-count system quantities are recomputed server-side; client values are ignored and physical selected-unit quantities are converted to base units.
- Stock valuation now uses normalized base quantity multiplied by the item's base-unit purchase price.
- Release build passed with 0 errors.
- New end-to-end conversion regression passed: 1/1. Existing inventory, purchase, production, and accounting workflow regressions passed: 5/5. The test matrix covers base/non-base units, legacy rows, unrelated-unit rejection, adjustment, count, waste, purchase, sale, cancellation/return, recipe, production consumption/output, movement history, and valuation.

## P-01 — Stock concurrency safety

Status: **Fixed and checkpointed — 2026-07-22**

- Confirmed the check-then-write race under the prior default transaction behavior.
- Added a shared SQL Server transaction-owned application lock keyed by branch and item. Multi-item operations acquire locks in stable item order to avoid lock-order deadlocks.
- Sale, purchase, adjustment, stock-count, waste, production-post, and cancellation/return movement writers now participate in the same lock invariant.
- Availability checks for sales, waste, adjustments, and production now execute while the item lock and the movement-writing transaction are both active.
- Release build passed with 0 errors.
- Controlled concurrent-sale regression passed: two independent DbContexts attempted to consume the same 10 units at the same time; exactly one posted, the other remained Draft, and the database balance was 0 rather than -10.
- The full cross-workflow inventory-unit regression was rerun after locking and passed 1/1.

## H-06 — Business-date reporting

Status: **Fixed and checkpointed — 2026-07-22**

- Confirmed that accounting daily sales, purchases, and cash movement reports filtered UTC-stored timestamps through naive calendar-day boundaries.
- Added one branch-scoped business-date resolver that maps an explicit/current `WorkingDay.BusinessDate` to its stable `WorkingDayId`.
- Daily sales, purchase, and cash reports now filter by that working-day identity, not invoice or movement timestamps.
- Existing dashboard, treasury, closing, and trend queries were verified to already use working-day identity and were preserved.
- Production and waste “today” summaries now use the same current business-day resolver instead of `DateTime.UtcNow.Date`.
- Release build passed with 0 errors.
- Cross-midnight integration regression passed 1/1: transactions timestamped on the neighboring UTC calendar date remained in their assigned Egypt business day; two adjacent working days returned isolated sales, purchase, cash, dashboard/trend, waste, and production totals.

## H-09 — Production summary child loading

Status: **Fixed and checkpointed — 2026-07-22**

- Confirmed that the summary loaded only production-order headers and then summed unloaded navigation collections with lazy loading disabled.
- Replaced header materialization/navigation access with database-side aggregates over completed consumed and produced rows for the current business day.
- Order count, consumed cost, and produced value now come from explicit queries and do not depend on DbContext tracking state.
- Release build passed with 0 errors.
- Fresh-scope regression passed 1/1 with two completed orders: count 2, consumed cost 14, and produced value 38.

## H-10 — Sales By Item report

Status: **Fixed and checkpointed — 2026-07-22**

- Confirmed that the reporting method returned an unconditional empty collection.
- Added a typed per-item result with base-unit quantity, gross sales, discounts, return quantity/value, net quantity, and net sales.
- The query filters by `WorkingDay.BusinessDate` through `WorkingDayId`; the DbContext's branch query filters keep results in the current branch.
- Posted sales contribute to gross totals. Cancelled invoices contribute only when a recorded sale-cancellation inventory reversal proves that the invoice had been posted; discarded drafts are excluded.
- Quantities from non-base invoice lines are converted to the item's base unit before aggregation.
- The current schema has no discount column, so `Discounts` is explicitly 0 rather than inferred or fabricated. Net sales still apply the field consistently for forward compatibility.
- Release build passed with 0 errors.
- Sales-by-item and business-date regressions passed 2/2, covering non-base units, posted returns, excluded cancelled drafts, missing-date results, gross/return/net arithmetic, and adjacent business days.

## H-08 — Thermal invoice receipt rendering

Status: **Fixed and checkpointed — 2026-07-22**

- Confirmed that the thermal path printed the default C# record `ToString()` value and therefore did not enumerate receipt lines.
- Added a dedicated, printer-independent thermal receipt renderer and kept printer dispatch in the print service.
- Invoice print data now carries business/branch header, document type, cashier, payment type, discount, tax, footer, and line unit symbols in addition to the existing invoice/customer/payment totals.
- The rendered receipt includes invoice number/date/cashier/customer, every item, quantity, unit, unit price, line total, subtotal, discount, tax, total, paid, remaining, business-day audit line, printed-by timestamp, and footer.
- Thermal dispatch now rejects non-invoice objects instead of falling back to `object.ToString()`.
- Release build passed with 0 errors.
- Renderer plus sale/purchase workflow regressions passed 3/3. The renderer test proves both invoice lines and all required totals appear and that record/collection type serialization does not.
- Physical-printer output remains a final hardware acceptance item; the deterministic render and dispatch source paths are verified.

## H-07 — Backup confidentiality

Status: **Fixed and checkpointed — 2026-07-22**

- Confirmed that the compatibility `password` argument was discarded and that every published backup was a directly readable ZIP containing the SQL database backup and external business files.
- New backups are published as versioned `.berpbackup` encrypted envelopes. The inner ZIP is encrypted with AES-256-CBC and authenticated using encrypt-then-MAC HMAC-SHA-256; the complete header and ciphertext are authenticated before any plaintext is staged.
- Unattended automatic, safety, and ordinary manual backups use a random per-user device master key protected by Windows DPAPI `CurrentUser`. Each backup derives independent encryption and authentication keys from a fresh random salt.
- The existing `CreateBackupAsync(customPath, password)` API now creates portable password-protected backups. PBKDF2-HMAC-SHA-256 with 210,000 iterations derives separate keys; password policy is enforced and passwords are not stored in metadata, history, audit JSON, filenames, or log templates.
- Validation, local/history/cloud restore, safety rollback, retention, and final-reopen checks all understand the encrypted envelope. Wrong passwords and any authenticated-byte tampering fail before ZIP parsing or SQL restore.
- Legacy ZIP backups remain validation/restore compatible. The open/save dialogs accept both `.berpbackup` and `.zip` during migration.
- Decryption and validation staging uses the centralized `%LOCALAPPDATA%\BakeryERP\RestoreWork` location. Startup cleanup removes abandoned staging while preserving any directory carrying `recovery-required.json`.
- The binary format, key modes, portability boundary, and compatibility policy are documented in `BACKUP_ENCRYPTION_FORMAT.md`.
- Release build passed with 0 errors.
- All backup-focused regressions passed 17/17. The matrix includes the formerly ignored password overload, non-ZIP ciphertext, correct/wrong/missing password behavior, tamper rejection, no password bytes in the artifact, device-key backups, legacy ZIP validation, retention, automatic workflow behavior, encrypted safety backups, successful password restore, partial failure rollback, and retained manual-recovery state.
