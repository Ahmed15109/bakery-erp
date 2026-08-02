# Bakery ERP — Deployment Guide

## System Requirements
- OS: Windows 10/11 (64-bit)
- Runtime: none for the supported self-contained installer
- Database: **Microsoft SQL Server Express LocalDB x64, version 2019 or newer**
- Supported instance for version 1.0: `(localdb)\MSSQLLocalDB`

The version 1.0 installer does not support a separately managed SQL Server Express named
instance as its default deployment target. A deployment that deliberately uses another
supported SQL Server connection must place its override in the per-user configuration file
described below and qualify that environment independently.

If LocalDB is missing, setup stops before copying the application and displays Arabic
installation instructions. Install LocalDB from the official
[Microsoft SQL Server 2022 Express download](https://www.microsoft.com/download/details.aspx?id=104781),
then run Bakery ERP setup again. In the SQL Server Express download workflow, select the
LocalDB package.

## Compilation & Publishing
To prepare the application for production deployment, build in Release mode:

```powershell
dotnet publish Bakery.WPF/Bakery.WPF.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true
```

## Portable Mode
1. Copy the output of the publish folder to a USB drive or target PC folder (e.g., `C:\BakeryERP`).
2. Confirm LocalDB is installed before launching the portable copy.
3. The application will create and migrate its database on first launch.

## SQL Server Configuration
The shipped version 1.0 configuration uses the supported LocalDB automatic instance:

```json
{
  "ConnectionStrings": {
    "BakeryDatabase": "Server=(localdb)\\MSSQLLocalDB;Database=BakeryERP;Trusted_Connection=True;MultipleActiveResultSets=true;TrustServerCertificate=True"
  }
}
```

## Customer Configuration and Upgrade Preservation

`appsettings.defaults.json` is immutable application-owned configuration installed beside the
executable. Do not edit it. Bakery ERP loads an optional customer override from:

`%LOCALAPPDATA%\BakeryERP\appsettings.user.json`

The per-user file is created on first launch and is never installed, overwritten, or deleted by
setup/uninstall. Environment variables and command-line values take precedence over both JSON
files. Configuration changes require an application restart.

On the first launch after upgrading an older installation, Bakery ERP copies the prior
executable-side `appsettings.json` to the per-user location only when no per-user file already
exists. Later upgrades never overwrite customer changes.

Google Drive OAuth deployment values are not shipped in source or installer defaults. Supply
both `GoogleDrive:ClientId` and `GoogleDrive:ClientSecret` through the per-user file or protected
deployment environment. OAuth access/refresh tokens are stored separately with Windows DPAPI;
never place tokens in either JSON file.

The application validates the effective database connection shape and paired Google OAuth
settings before starting services. Validation errors identify the setting but do not echo the
connection string or credential value into the log/UI.

## First Launch Experience
- The app will run migrations automatically.
- If no user exists, a mandatory Arabic first-run screen asks the bakery owner to choose the
  first administrator username, full name, and a password of at least 12 characters.
- No universal default production password is shipped.
- After the first administrator is created, the setup screen cannot create another one.
- The `Health Monitor` should show all systems as "Online".
