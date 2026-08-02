# Bakery ERP Deployment Guide

## System requirements

- Windows 10 or Windows 11, 64-bit.
- Microsoft SQL Server Express LocalDB x64, version 2019 or newer.
- The supported default instance: `(localdb)\MSSQLLocalDB`.
- Administrator access when installing the Inno Setup package.

The supported installer is a self-contained `win-x64` deployment, so it does not require a separately installed .NET runtime. LocalDB remains a prerequisite. If it is absent, setup stops before copying the application and offers the official [Microsoft SQL Server 2022 Express download](https://www.microsoft.com/download/details.aspx?id=104781); install the LocalDB component, then run setup again.

The default deployment target is LocalDB. A deployment that deliberately uses another SQL Server connection must supply it through the per-user configuration described below and qualify that environment independently.

## Publish and package

From the repository root:

```powershell
dotnet restore .\BakeryERP.sln
dotnet build .\BakeryERP.sln -c Release
dotnet publish .\Bakery.WPF\Bakery.WPF.csproj `
  -c Release `
  -r win-x64 `
  --self-contained true `
  -p:PublishSingleFile=true `
  -o .\publish
```

The Inno Setup script consumes `publish\` and produces the installer:

```powershell
iscc .\BakeryERP.iss
```

Inno Setup 6 is required only for the packaging step. A portable deployment may copy the contents of `publish\` to the target computer, but LocalDB must still be installed before first launch.

## Database configuration

The tracked defaults use Windows integrated authentication with the supported automatic LocalDB instance:

```json
{
  "ConnectionStrings": {
    "BakeryDatabase": "Server=(localdb)\\MSSQLLocalDB;Database=BakeryERP;Trusted_Connection=True;MultipleActiveResultSets=true;TrustServerCertificate=True"
  }
}
```

## Configuration and upgrade preservation

`appsettings.defaults.json` is application-owned configuration installed beside the executable. Do not edit it on a customer computer. Bakery ERP creates and loads the per-user override at:

`%LOCALAPPDATA%\BakeryERP\appsettings.user.json`

Configuration precedence is:

1. installed `appsettings.defaults.json`;
2. per-user `appsettings.user.json`;
3. environment variables;
4. command-line arguments.

Changes require an application restart. The installer does not install, overwrite, or remove the per-user file. On the first launch after upgrading an older installation, the application migrates an executable-side `appsettings.json` only when no per-user file exists.

Google Drive OAuth values are not shipped in source or installer defaults. Provide both `GoogleDrive:ClientId` and `GoogleDrive:ClientSecret` through the per-user file or a protected deployment environment, or leave both empty to disable cloud backup. OAuth access and refresh tokens are protected separately with Windows DPAPI; never place tokens in either JSON file.

The application validates the effective database connection and requires the two Google OAuth settings to be supplied together. Validation errors identify the setting without echoing secret values.

## Application data

Runtime data is stored under `%LOCALAPPDATA%\BakeryERP\`, independently of the installation directory:

| Path | Contents |
|---|---|
| `appsettings.user.json` | Per-user configuration overrides |
| `Backups\` | Default local backup destination; an authorized user can select another folder |
| `Logs\` | Structured application and startup logs |
| `Attachments\`, `Documents\`, `Templates\`, `Logos\` | User-managed application content included in backups |
| `RestoreWork\` | Temporary validated restore workspace, cleaned by the application |
| `BackupDownloads\` | Controlled download workspace for cloud backups |
| `backup-encryption.key` | Device-backup key protected for the current Windows user with DPAPI |

Uninstall removes application files but preserves user configuration and data. Back up the database, application content, and the Windows profile material needed for device-protected backups before replacing a computer or Windows account.

## First launch and upgrades

At startup, Bakery ERP validates configuration, creates the database when needed, takes a safety snapshot before migrating an existing database, applies pending EF Core migrations, seeds required reference data, and runs an integrity check. If no user exists, a mandatory Arabic setup screen asks the owner to create the first Super Administrator with a password of at least 12 characters. No universal production credential is shipped, and the setup flow cannot create a second initial administrator.

After an upgrade, confirm that the application starts, the expected branch can be selected, the Health Monitor can read database and backup status, and a manual backup completes before normal operation resumes.
