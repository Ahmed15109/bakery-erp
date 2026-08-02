# Bakery ERP Documentation

The [root README](../README.md) provides the portfolio overview, verified feature set, architecture diagram, prerequisites, and source-build instructions. The documents below cover the operational and maintenance detail that benefits from a dedicated guide.

## User documentation

| Document | Purpose |
|---|---|
| [User guide](user/USER_GUIDE.md) | First run, daily operation, Working Day lifecycle, printing, backups, restore, and basic troubleshooting |

## Developer documentation

| Document | Purpose |
|---|---|
| [Deployment guide](developer/DEPLOYMENT_GUIDE.md) | Windows deployment, LocalDB prerequisite, publishing, configuration precedence, upgrades, and application-data paths |
| [Security and permissions](developer/SECURITY_AND_PERMISSIONS.md) | Authentication, roles, branch and safe scope, enforcement boundaries, session invalidation, and audit behavior |
| [Backup encryption format](developer/BACKUP_ENCRYPTION_FORMAT.md) | Versioned `.berpbackup` envelope, cryptographic construction, key modes, and compatibility policy |
| [Dependency decision](developer/DEPENDENCY_DECISION.md) | Maintained rationale for the LiveCharts, SkiaSharp, OpenTK, and .NET SDK choices |

These files describe the current repository. Superseded plans, phase reports, handoffs, and verification logs remain available through Git history instead of the public documentation tree.
