# Bakery ERP User Guide

Bakery ERP is an Arabic, right-to-left Windows application for day-to-day bakery operations. Screens and actions are permission-aware, so the options available to each user depend on their assigned branch, roles, direct permissions, and safe access.

## First run and sign-in

On a new database, the first-run screen requires the owner to create the initial Super Administrator. Choose the username, full name, and a password of at least 12 characters; the application does not ship with a default production account.

At sign-in, select an assigned active branch when prompted. The application establishes an active branch and an accessible safe for the session. Contact an administrator if the required branch, module, or safe is not available rather than sharing another user’s account.

## Daily operating cycle

1. **Open the Working Day.** Use the Working Day action available from the dashboard or treasury workspace. Posting operational and financial transactions requires an open day for the active branch.
2. **Record business activity.** Create sales and purchase invoices, receive or issue stock, post production, record waste, manage party payments, and enter authorized treasury or employee transactions as they occur.
3. **Review before posting.** Draft invoices and production orders can be checked before they affect stock, ledgers, or safes. Posted records should be cancelled or reversed through the supported workflow rather than edited outside the application.
4. **Monitor the branch.** Use dashboards, ledgers, statements, inventory views, and reports to review balances and activity. A hidden safe balance or unavailable command normally indicates a permission restriction.
5. **Close the Working Day.** Review the calculated cash, choose the amount transferred to the main safe, and resolve any displayed blockers. An override is available only to users with the dedicated permission and requires a reason.
6. **Confirm the backup.** A successful close commits the Working Day first and then queues an automatic encrypted backup. Check the backup screen or Health Monitor before shutting down the computer.

## Sales, purchases, and party accounts

- Save an invoice as a draft while it is still being prepared; posting assigns its business-date number and creates the related stock, safe, and party-ledger movements in one transaction.
- Use cash, credit, or mixed settlement only when it matches the real transaction.
- Use customer/supplier statements and the party payment workflow for later collections or payments.
- Cancel posted invoices and reverse eligible payments through their application commands. Reversal records preserve the original audit trail.

## Inventory, production, and employees

- Maintain each item’s type, base unit, alternative units, conversion factors, barcode, cost, and reorder information before using it in transactions.
- Use stock adjustments and stock-count sessions for controlled corrections; do not compensate for count differences with fictitious purchases or sales.
- Define recipes before production. Posting a production order consumes raw materials, adds finished goods, and records eligible employee production wages together.
- Record waste against the correct Working Day and item so stock and cost reports remain consistent.
- Use employee transactions and settlements for advances, bonuses, deductions, wage payments, and statement reconciliation.

## Treasury and reporting

The treasury workspace operates on the selected safe. Safe permissions independently control access, balance visibility, ledger visibility, cash-in, cash-out, transfer source, and transfer destination. Verify the selected safe before recording a transaction or printing a treasury report.

The application supports permission-controlled PDF reports, UTF-8 CSV exports, A4 printing, and thermal invoice receipts. Review the Windows printer selection and paper size before printing production documents.

## Backup and restore

Open **Settings → Backup and Restore** to view backup history and status. Depending on permission, a user can create a manual backup, change the destination, restore, delete a local copy, or connect Google Drive.

- Local backups are encrypted `.berpbackup` files. The default folder is `%LOCALAPPDATA%\BakeryERP\Backups`, and selecting a different physical drive or external medium provides better protection from disk failure.
- The default retention target is the latest five successful local backups. Integrity and upload safeguards may preserve additional files when deleting an older copy would be unsafe.
- Restore accepts a backup from history, Google Drive, or a local `.berpbackup`/legacy `.zip` file. The application validates the archive, creates a safety snapshot, restores the data, and restarts after success.
- Device-protected backups depend on the current Windows user’s DPAPI-protected key. Preserve the Windows profile and `%LOCALAPPDATA%\BakeryERP\backup-encryption.key` as part of a machine-replacement plan.
- Google Drive is optional. It must be configured by the deployment owner and explicitly connected by an authorized user before cloud uploads can run.

Never interrupt a restore, manually edit a backup archive, or delete the last known-good copy. Keep at least one verified backup away from the database drive.

## Health and troubleshooting

The **Health Monitor** displays database status, the most recent backup, pending recoverable drafts, and available system-drive space. If an operation is unavailable:

1. confirm the active branch, selected safe, and Working Day status;
2. check that the current account has the required module and safe permissions;
3. refresh the affected screen and review the Arabic validation message;
4. check the Health Monitor and `%LOCALAPPDATA%\BakeryERP\Logs`;
5. create a backup before attempting restore, reset, or configuration changes.

For installation, LocalDB, configuration, and upgrade issues, see the [deployment guide](../developer/DEPLOYMENT_GUIDE.md).
