# Final Production-Ready Phase Implementation Plan

This plan outlines the steps required to transition the Bakery ERP system into a fully production-ready, crash-resilient, and user-friendly offline desktop application.

## User Review Required

> [!IMPORTANT]
> The requirements for this phase are extensive, involving database backups, printing systems, advanced reporting, and deployment readiness. Please review the proposed solutions carefully, particularly the choices regarding reporting libraries and database backup mechanisms.

## Open Questions

> [!WARNING]
> 1. **Printing & Reporting Engine**: You mentioned "FastReport or RDLC". Since this is a WPF .NET 8 application, integrating standard RDLC can be tricky as it relies on legacy Windows Forms components or third-party wrappers, and FastReport requires additional NuGet packages. Would you prefer using WPF's native `FlowDocument` for printing (which requires zero external dependencies and supports Thermal & A4 easily) or should I proceed with pulling in an external library like `FastReport.OpenSource`?
> 2. **Backup System Location**: Should backup files be saved in the application's local AppData folder by default, or should the user always be prompted for a directory?
> 3. **Charts Library**: To add charts to the Dashboard, `LiveChartsCore.SkiaSharpView.WPF` is the modern standard. Do you approve adding this NuGet package to the WPF project?

## Proposed Changes

### 1. Backup & Restore System
- **[NEW] `Bakery.Application/Interfaces/IBackupService.cs`**: Define methods for `CreateBackupAsync`, `RestoreBackupAsync`, and `GetBackupHistoryAsync`.
- **[NEW] `Bakery.Infrastructure/Services/BackupService.cs`**: Implement raw SQL commands (`BACKUP DATABASE` and `RESTORE DATABASE`). Note: Restoring an active database requires setting it to `SINGLE_USER` mode, executing the restore, and restarting the application.
- **[NEW] `Bakery.WPF/ViewModels/BackupViewModel.cs` & `BackupView.xaml`**: UI to trigger manual backups, view history, and restore.

### 2. Crash Recovery & Stability
- **[MODIFY] `Bakery.WPF/App.xaml.cs`**: Enhance `DispatcherUnhandledException` and `AppDomain.UnhandledException` to log fatal errors, attempt a graceful emergency save of the current state, and show a user-friendly recovery message.
- **[NEW] `Bakery.Application/Interfaces/IRecoveryService.cs`**: Service to serialize and save active draft invoices to a local JSON file every minute, recovering them upon startup if the app closed unexpectedly.

### 3. Printing & Advanced Reporting
- **[NEW] `Bakery.Reporting` Layer Enhancements**: Create strongly-typed DTOs for the required reports (Daily Sales, Production Efficiency, Waste, Employee Wages).
- **[NEW] `Bakery.WPF/Services/PrintService.cs`**: A dedicated service handling `PrintDialog`, Thermal vs. A4 templates, and silent printing.
- **[NEW] `Bakery.WPF/Views/PrintTemplates/`**: XAML-based `FlowDocument` templates for receipts and invoices.

### 4. Settings System
- **[NEW] `Bakery.Application/Interfaces/ISettingsService.cs`**: Wrapper around the existing `AppSetting` database entity to easily get/set strongly-typed settings (e.g., Theme, PrinterName, BackupPath).
- **[NEW] `Bakery.WPF/ViewModels/SettingsViewModel.cs` & `SettingsView.xaml`**: User interface to manage system configurations.

### 5. Performance Optimization
- **[MODIFY] `Bakery.Infrastructure/Repositories/BaseRepository.cs`**: Introduce `ListAsNoTrackingAsync` for read-only queries (like reporting and dashboard data) to reduce EF Core tracking overhead.
- **[MODIFY] `Bakery.Infrastructure/Configurations/`**: Add SQL indexes to frequently filtered columns (e.g., `EntryDate`, `WorkingDayId`, `Status`).

### 6. Dashboard & UX Enhancements
- **[MODIFY] `DashboardViewModel.cs` & `DashboardView.xaml`**: Add chart bindings, recent activity feeds, and quick action buttons.
- **[NEW] Navigation Shortcuts**: Add `InputBindings` to `MainWindow.xaml` for common tasks (e.g., F2 for New Sale, F3 for Production).

### 7. Deployment Readiness
- **[NEW] `Bakery.WPF/FINAL_SETUP.md`**, **`DEPLOYMENT_GUIDE.md`**, and **`USER_GUIDE.md`**: Comprehensive documentation for end-users and IT administrators.
- **[MODIFY] `DatabaseInitializer.cs`**: Ensure automatic migrations execute cleanly on first startup in a production environment.

## Verification Plan

- **Automated Checks**: Build the entire solution in `Release` configuration. Verify no warnings or DI errors.
- **Manual Verification**:
  1. Trigger a manual backup and verify the `.bak` file is created.
  2. Change an app setting, restart the app, and verify it persists.
  3. Load the dashboard to confirm the queries execute rapidly and charts render correctly.
  4. Generate a test print document and preview the output.
