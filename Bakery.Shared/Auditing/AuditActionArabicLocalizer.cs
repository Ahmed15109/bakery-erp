namespace Bakery.Shared.Auditing;

/// <summary>
/// Presentation-only Arabic labels for persisted audit identifiers.
/// </summary>
public static class AuditActionArabicLocalizer
{
    private static readonly IReadOnlyDictionary<string, string> Labels =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [AuditActionKeys.Create] = "إضافة",
            [AuditActionKeys.Update] = "تعديل",
            [AuditActionKeys.Delete] = "حذف",
            [AuditActionKeys.Login] = "تسجيل الدخول",
            [AuditActionKeys.LoginFailed] = "فشل تسجيل الدخول",
            [AuditActionKeys.Logout] = "تسجيل الخروج",
            [AuditActionKeys.AuthorizationDenied] = "رفض صلاحية",
            [AuditActionKeys.FirstRunAdministratorCreated] = "إنشاء مسؤول النظام لأول مرة",
            [AuditActionKeys.UserCreated] = "إضافة مستخدم",
            [AuditActionKeys.UserUpdated] = "تعديل مستخدم",
            [AuditActionKeys.UserActiveStateChanged] = "تغيير حالة تفعيل المستخدم",
            [AuditActionKeys.UserPasswordReset] = "إعادة تعيين كلمة المرور",
            [AuditActionKeys.UserPasswordChanged] = "تغيير كلمة المرور",
            [AuditActionKeys.UserDeleted] = "حذف مستخدم",
            [AuditActionKeys.UserSafePermissionsUpdated] = "تعديل صلاحيات خزائن المستخدم",
            [AuditActionKeys.RoleCreated] = "إضافة دور",
            [AuditActionKeys.RoleUpdated] = "تعديل دور",
            [AuditActionKeys.RoleDeleted] = "حذف دور",
            [AuditActionKeys.BranchCreated] = "إضافة فرع",
            [AuditActionKeys.BranchUpdated] = "تعديل فرع",
            [AuditActionKeys.BranchActivated] = "تنشيط فرع",
            [AuditActionKeys.BranchDeactivated] = "إيقاف فرع",
            [AuditActionKeys.BranchDeleted] = "حذف فرع",
            [AuditActionKeys.WorkingDayOpened] = "فتح دورة العمل",
            [AuditActionKeys.WorkingDayAutoOpened] = "فتح دورة العمل تلقائياً",
            [AuditActionKeys.WorkingDayClosed] = "إغلاق دورة العمل",
            [AuditActionKeys.WorkingDayEmptySuccessorDiscarded] = "إلغاء يوم عمل فارغ لإعادة الفتح",
            [AuditActionKeys.WorkingDayReopened] = "إعادة فتح دورة العمل",
            [AuditActionKeys.SaleInvoicePosted] = "ترحيل فاتورة بيع",
            [AuditActionKeys.SaleInvoiceCancelled] = "إلغاء فاتورة بيع",
            [AuditActionKeys.PurchaseInvoicePosted] = "ترحيل فاتورة شراء",
            [AuditActionKeys.PurchaseInvoiceCancelled] = "إلغاء فاتورة شراء",
            [AuditActionKeys.PartyPaymentProcessed] = "تسجيل دفعة حساب",
            [AuditActionKeys.SaleInvoiceDraftDeleted] = "حذف مسودة فاتورة بيع",
            [AuditActionKeys.PurchaseInvoiceDraftDeleted] = "حذف مسودة فاتورة شراء",
            [AuditActionKeys.PartyPaymentReversed] = "عكس دفعة حساب",
            [AuditActionKeys.WorkingDayReopenBlockerResolved] = "معالجة مانع إعادة فتح يوم العمل",
            [AuditActionKeys.InventoryAdjusted] = "تسوية مخزنية",
            [AuditActionKeys.StockCountStarted] = "بدء جرد مخزني",
            [AuditActionKeys.StockCountCompleted] = "إتمام جرد مخزني",
            [AuditActionKeys.ProductionPosted] = "ترحيل أمر إنتاج",
            [AuditActionKeys.ProductionCancelled] = "إلغاء أمر إنتاج",
            [AuditActionKeys.ManualDeposit] = "إيداع يدوي",
            [AuditActionKeys.ManualWithdrawal] = "سحب يدوي",
            [AuditActionKeys.ManualTransactionReversed] = "عكس حركة يدوية",
            [AuditActionKeys.ReverseTransactionCreated] = "إنشاء حركة عكسية",
            [AuditActionKeys.FactoryReset] = "إعادة ضبط المصنع",
            [AuditActionKeys.BackupSettingsChanged] = "تغيير إعدادات النسخ الاحتياطي",
            [AuditActionKeys.BackupManualDeleted] = "حذف نسخة احتياطية يدوياً",
            [AuditActionKeys.BackupAutomaticDeleted] = "حذف نسخة احتياطية تلقائياً",
            [AuditActionKeys.BackupManualStarted] = "بدء نسخة احتياطية يدوية",
            [AuditActionKeys.BackupAutomaticStarted] = "بدء نسخة احتياطية تلقائية",
            [AuditActionKeys.BackupManualSucceeded] = "نجاح النسخة الاحتياطية اليدوية",
            [AuditActionKeys.BackupAutomaticSucceeded] = "نجاح النسخة الاحتياطية التلقائية",
            [AuditActionKeys.BackupFailed] = "فشل النسخة الاحتياطية",
            [AuditActionKeys.BackupRestoreAttempted] = "محاولة استعادة نسخة احتياطية",
            [AuditActionKeys.BackupRestoreSucceeded] = "نجاح استعادة النسخة الاحتياطية",
            [AuditActionKeys.BackupRestoreFailed] = "فشل استعادة النسخة الاحتياطية",
            [AuditActionKeys.BackupManualUploadRetried] = "إعادة محاولة رفع النسخة الاحتياطية",
            [AuditActionKeys.BackupUploadSucceeded] = "نجاح رفع النسخة الاحتياطية",
            [AuditActionKeys.BackupUploadFailed] = "فشل رفع النسخة الاحتياطية",
            [AuditActionKeys.GoogleDriveConnected] = "ربط جوجل درايف",
            [AuditActionKeys.GoogleDriveDisconnected] = "فصل جوجل درايف"
        };

    public static bool TryGet(string action, out string label) =>
        Labels.TryGetValue(action, out label!);
}
