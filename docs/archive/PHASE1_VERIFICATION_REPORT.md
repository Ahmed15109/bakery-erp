# Bakery ERP — Phase 1 Verification Report

Date: 2026-07-22 (Africa/Cairo)

## Gate result

**PASS — no confirmed Critical delivery blocker remains from Phase 1.**

| Finding | Verification result | Status |
|---|---|---|
| C-01 — single-file Serilog startup crash | Explicit sink assembly loading, bootstrap logging before host construction, fallback startup log, exact single-file runtime launch | Fixed |
| C-02 — unauthorized Working Day override | Dedicated `WorkingDay.OverrideCloseBlockers` permission enforced in service and UI; denial and success paths audited and tested | Fixed |
| H-01 — unusable clean database | Interactive first-run administrator flow with password policy, database lock, one-admin guarantee, role/branch assignment, and password-free audit details | Fixed |
| H-02 — missing database prerequisite handling | Installer detects x64 LocalDB 2019+ and blocks with actionable Arabic instructions and an official Microsoft download link when absent | Fixed |

## Automated verification

- Forced no-cache restore: passed.
- Release build: passed with 0 errors.
- Full Release test suite: **196 passed, 0 failed, 0 skipped**.
- Focused Phase 1 security/startup/provisioning/installer tests: **8 passed**.
- Exact publish command passed:
  `dotnet publish Bakery.WPF\Bakery.WPF.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o publish`
- Inno Setup 6.7.3 compile: passed.
- Final setup SHA-256: `D59E53FE93D02B7B1A2300C9CE03FD920EB6657B3FDEC33B08A2FF1811C2E5ED`.

## Installed-artifact runtime verification

- Silent per-user fresh install: exit code 0.
- Published and installed executable hashes match:
  `585220B7A9C5BA883DC3AFCD118C9D9DACE87DBE2D344C66CCB0B7D01DEBF18E`.
- Empty isolated LocalDB: 36 migrations applied; first window was `إعداد مسؤول النظام`.
- First administrator created through the real installed UI with a user-selected 12+ character password.
- Database evidence: 1 active super-administrator, password stored as a 90-character hash, protected system-administrator role, `MAIN` branch, one creation audit, and no password field/value in that audit.
- Real login succeeded and opened the main dashboard; clean application shutdown succeeded.
- Installer prerequisite detector passed on the host's LocalDB 15 installation. The missing-prerequisite branch is covered deterministically by the installer contract test; a no-LocalDB VM was not available in this environment.

## Residual observations

- The existing NU1701 warnings for OpenTK/SkiaSharp WPF dependencies remain scheduled for Phase 3.
- One recoverable EF Core same-context concurrency error was logged during post-login dashboard loading. The dashboard remained usable and the session shut down cleanly, so it is not classified as a Phase 1 Critical blocker; it remains an explicit follow-up for the next gate.

