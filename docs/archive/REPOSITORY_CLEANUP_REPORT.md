# Repository Cleanup Report

Date: 2026-07-22
Scope: `BakeryERP.sln`, all seven solution projects, installer/publish inputs, test assets, and repository-root artifacts.

## Outcome

The cleanup removed generated clutter, abandoned QA harnesses, one empty placeholder test, confirmed unreachable code, and one redundant test-only package without changing production behavior. The final forced restore, Debug build, Release build, 228-test suite, exact self-contained publish, and Inno Setup compile all passed. An isolated current-user install of the exact rebuilt setup completed first-run provisioning, login, dashboard, treasury, reports, and backup-page startup successfully.

The repository initially contained an empty, invalid `.git` directory. Before deletion, it was initialized and checkpointed as commit `b458dc85468b2675b4c918dbed705ab23ccab92d` (`checkpoint: repository state before cleanup audit`).

## Audit Classification Summary

### CONFIRMED UNUSED

- `SalesPurchaseSummaryDto`.
- `RecipeDto`, `RecipeItemDto`, `ProductionOrderDto`, `ProductionConsumedItemDto`, and `ProductionProducedItemDto`.
- `WorkingDayStatusToBrushConverter` and `WorkingDayStatusToStringConverter`.
- Private test doubles `UnexpectedDialogService` and `UnexpectedUserManagementService`.
- Unhooked `EmployeeFormDialog.Cancel_Click` handler; the cancel button uses WPF `IsCancel="True"` and has no click binding.
- Empty `UnitTest1.Test1` placeholder.
- Root `test.cs` LocalDB diagnostic program.

For these candidates, exact identifier scans covered C#, XAML, project files, configuration, installer inputs, and tests. The audit also checked DI registrations, navigation/dialog mappings, resource dictionaries, EF model configuration, serializer calls, assembly scanning/reflection, publish/installer inputs, and test discovery. No dynamic/name-based activation or serialization contract referenced any removed type.

### DUPLICATE IMPLEMENTATION

- `test_icon.py` was an older icon generator with no references. `build_icon.py` is retained and its geometry markers match the checked-in SVG assets.
- The standalone `FreshDbVerifier` and DPI screenshot programs duplicated maintained integration/runtime coverage and existed only beneath the generated `artifacts` tree.

### GENERATED ARTIFACT

- All `bin`, `obj`, `publish`, `TestResults`, `.trx`, coverage, log, diagnostic database, screenshot QA, runtime-copy, and installer verification intermediates described below.
- `OutlinedComboBoxTemplate.xml` was an extracted template fragment, not an application resource dictionary.
- `OutlinedTextBoxStyle.xml` contained a captured `XamlParseException` stack trace rather than usable XAML.

### REQUIRED

- EF Core migrations, designer files, model snapshot, `BakeryDbContextFactory`, and `IEntityTypeConfiguration` classes. Configurations are loaded by `ApplyConfigurationsFromAssembly` and migrations/design factories are tooling/reflection entry points.
- FluentValidation validators and `ApplicationAssemblyMarker`. Validators are loaded by `AddValidatorsFromAssemblyContaining`.
- WPF views, ViewModels, converters, resource dictionaries, DI registrations, dialog mappings, and navigation targets referenced from XAML or runtime mappings.
- xUnit test classes and `LoginWpfCollection`, which are discovered by attributes/reflection rather than ordinary call sites.
- Private methods decorated with `[RelayCommand]`; their generated command properties are referenced by XAML.
- `BakeryERP_Setup_v1.0.exe`, the final release deliverable.

### POSSIBLY UNUSED — KEPT

- The obsolete `IWorkingDayService.AutoOpenIfNeededAsync` and `SimplifiedCloseAsync` compatibility APIs. They have no current UI call sites but are public compatibility surface and were explicitly retained.
- `Microsoft.EntityFrameworkCore.Design` in `Bakery.Infrastructure` and `Bakery.WPF`. These are design-time migration/tooling dependencies; only the demonstrably redundant copy in the test project was removed.
- Explicit OpenTK, GLWpfControl, SkiaSharp, and HarfBuzz references. They appear redundant in simple source scans but are runtime-loaded by the WPF chart/render path and are covered by `PresentationDependencyRuntimeTests`.
- `ReportAssemblyMarker`, icon source assets, and `build_icon.py`, which support assembly/resource discovery and reproducible branding.

## Removed Generated Files

The pre-cleanup generated-target inventory contained 7,032 files totaling 6,815,792,223 bytes (6,500.05 MiB). Verification regenerated build/publish output; a final purge removed those verification outputs again.

| Path/pattern | Evidence and disposition |
|---|---|
| `**/bin/` | Compiler, test-host, native runtime, and nested publish output removed across all projects and QA harnesses. |
| `**/obj/` | MSBuild/NuGet/XAML intermediates removed. The active IDE recreated about 23 KiB afterward; the paths are ignored. |
| `/publish/` and nested publish copies | Exact release publish was verified, consumed by Inno Setup, then removed. |
| `Bakery.IntegrationTests/TestResults/` | Removed 50 `.trx` reports (3.30 MiB). They were test-run output, not test inputs. |
| `/artifacts/` | Removed coverage XML, screenshots, copied runtime/publish trees, QA binaries, install logs, copied `.mdf`/`.ldf`/`.bak` databases, verification builds, and abandoned harness output (1,893.48 MiB after the first build-output purge). |
| `/Logs/`, `*.log` | Repository-local runtime and installer logs removed. |
| `test_output*.txt`, `run_output*.txt` | Old captured console/test reports removed. |
| `.vs/` | Most IDE state was removed. 11 actively locked Visual Studio index files (17.85 MiB) remain and are ignored. |
| Empty `.agents/` and `Bakery.Domain/Validators/` | Empty directories removed. |

`.gitignore` now excludes Visual Studio state, `bin`, `obj`, publish output, `artifacts`, `TestResults`, `.trx`, coverage, logs, temporary/backup files, copied local databases, machine-local appsettings files, and installer intermediates.

## Removed Source Files

| File | Reason and evidence |
|---|---|
| `Bakery.IntegrationTests/UnitTest1.cs` | Contained only an empty `[Fact] Test1()` with no assertions or behavior. Removing it changed discovery from 229 tests to 228 meaningful tests. |
| `test.cs` | Standalone hard-coded `BakeryERP` LocalDB diagnostic, outside every project/solution/installer input and absent from all references. |
| `test_icon.py` | Obsolete duplicate of the retained `build_icon.py`; no project, script, documentation, or installer reference. |
| `OutlinedComboBoxTemplate.xml` | Unreferenced root-level extraction fragment; not merged by `App.xaml` or included by the WPF project/installer. |
| `OutlinedTextBoxStyle.xml` | Captured exception text, not usable XML/XAML; unreferenced by project/resources/installer/tests. |
| `artifacts/FreshDbVerifier/FreshDbVerifier.csproj` | Ad hoc smoke harness absent from `BakeryERP.sln`, all `ProjectReference` entries, installer inputs, and maintained test discovery. |
| `artifacts/FreshDbVerifier/Program.cs` | Harness body duplicated maintained migration/login/treasury/invoice/backup/restore/runtime tests and was reachable only through the removed standalone project. |
| `artifacts/report-dpi-qa/ReportDpiQa.csproj` | Screenshot QA harness absent from the solution, installer, publish, and maintained test suite. |
| `artifacts/report-dpi-qa/Program.cs` | Generated DPI screenshots/reports only; reachable solely through the removed QA project. |
| `artifacts/build_brand_icon.ps1` | Unreferenced artifact-area icon script superseded by retained root branding source/generator. |
| `artifacts/make_ico.ps1` | Unreferenced artifact-area icon conversion script superseded by retained `build_icon.py`. |

Confirmed dead declarations were also removed from retained files:

- `Bakery.Application/DTOs/Accounting/AccountingDtos.cs`: `SalesPurchaseSummaryDto`.
- `Bakery.Application/DTOs/ProductionDtos.cs`: five mutually dependent legacy DTOs and the now-unused entity import.
- `Bakery.WPF/Converters/CommonConverters.cs`: two unregistered/unreferenced working-day converters.
- `Bakery.IntegrationTests/LoginViewModelTests.cs`: two unused private test doubles.
- `Bakery.WPF/Views/EmployeeFormDialog.xaml.cs`: unhooked `Cancel_Click`.

No entire production source file was deleted solely because of a text-search result.

## Removed Packages

| Package | Previous version | Project | Evidence |
|---|---:|---|---|
| `Microsoft.EntityFrameworkCore.Design` | 8.0.29 | `Bakery.IntegrationTests` | The only test-project occurrence was the `PackageReference`; no design-time factory or Design namespace was used. The package remains in the infrastructure/tooling and WPF startup projects. Forced restore, Release build, all tests, exact publish, and installed startup passed after removal. |

No `ProjectReference` was removed. Direct project dependencies are used by compile-time code, WPF composition, reporting, or integration tests.

## Kept Suspicious Files

- All 36 EF Core migrations plus their designer files and `BakeryDbContextModelSnapshot` were retained because database upgrade history depends on them.
- `BakeryDbContextFactory.cs` was retained for EF design-time discovery.
- Configuration classes with a single textual occurrence were retained because `BakeryDbContext` scans the assembly.
- Validator classes with few direct references were retained because FluentValidation scans their assembly.
- Combined ViewModel/source files were retained; only proven declarations within them were removed.
- Reflection-driven xUnit test/collection classes and source-generated MVVM command methods were retained.
- `BakeryERP_Setup_v1.0.exe` was retained and rebuilt from the verified cleaned publish.
- Historical implementation/audit documents were retained because no authoritative supersession map proved them safe to delete.

## Test Results

### Before cleanup

- `dotnet restore BakeryERP.sln --force --no-cache`: PASS.
- Debug build: PASS, 0 warnings, 0 errors.
- Release build: PASS, 0 warnings, 0 errors.
- Full suite: PASS, 229/229. One test was the empty placeholder removed by this audit.

### After cleanup

- Forced no-cache restore: PASS.
- Debug build: PASS, 0 warnings, 0 errors.
- Release build: PASS, 0 warnings, 0 errors.
- Full meaningful suite: PASS, 228/228, 0 failed, 0 skipped.
- Focused real LocalDB backup/restore workflows: PASS, 15/15.
- `dotnet format ... analyzers --diagnostics IDE0005 --verify-no-changes`: PASS; 0 unused-import changes across 566 files.
- NuGet vulnerability scan, including transitive packages: PASS; no known vulnerable packages were reported for any solution project from the configured sources.
- NuGet deprecation scan: `xunit` 2.9.3 is the only deprecated top-level package. Deprecated transitive identity/token, `System.Text.Json` 4.7.2, `System.Collections.Immutable` 6.0.0, and xUnit v2 packages remain pending coordinated upgrades; none was removed merely because it was transitively marked legacy.
- Test discovery: PASS after removing the placeholder.

### Publish, installer, and runtime

- Exact publish command: `dotnet publish Bakery.WPF\Bakery.WPF.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o publish --verbosity minimal` — PASS.
- Published payload: 39 files, 325.91 MiB; executable SHA-256 `18405EDD888C2A1209090784E7242959BCDDAA5EE61B73D2A1971CF26D9F927B`.
- Inno Setup 6.7.3 compile: PASS.
- Rebuilt setup: 67,128,915 bytes; SHA-256 `7715ACCC5BFA1B6050B4A09E3855BBE45BE2B83094E0D3B8E2632B1DF6387D32`.
- Isolated current-user install: PASS; installed executable hash exactly matched the published executable.
- Fresh isolated database: PASS; all 36 migrations applied and integrity check passed.
- First-run administrator creation: PASS.
- Real installed login: PASS.
- Dashboard, treasury, reports, and backup page: PASS through UI Automation; application remained responsive.
- Current installed smoke run logged 0 Error/Fatal events. Existing warnings about MARS savepoints and unordered `First` queries were observed but were not introduced by cleanup.
- Isolated install was uninstalled and its uniquely named LocalDB smoke database was dropped afterward.

The installed backup page's manual-backup button remained disabled after its asynchronous load because `BackupManagementViewModel` does not refresh `CreateManualCommand.CanExecute` when `IsBusy` returns to false. That code was pre-existing and outside this no-behavior-change cleanup. Backup creation, validation, safety backup, rollback, and restore were therefore verified through the maintained 15-test real LocalDB workflow rather than by clicking the disabled installed button.

## Repository Size

| Measure | Before | After cleanup |
|---|---:|---:|
| Working-tree files | 7,614 | 631 including this report (plus small IDE-regenerated ignored caches) |
| Working-tree size | 6,890,736,009 bytes (6,571.52 MiB) | approximately 93.8 MB (89.46 MiB) |
| Net working-tree space recovered | — | approximately 6,796.9 MB (6,482.06 MiB / 6.33 GiB, 98.64%) |

The new local Git checkpoint occupies approximately 77.30 MiB and is reported separately from the working-tree comparison. The final working-tree number includes the 64.02 MiB release installer and 17.85 MiB of IDE-locked `.vs` indexes.

## Remaining Cleanup Recommendations

1. Close Visual Studio/Copilot indexing, then delete the ignored `.vs/` folder and any tiny IDE-regenerated `bin/obj` directories. They could not be fully removed while open and locked.
2. Fix `BackupManagementViewModel` command invalidation in a separate behavior-change task, then repeat the installed manual backup/restore UI smoke.
3. Run the installer in its default administrator/machine-wide mode on clean Windows 10 and Windows 11 VMs. This session was non-administrative, so runtime verification used Inno's supported `/CURRENTUSER` override.
4. Plan a separate dependency-upgrade task for xUnit v2 and the deprecated transitive identity/token/runtime packages. These upgrades can affect test discovery and runtime bindings, so this cleanup deliberately did not combine them with deletion work.
5. Consolidate historical reports only after designating an authoritative document set; the audit did not delete potentially valuable delivery/security history.
