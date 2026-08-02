# Bakery ERP — Phase 2 Verification Report

Date: 2026-07-22  
Scope: confirmed high-severity data, reporting, backup, printing, restore, and deployment-path findings from `PRE_DELIVERY_FULL_AUDIT.md`

## Outcome

All Phase 2 source issues H-03 through H-10 and the confirmed stock-concurrency problem P-01 are fixed and checkpointed. The final Release build and complete integration suite pass. This closes Phase 2 only; it is not a release approval because the requested Phase 3 and Phase 4 work remains.

## Fixed findings

| Audit ID | Result | Verification evidence |
|---|---|---|
| H-03 mutable Program Files paths | Fixed | One `%LOCALAPPDATA%\BakeryERP` path service; source contract, path tests, and installed-payload runtime write proof |
| H-04 unsafe cross-resource restore | Fixed | Staged selected/safety restore, exact file rollback, failure injection, retained recovery manifest when rollback fails |
| H-05 inventory unit correctness | Fixed | Item/unit enforcement and base-unit normalization across purchase, sale, count, adjustment, waste, production, return, history, and valuation |
| P-01 stock check/write race | Fixed | Transaction-owned SQL application locks per branch/item; controlled concurrent oversell regression |
| H-06 business-date reporting | Fixed | Central WorkingDay resolver; adjacent-day and Egypt-midnight regressions |
| H-09 unloaded production children | Fixed | Explicit database aggregates from a fresh DbContext |
| H-10 empty Sales By Item report | Fixed | Quantity/gross/discount/return/net query with business-day and branch scope |
| H-08 unusable thermal serialization | Fixed | Structured receipt renderer separated from print dispatch; no DTO `ToString()` path |
| H-07 plaintext backups/ignored password | Fixed | Authenticated encrypted envelope, DPAPI device mode, PBKDF2 password mode, tamper rejection, legacy ZIP compatibility |

Detailed per-finding evidence is recorded in `PHASE2_CHECKPOINTS.md`. The backup format and portability policy are documented in `BACKUP_ENCRYPTION_FORMAT.md`.

## Direct database reconciliation

`EndToEndSystemTests.CompleteBusinessDayWorkflow_ShouldSucceedAndMaintainIntegrity` now performs a fresh operational workflow and independently compares service/report output with persisted ledger data:

- Purchase header total `7000` equals the sum of purchase lines; paid and remaining fields reconcile.
- Sale header total `300` equals the sum of sale lines; paid and remaining fields reconcile.
- Finished-product stock `30` from `IStockCalculationService` equals the signed base-unit `InventoryMovements` sum.
- Production consumed cost `800` and produced value `800` equal direct child-row aggregates for the completed order.
- Daily purchase/sales reports equal posted invoice totals for `WorkingDay.BusinessDate`.
- Sales By Item reports quantity `20`, gross/net sales `300`, and no returns for the posted product.
- Treasury/report balance `43300` equals the direct `SafeMovements` sum: opening `50000`, purchase `-7000`, sale `+300`.
- The integrity check passes and the working day closes successfully after reconciliation.

## Build and test results

- Release build: **PASS**, 0 errors.
- Full solution test gate: **PASS**, 207 passed, 0 failed, 0 skipped.
- Encrypted backup-focused gate: **PASS**, 17/17.
- Encrypted restore/rollback gate: **PASS**, 3/3.
- Direct reconciliation workflow: **PASS**, 1/1.
- Pre-existing NU1701 compatibility warnings for OpenTK/OpenTK.GLWpfControl/SkiaSharp.Views.WPF remain visible and are reserved for the requested Phase 3 dependency review; they were not hidden or suppressed.

The first full-suite attempt exposed three stale standalone test-fixture registrations after the new path/unit/lock dependencies. The fixture was corrected to use the production path, unit-conversion, and stock-lock services; its focused tests then passed 6/6 and the full 207-test gate passed.

## Residual acceptance items

- Physical thermal-printer output still requires hardware acceptance.
- SQL Express service-account access to user-selected backup destinations remains an environment matrix item.
- Live Google Drive OAuth/upload/download and cross-device recovery remain external integration tests.
- Clean installer upgrade/reinstall/uninstall, writable customer configuration, invoice-number allocation, sale backup performance, logging hardening, typed audit keys, dependency replacement, and the final exact-artifact matrix belong to Phases 3–4.

## Phase recommendation

**Phase 2 complete. Continue to Phase 3; do not approve delivery yet.**
