namespace Bakery.Shared.Auditing;

/// <summary>
/// Immutable identifiers persisted in AuditLogs.Action. These values are not UI
/// labels; presentation layers localize them separately.
/// </summary>
public static class AuditActionKeys
{
    public const string Create = "Create";
    public const string Update = "Update";
    public const string Delete = "Delete";

    public const string Login = "Login";
    public const string LoginFailed = "Failed login";
    public const string Logout = "Logout";
    public const string AuthorizationDenied = "AuthorizationDenied";

    public const string FirstRunAdministratorCreated = "FirstRunAdministratorCreated";
    public const string UserCreated = "UserCreated";
    public const string UserUpdated = "UserUpdated";
    public const string UserActiveStateChanged = "UserActiveStateChanged";
    public const string UserPasswordReset = "UserPasswordReset";
    public const string UserPasswordChanged = "UserPasswordChanged";
    public const string UserDeleted = "UserDeleted";
    public const string UserSafePermissionsUpdated = "UserSafePermissionsUpdated";
    public const string RoleCreated = "RoleCreated";
    public const string RoleUpdated = "RoleUpdated";
    public const string RoleDeleted = "RoleDeleted";

    public const string BranchCreated = "CreateBranch";
    public const string BranchUpdated = "UpdateBranch";
    public const string BranchActivated = "ActivateBranch";
    public const string BranchDeactivated = "DeactivateBranch";
    public const string BranchDeleted = "DeleteBranch";

    public const string WorkingDayOpened = "OpenDay";
    public const string WorkingDayAutoOpened = "AutoOpenDay";
    public const string WorkingDayClosed = "CloseDay";
    public const string WorkingDayEmptySuccessorDiscarded = "DiscardEmptySuccessorForReopen";
    public const string WorkingDayReopened = "ReopenDay";

    public const string SaleInvoicePosted = "Post sale invoice";
    public const string SaleInvoiceCancelled = "Cancel sale invoice";
    public const string PurchaseInvoicePosted = "Post purchase invoice";
    public const string PurchaseInvoiceCancelled = "Cancel purchase invoice";
    public const string PartyPaymentProcessed = "Process party payment";
    public const string SaleInvoiceDraftDeleted = "DeleteSaleInvoiceDraft";
    public const string PurchaseInvoiceDraftDeleted = "DeletePurchaseInvoiceDraft";
    public const string PartyPaymentReversed = "ReversePartyPayment";
    public const string WorkingDayReopenBlockerResolved = "ResolveWorkingDayReopenBlocker";

    public const string InventoryAdjusted = "Inventory adjustment";
    public const string StockCountStarted = "Start stock count";
    public const string StockCountCompleted = "Complete stock count";
    public const string WasteCreated = "Create";
    public const string ProductionPosted = "PostProduction";
    public const string ProductionCancelled = "CancelProduction";

    public const string ManualDeposit = "ManualDeposit";
    public const string ManualWithdrawal = "ManualWithdrawal";
    public const string ManualTransactionReversed = "ReverseManualTransaction";
    public const string ReverseTransactionCreated = "CreateReverseTransaction";
    public const string FactoryReset = "FactoryReset";

    public const string BackupSettingsChanged = "BackupSettingsChanged";
    public const string BackupManualDeleted = "BackupManualDeletion";
    public const string BackupAutomaticDeleted = "BackupAutomaticDeletion";
    public const string BackupManualStarted = "BackupManualCreation";
    public const string BackupAutomaticStarted = "BackupAutomaticCreation";
    public const string BackupManualSucceeded = "BackupManualSuccess";
    public const string BackupAutomaticSucceeded = "BackupAutomaticSuccess";
    public const string BackupFailed = "BackupFailure";
    public const string BackupRestoreAttempted = "BackupRestoreAttempt";
    public const string BackupRestoreSucceeded = "BackupRestoreSuccess";
    public const string BackupRestoreFailed = "BackupRestoreFailure";
    public const string BackupManualUploadRetried = "BackupManualUploadRetry";
    public const string BackupUploadSucceeded = "BackupUploadSuccess";
    public const string BackupUploadFailed = "BackupUploadFailure";
    public const string GoogleDriveConnected = "GoogleDriveConnected";
    public const string GoogleDriveDisconnected = "GoogleDriveDisconnected";

    public static IReadOnlySet<string> Known { get; } = new HashSet<string>(StringComparer.Ordinal)
    {
        Create, Update, Delete,
        Login, LoginFailed, Logout, AuthorizationDenied,
        FirstRunAdministratorCreated, UserCreated, UserUpdated, UserActiveStateChanged,
        UserPasswordReset, UserPasswordChanged, UserDeleted, UserSafePermissionsUpdated,
        RoleCreated, RoleUpdated, RoleDeleted,
        BranchCreated, BranchUpdated, BranchActivated, BranchDeactivated, BranchDeleted,
        WorkingDayOpened, WorkingDayAutoOpened, WorkingDayClosed,
        WorkingDayEmptySuccessorDiscarded, WorkingDayReopened,
        SaleInvoicePosted, SaleInvoiceCancelled, PurchaseInvoicePosted,
        PurchaseInvoiceCancelled, PartyPaymentProcessed, SaleInvoiceDraftDeleted,
        PurchaseInvoiceDraftDeleted, PartyPaymentReversed, WorkingDayReopenBlockerResolved,
        InventoryAdjusted, StockCountStarted, StockCountCompleted,
        ProductionPosted, ProductionCancelled,
        ManualDeposit, ManualWithdrawal, ManualTransactionReversed, ReverseTransactionCreated,
        FactoryReset, BackupSettingsChanged, BackupManualDeleted, BackupAutomaticDeleted,
        BackupManualStarted, BackupAutomaticStarted, BackupManualSucceeded,
        BackupAutomaticSucceeded, BackupFailed, BackupRestoreAttempted,
        BackupRestoreSucceeded, BackupRestoreFailed, BackupManualUploadRetried,
        BackupUploadSucceeded, BackupUploadFailed, GoogleDriveConnected, GoogleDriveDisconnected
    };

    public static bool IsKnown(string? action) => action is not null && Known.Contains(action);
}
