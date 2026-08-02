using Bakery.Shared.Auditing;

namespace Bakery.Shared.Helpers;

/// <summary>
/// Arabic-first localization strings for the Bakery ERP application.
/// All visible UI text is defined here to keep XAML clean and
/// facilitate future multi-language support via resource files.
/// </summary>
public static class Loc
{
    // ── App-level ────────────────────────────────────────────────────────────
    public static string AppTitle          => "نظام المخبز";
    public static string OfflineBakeryOps  => "عمليات المخبز بدون إنترنت";
    public static string SidebarSubtitle   => "إدارة ذكية للمبيعات والمخزون";
    public static string AppVersionInfo     => "نظام إدارة المخبز المتكامل - النسخة المميزة";

    // ── Navigation ───────────────────────────────────────────────────────────
    public static string Dashboard         => "لوحة التحكم";
    public static string WorkingDay        => "يوم العمل";
    public static string Sales             => "المبيعات";
    public static string Purchases         => "المشتريات";
    public static string Invoices          => "الفواتير";
    public static string Production        => "الإنتاج";
    public static string Inventory         => "المخزون";
    public static string Waste             => "الهالك";
    public static string Employees         => "الموظفين";
    public static string Accounts          => "الحسابات";
    public static string Safes             => "الخزن";
    public static string Reports           => "التقارير";
    public static string Settings          => "الإعدادات";
    public static string Logout            => "تسجيل الخروج";

    // ── Login ────────────────────────────────────────────────────────────────
    public static string Login             => "تسجيل الدخول";
    public static string SignInToContinue  => "سجل دخولك لمتابعة إدارة العمليات";
    public static string HintUsername      => "اسم المستخدم";
    public static string HintPassword      => "كلمة المرور";

    // ── Branch Selection ─────────────────────────────────────────────────────
    public static string SelectBranch       => "اختر الفرع";
    public static string BranchLabel        => "الفرع";
    public static string BranchName         => "اسم الفرع";
    public static string NoBranchesAssigned => "لم يتم تعيين فروع لهذا المستخدم. تواصل مع المسؤول.";
    public static string ErrNoBranchSelected => "يرجى اختيار الفرع.";
    public static string CurrentBranch      => "الفرع الحالي";
    public static string SwitchBranch       => "تبديل الفرع";
    public static string ConfirmBranchSwitch => "تأكيد";
 
    // ── Branches Module ──────────────────────────────────────────────────────
    public static string BranchesModule       => "إدارة الفروع";
    public static string AddBranch            => "إضافة فرع";
    public static string EditBranch           => "تعديل فرع";
    public static string CodeLabel            => "الكود";
    public static string NameLabel            => "الاسم";
    public static string ActiveLabel          => "نشط";
    public static string NotesLabel           => "ملاحظات";
    public static string ConfirmDeleteBranch  => "هل أنت متأكد من حذف الفرع '{0}'؟";
    public static string UserBranchesLabel    => "الفروع المتاحة";
    public static string ErrBranchCodeExists  => "كود الفرع موجود بالفعل.";
    public static string ErrBranchNameExists  => "اسم الفرع موجود بالفعل.";
    public static string ErrBranchNotFound    => "الفرع غير موجود.";
    public static string SelectBranches       => "تحديد الفروع";

    // ── Dashboard ────────────────────────────────────────────────────────────
    public static string QuickActions      => "إجراءات سريعة";
    public static string NewSale           => "فاتورة مبيعات جديدة";
    public static string NewPurchase       => "فاتورة مشتريات جديدة";
    public static string OpenDay           => "فتح اليوم";
    public static string CloseDay          => "إغلاق اليوم";
    public static string NewProduction     => "دفعة إنتاج جديدة";
    public static string DailySales        => "مبيعات اليوم";
    public static string ProductionVolume  => "حجم الإنتاج";
    public static string Efficiency        => "الكفاءة";
    public static string WasteCost         => "تكلفة الهالك";
    public static string LowStockAlerts    => "تنبيهات المخزون";
    public static string SafeBalance       => "رصيد الخزنة";
    public static string TodaysSales       => "مبيعات اليوم";
    public static string TodaysProduction  => "إنتاج اليوم";
    public static string WasteCostCard     => "تكلفة الهالك";
    public static string EmployeeWagesCard => "رواتب الموظفين";
    public static string SalesChart        => "منحنى المبيعات";
    public static string ProductionChart   => "منحنى الإنتاج";
    public static string ActiveBusinessDay => "يوم العمل النشط";
    public static string TreasuryBalance   => "رصيد الخزائن";
    public static string TodaysIndicators  => "مؤشرات اليوم";
    public static string PerformanceTrends => "اتجاهات الأداء";
    public static string InventoryAdjustments => "تسويات المخزون";
    public static string TreasuryAction    => "الخزينة";
    public static string EndDay            => "إنهاء اليوم";
    public static string SalesValue        => "قيمة المبيعات";
    public static string ProductionQuantity => "كمية الإنتاج";

    // ── Working Day ──────────────────────────────────────────────────────────
    public static string HintBusinessDate  => "تاريخ العمل";
    public static string HintOpeningCash   => "رصيد الفتح";
    public static string HintNotes         => "ملاحظات";
    public static string OpeningCash       => "رصيد الفتح";
    public static string ExpectedCash      => "النقد المتوقع";
    public static string ExpensesAndWages  => "المصاريف والرواتب";
    public static string NoOpenWorkingDay  => "لا يوجد يوم عمل مفتوح حالياً.";

    // ── Inventory ────────────────────────────────────────────────────────────
    public static string Items             => "الأصناف";
    public static string Units             => "الوحدات";
    public static string StockCount        => "جرد المخزون";
    public static string Movements         => "حركات المخزون";
    public static string Adjustment        => "تسوية";
    public static string Refresh           => "تحديث";
    public static string NewItem           => "صنف جديد";
    public static string Start             => "بدء";
    public static string Complete          => "إتمام";
    public static string Filter            => "بحث";
    public static string BreadLoaf         => "رغيف خبز";
    public static string Flour             => "دقيق";
    public static string GasCylinder       => "أسطوانة غاز";
    public static string Salt              => "ملح";
    public static string Yeast             => "خميرة";

    // ── System-generated inventory movement notes ───────────────────────────
    // Backend services use these factories for new rows and the normalizer for
    // legacy rows that were persisted with older English templates.
    public static string InventoryNoteProducedFromProduction(string? productionNumber) =>
        AppendInventoryReference("تم الإنتاج من أمر إنتاج", productionNumber);

    public static string InventoryNoteConsumedForProduction(string? productionNumber) =>
        AppendInventoryReference("تم استهلاك الصنف للإنتاج", productionNumber);

    public static string InventoryNoteStockCountVariance(int sessionId) =>
        $"تسوية جرد رقم {sessionId}";

    public static string InventoryNoteReversal(string? originalNote)
    {
        var localizedOriginal = LocalizeInventoryMovementNote(originalNote);
        return string.IsNullOrWhiteSpace(localizedOriginal)
            ? "حركة عكسية"
            : $"حركة عكسية: {localizedOriginal}";
    }

    public static string? LocalizeInventoryMovementNote(string? note)
    {
        if (string.IsNullOrWhiteSpace(note)) return note;

        var value = note.Trim();

        const string reversalPrefix = "Reversal of";
        if (value.StartsWith(reversalPrefix, StringComparison.OrdinalIgnoreCase))
            return InventoryNoteReversal(value[reversalPrefix.Length..].Trim());

        if (TryLocalizeInventoryPrefix(value, "Produced from Production", "تم الإنتاج من أمر إنتاج", out var localized))
            return localized;

        if (TryLocalizeInventoryPrefix(value, "Consumed for Production", "تم استهلاك الصنف للإنتاج", out localized))
            return localized;

        const string stockCountPrefix = "Stock count variance session";
        if (value.StartsWith(stockCountPrefix, StringComparison.OrdinalIgnoreCase))
        {
            var sessionReference = value[stockCountPrefix.Length..].Trim();
            if (sessionReference.StartsWith('#')) sessionReference = sessionReference[1..].TrimStart();
            return string.IsNullOrEmpty(sessionReference)
                ? "تسوية جرد"
                : $"تسوية جرد رقم {sessionReference}";
        }

        if (value.Equals("Purchases", StringComparison.OrdinalIgnoreCase)) return "مشتريات";
        if (value.Equals("Sales", StringComparison.OrdinalIgnoreCase)) return "مبيعات";

        // User-authored notes and already-localized values remain verbatim.
        return note;
    }

    private static string AppendInventoryReference(string message, string? reference) =>
        string.IsNullOrWhiteSpace(reference) ? message : $"{message} {reference.Trim()}";

    private static bool TryLocalizeInventoryPrefix(string value, string englishPrefix, string arabicPrefix, out string localized)
    {
        if (!value.StartsWith(englishPrefix, StringComparison.OrdinalIgnoreCase))
        {
            localized = string.Empty;
            return false;
        }

        var reference = value[englishPrefix.Length..].Trim();
        localized = AppendInventoryReference(arabicPrefix, reference);
        return true;
    }

    // ── Parties ──────────────────────────────────────────────────────────────
    public static string Parties           => "الأطراف";
    public static string SaveParty         => "حفظ الطرف";
    public static string Statement         => "كشف حساب";
    public static string StatementTitle    => "كشف الحساب";

    // ── Units ─────────────────────────────────────────────────────────────────
    public static string SaveUnit          => "حفظ الوحدة";

    // ── Common Actions ───────────────────────────────────────────────────────
    public static string Save              => "حفظ";
    public static string Cancel            => "إلغاء";
    public static string Edit              => "تعديل";
    public static string Delete            => "حذف";
    public static string ConfirmDelete     => "هل أنت متأكد من الحذف؟";
    public static string Active            => "نشط";
    public static string AddLine           => "إضافة سطر";
    public static string SaveAndPost       => "حفظ وترحيل";
    public static string QuickBread        => "خبز سريع";

    // ── Close Day Dialog ─────────────────────────────────────────────────────
    public static string CloseWorkingDay       => "إغلاق يوم العمل";
    public static string HintActualCash        => "النقد الفعلي المحسوب";
    public static string HintClosingNotes      => "ملاحظات الإغلاق";
    public static string HintAdminOverride     => "تجاوز المدير";
    public static string HintOverrideReason    => "سبب التجاوز";

    // ── Invoice Dialogs ──────────────────────────────────────────────────────
    public static string SaleInvoiceTitle      => "فاتورة مبيعات";
    public static string PurchaseInvoiceTitle  => "فاتورة مشتريات";
    public static string InvoicesTitle         => "إدارة الفواتير";
    public static string ItemFormTitle         => "بيانات الصنف";
    public static string AdjustmentTitle       => "تسوية مخزون";
    public static string HintCustomer          => "العميل";
    public static string HintSupplier          => "المورد";
    public static string HintItemBarcode       => "صنف / باركود";
    public static string HintQty               => "الكمية";
    public static string HintPaid              => "المدفوع";
    public static string HintItem              => "الصنف";
    public static string HintType              => "النوع";
    public static string HintName              => "الاسم";
    public static string HintPhone             => "الهاتف";
    public static string HintAddress           => "العنوان";
    public static string HintNationalId        => "الرقم الوطني";
    public static string HintSymbol            => "الرمز";
    public static string HintCode              => "الكود";
    public static string HintBarcode           => "الباركود";
    public static string HintDefaultUnit       => "الوحدة الافتراضية";
    public static string HintPurchasePrice     => "سعر الشراء";
    public static string HintSalePrice         => "سعر البيع";
    public static string HintMinStock          => "الحد الأدنى للمخزون";
    public static string HintReorderLevel      => "مستوى إعادة الطلب";
    public static string HintQuantity          => "الكمية";
    public static string HintIncreaseStock     => "زيادة المخزون";
    public static string HintReason            => "السبب";
    public static string HintUnit              => "الوحدة";
    public static string HintSearch            => "بحث بالكود أو الاسم أو الباركود";
    public static string HintSelectParty       => "اختر الطرف...";

    // ── DataGrid Column Headers ───────────────────────────────────────────────
    public static string ColNo             => "رقم";
    public static string ColDate           => "التاريخ";
    public static string ColCustomer       => "العميل";
    public static string ColSupplier       => "المورد";
    public static string ColStatus         => "الحالة";
    public static string ColTotal          => "الإجمالي";
    public static string ColPaid           => "المدفوع";
    public static string ColRemaining      => "المتبقي";
    public static string ColCode           => "الكود";
    public static string ColName           => "الاسم";
    public static string ColBarcode        => "الباركود";
    public static string ColPhone          => "الهاتف";
    public static string ColType           => "النوع";
    public static string ColUnit           => "الوحدة";
    public static string ColStock          => "المخزون";
    public static string ColMin            => "الحد الأدنى";
    public static string ColUnitCost       => "سعر الوحدة";
    public static string ColValue          => "القيمة";
    public static string ColQty            => "الكمية";
    public static string ColBalance        => "الرصيد";
    public static string ColDescription    => "البيان";
    public static string ColRef            => "المرجع";
    public static string ColSystem         => "نظامي";
    public static string ColPhysical       => "فعلي";
    public static string ColVariance       => "الفرق";
    public static string ColItemCode       => "كود الصنف";
    public static string ColItem           => "الصنف";
    public static string ColMovType        => "نوع الحركة";
    public static string ColRunBalance     => "الرصيد الجاري";
    public static string ColNotes          => "ملاحظات";
    public static string ColPrice          => "السعر";
    public static string ColSymbol         => "الرمز";
    public static string ColActions        => "الإجراءات";
    public static string ColOut            => "نفد";
    public static string ColLow            => "منخفض";
    public static string ColActive         => "نشط";

    // ── Settings ─────────────────────────────────────────────────────────────
    public static string SystemSettings    => "إعدادات النظام";
    public static string EnableDarkMode    => "تفعيل الوضع المظلم";
    public static string EnableArabicRtl   => "تفعيل اللغة العربية (يمين لليسار)";
    public static string SaveSettings      => "حفظ الإعدادات";

    // ── Health Monitor ────────────────────────────────────────────────────────
    public static string OfflineHealthMonitor  => "مراقبة النظام الداخلية";
    public static string DatabaseStatus        => "حالة قاعدة البيانات";
    public static string LastBackup            => "آخر نسخ احتياطي";
    public static string PendingRecoveries     => "استردادات معلقة";
    public static string DiskSpace             => "مساحة القرص";
    public static string RefreshStatus         => "تحديث الحالة";
    public static string Online                => "متصل";
    public static string Unknown               => "غير معروف";
    public static string NoBackupsFound        => "لم يتم العثور على نسخ احتياطية";
    public static string NotSignedIn           => "لم يتم تسجيل الدخول";

    // ── Placeholder Views ─────────────────────────────────────────────────────
    public static string ProductionView    => "الإنتاج";
    public static string WasteView         => "الهالك";
    public static string RecipesView       => "الوصفات";
    public static string EmployeesView     => "الموظفين";
    public static string EmployeeWages     => "رواتب الموظفين";
    public static string Customer          => "عميل";
    public static string Supplier          => "مورد";
    public static string Employee          => "موظف";
    public static string Mixed             => "متنوع";

    // ── Print ─────────────────────────────────────────────────────────────────
    public static string ReceiptHeader     => "إيصال - نظام مخبز";
    public static string ReportHeader      => "تقرير - نظام مخبز";
    public static string ReceiptFooter     => "شكراً لتعاملكم معنا!";
    public static string DateLabel         => "التاريخ:";
    public static string Separator         => "────────────────────────";

    // ── Service Messages ─────────────────────────────────────────────────────
    public static string NoPermission          => "غير مصرح";
    public static string ErrAdminRequired      => "مطلوب صلاحية مدير النظام.";
    public static string ErrNoOpenDay          => "لا يوجد يوم عمل مفتوح حالياً.";
    public static string ErrNotEnoughStock     => "الرصيد غير كافٍ.";
    public static string ErrStockCountClosed   => "عملية الجرد مغلقة بالفعل.";
    public static string ErrInvalidCredentials => "خطأ في اسم المستخدم أو كلمة المرور.";
    public static string ErrOnlyOneOpenDay     => "يمكن فتح يوم عمل واحد فقط في كل مرة.";
    public static string ErrDayAlreadyClosed   => "هذا اليوم مغلق بالفعل.";
    public static string ErrDraftsExist        => "توجد مسودات فواتير. مطلوب تجاوز المدير لإغلاق اليوم.";
    public static string PrintPreview          => "معاينة الطباعة";

    // ── More Service Messages ────────────────────────────────────────────────
    public static string ErrPaidExceedsTotal   => "المبلغ المدفوع لا يمكن أن يتجاوز الإجمالي.";
    public static string ErrOnlyDraftsEditable => "يمكن تعديل الفواتير المسودة فقط.";
    public static string ErrInvoiceEmpty       => "لا يمكن أن تكون الفاتورة فارغة.";
    public static string ErrCancelReasonReq    => "سبب الإلغاء مطلوب.";
    public static string ErrItemCodeExists     => "كود الصنف موجود بالفعل.";
    public static string ErrUnitSymbolExists   => "رمز الوحدة موجود بالفعل.";
    public static string ErrQtyPositive        => "الكمية يجب أن تكون أكبر من الصفر.";
    public static string ErrCannotDeleteItem   => "لا يمكن حذف الأصناف التي لها حركات.";
    public static string ErrSaveFailed         => "فشل حفظ البيانات.";
    public static string ErrPostFailed         => "فشل ترحيل البيانات.";
    public static string ErrSelectParty        => "يرجى اختيار العميل أو المورد.";
    public static string ErrEmptyInvoice       => "الفاتورة فارغة. يرجى إضافة أصناف.";
    public static string MsgInvoiceSaved       => "تم حفظ وترحيل الفاتورة بنجاح.";

    // ── Descriptions ─────────────────────────────────────────────────────────
    public static string DescCreditSale        => "بيع آجل";
    public static string DescSaleCash          => "نقد مبيعات";
    public static string DescCancelSale        => "إلغاء بيع";
    public static string DescCancelSaleCash    => "إلغاء نقد مبيعات";
    public static string DescCreditPurchase    => "شراء آجل";
    public static string DescPurchaseCash      => "نقد مشتريات";
    public static string DescCancelPurchase    => "إلغاء شراء";
    public static string DescCancelPurchaseCash => "إلغاء نقد مشتريات";
    public static string OpenDaySince          => "مفتوح منذ";
    public static string ExpectedCashLabel     => "النقد المتوقع:";
    public static string AdminUser             => "المدير";
    public static string Today                 => "اليوم";
    public static string Date                  => "التاريخ";
    public static string Value                 => "القيمة";

    // ── Enum Mappings ────────────────────────────────────────────────────────
    public static string StatusDraft           => "مسودة";
    public static string StatusPosted          => "مرحل";
    public static string StatusCancelled       => "ملغي";
    
    public static string TypeCustomer          => "عميل";
    public static string TypeSupplier          => "مورد";
    public static string TypeEmployee          => "موظف";
    public static string TypeMixed             => "عميل ومورد";
    public static string TypeBoth              => "كلاهما";

    public static string PaymentCash           => "نقدي";
    public static string PaymentCredit         => "آجل";
    public static string PaymentMixed          => "نقدي وآجل";

    public static string DayOpen               => "مفتوح";
    public static string DayClosed             => "مغلق";

    public static string ItemRaw               => "مادة خام";
    public static string ItemFinished          => "منتج نهائي";
    public static string ItemFuel              => "وقود";
    public static string ItemService           => "خدمة";
    public static string ItemPackaging         => "تغليف";

    // ── Movement Types ───────────────────────────────────────────────────────
    public static string MovOpening            => "رصيد افتتاحي";
    public static string MovPurchase           => "مشتريات";
    public static string MovSale               => "مبيعات";
    public static string MovConsume            => "استهلاك إنتاج";
    public static string MovProduce            => "وارد إنتاج";
    public static string MovWaste              => "هالك";
    public static string MovAdjustment         => "تسوية جرد";
    public static string MovTransfer           => "تحويل";

    public static string SafeSale              => "تحصيل مبيعات";
    public static string SafePurchase          => "سداد مشتريات";
    public static string SafeExpense           => "مصاريف";
    public static string SafeWage              => "رواتب";
    public static string SafeTransferIn        => "تحويل وارد";
    public static string SafeTransferOut       => "تحويل صادر";
    
    public static string WageMonthly           => "شهري";
    public static string WageDaily             => "يومي";
    public static string WagePiecework         => "بالإنتاج";

    // ── Employee Transaction Types ───────────────────────────────────────────
    public static string TxEarned              => "أجر مستحق";
    public static string TxAdvance             => "سلفة";
    public static string TxBonus               => "مكافأة";
    public static string TxDeduction           => "خصم";
    public static string TxSalaryPayment       => "صرف راتب";

    // ── Users Module ─────────────────────────────────────────────────────────
    public static string UsersModule           => "المستخدمون";
    public static string AddUser               => "إضافة مستخدم";
    public static string EditUser              => "تعديل مستخدم";
    public static string FullNameLabel         => "الاسم الكامل";
    public static string ActiveState           => "نشط";
    public static string Permissions           => "الصلاحيات";
    public static string SearchPermission      => "بحث عن صلاحية...";
    public static string SelectAll             => "تحديد الكل";
    public static string UnselectAll           => "إلغاء تحديد الكل";
    public static string ResetPassword         => "إعادة تعيين كلمة المرور";
    public static string NewPassword           => "كلمة المرور الجديدة";
    public static string ConfirmNewPassword    => "تأكيد كلمة المرور";
    public static string ForcePasswordChange   => "إجبار المستخدم على تغيير كلمة المرور عند الدخول القادم";
    public static string MsgPasswordResetOk    => "تم إعادة تعيين كلمة المرور بنجاح.";
    public static string ErrUserNotFound       => "المستخدم غير موجود.";
    public static string EmployeeSettlements   => "تسويات الموظفين";
    public static string EmployeeJobs          => "المسميات الوظيفية";
    public static string BasicInformation      => "البيانات الأساسية";
    public static string BasicInfoSubtitle     => "البيانات الأساسية والصلاحيات الفردية";
    public static string ManageUsersSubtitle   => "إدارة مستخدمي النظام وصلاحياتهم الفردية";
    public static string Inactive              => "غير نشط";
    public static string SearchPermissions     => "البحث في الصلاحيات...";
    public static string SearchUsers           => "البحث عن مستخدم...";
    public static string TotalUsers            => "إجمالي المستخدمين";
    public static string ActiveUsers           => "المستخدمون النشطون";
    public static string ColUpdated            => "آخر تحديث";
    public static string DialogCancel          => "إلغاء";
    public static string SaveUser              => "حفظ المستخدم";
    public static string ResetPasswordTitle    => "إعادة تعيين كلمة المرور";
    public static string PasswordRequirements  => "يجب ألا تقل كلمة المرور عن 12 حرفاً.";
    public static string ResetButton           => "تعيين";
    public static string EditTooltip           => "تعديل";
    public static string ResetPasswordTooltip  => "إعادة تعيين كلمة المرور";
    public static string ToggleActiveTooltip   => "تفعيل أو تعطيل";
    public static string DeleteTooltip         => "حذف";
    public static string ConfirmToggleActive   => "هل أنت متأكد من {0} المستخدم '{1}'؟";
    public static string ConfirmDeleteUser     => "هل أنت متأكد من حذف المستخدم '{0}'؟";
    public static string ConfirmResetPassword  => "هل أنت متأكد من إعادة تعيين كلمة مرور المستخدم '{0}'؟";
    public static string ErrPasswordLength     => "يجب ألا تقل كلمة المرور عن 12 حرفاً.";
    public static string ErrPasswordRequired   => "أدخل كلمة المرور الجديدة.";
    public static string ErrPasswordMismatch   => "كلمتا المرور غير متطابقتين.";
    public static string ErrUserCannotDelete   => "لا يمكن حذف هذا المستخدم. يرجى تعطيل حسابه بدلاً من ذلك.";

    // ── Treasury Unauthorized Access Exceptions ────────────────────────────────
    public static string ErrUnauthorizedSafeAccess         => "ليس لديك صلاحية الوصول لهذه الخزنة.";
    public static string ErrUnauthorizedSafeViewLedger     => "ليس لديك صلاحية عرض كشف حساب هذه الخزنة.";
    public static string ErrUnauthorizedSafeCashIn         => "ليس لديك صلاحية الإيداع في هذه الخزنة.";
    public static string ErrUnauthorizedSafeCashOut        => "ليس لديك صلاحية السحب من هذه الخزنة.";
    public static string ErrUnauthorizedSafeTransferFrom   => "ليس لديك صلاحية التحويل من هذه الخزنة.";
    public static string ErrUnauthorizedSafeReceiveTransfer => "ليس لديك صلاحية التحويل إلى هذه الخزنة.";
    public static string ErrUnauthorizedSafeAccessCancel   => "ليس لديك صلاحية الوصول لهذه الخزنة لغرض الإلغاء.";
    public static string ErrUnauthorizedSafeCashInCancel   => "ليس لديك صلاحية الإيداع في هذه الخزنة لغرض الإلغاء.";
    public static string ErrUnauthorizedSafeCashOutCancel  => "ليس لديك صلاحية السحب من هذه الخزنة لغرض الإلغاء.";
    public static string ErrSelectOnePermission => "يجب اختيار صلاحية واحدة على الأقل.";
    public static string ErrPasswordsDoNotMatch => "كلمتا المرور غير متطابقتين.";
    public static string PermissionsSubtitle   => "تعيين الصلاحيات مباشرة لهذا المستخدم";
    public static string ColPermissionsCount   => "عدد الصلاحيات";

    public static string GetPermissionDisplayName(string key, string defaultName)
    {
        var lookupKey = KeyCasingMap.TryGetValue(key, out var exactKey) ? exactKey : key;
        return lookupKey switch
        {
            "Sales.View" => "عرض المبيعات",
            "Sales.Create" => "إنشاء فاتورة بيع",
            "Sales.Edit" => "تعديل فاتورة بيع",
            "Sales.Delete" => "حذف فاتورة بيع",
            "Sales.Print" => "طباعة فاتورة",

            "Customers.View" => "عرض العملاء",
            "Customers.Add" => "إضافة عميل",
            "Customers.Edit" => "تعديل عميل",
            "Customers.Delete" => "حذف عميل",

            "Products.View" => "عرض المنتجات",
            "Products.Add" => "إضافة منتج",
            "Products.Edit" => "تعديل منتج",
            "Products.Delete" => "حذف منتج",

            "Purchases.View" => "عرض المشتريات",
            "Purchases.Create" => "إنشاء فاتورة شراء",
            "Purchases.Edit" => "تعديل فاتورة شراء",
            "Purchases.Delete" => "حذف فاتورة شراء",

            "Production.View" => "عرض الإنتاج",
            "Production.Create" => "إنشاء أمر إنتاج",
            "Production.Edit" => "تعديل أمر إنتاج",
            "Production.Waste" => "تسجيل الهوالك والتالف",

            "Inventory.View" => "عرض المخزون",
            "Inventory.StockAdjustments" => "تسجيل حركة مخزون",
            "Inventory.Count" => "جرد المخزون",

            "Treasury.View" => "عرض الخزنة",
            "Treasury.CashIn" => "تسجيل قبض",
            "Treasury.CashOut" => "تسجيل صرف",
            "Treasury.Transfer" => "تحويل بين الخزن",
            "Treasury.ManageSafes" => "إدارة الخزن",

            "Accounting.View" => "عرض الحسابات",
            "Accounting.JournalEntries" => "القيود اليومية",
            "Accounting.CustomerLedger" => "كشف حساب العملاء",
            "Accounting.SupplierLedger" => "كشف حساب الموردين",

            "Employees.View" => "عرض الموظفين",
            "Employees.Add" => "إضافة موظف",
            "Employees.Edit" => "تعديل موظف",
            "Employees.Delete" => "حذف موظف",
            "Employees.Salaries" => "احتساب الرواتب والأجور",
            "Employees.Advances" => "صرف السلف والخصومات",

            "Reports.Sales" => "تقارير المبيعات",
            "Reports.Inventory" => "تقارير المخزون",
            "Reports.Financial" => "التقارير المالية",

            "WorkingDay.Open" => "فتح يوم العمل",
            "WorkingDay.Close" => "إغلاق يوم العمل",
            "WorkingDay.OverrideCloseBlockers" => "تجاوز موانع إغلاق يوم العمل",
            "WorkingDay.Reopen" => "إعادة فتح يوم العمل",

            "Settings.System" => "إعدادات النظام",
            "Settings.BranchManagement" => "إدارة الفروع",
            "Settings.ResetSystem" => "إعادة ضبط النظام",

            "Users.View" => "عرض المستخدمين",
            "Users.Add" => "إضافة مستخدم",
            "Users.Edit" => "تعديل مستخدم",
            "Users.Delete" => "حذف مستخدم",
            "Users.ChangePermissions" => "إدارة الصلاحيات",

            "Branches.Switch" => "التنقل بين الفروع",

            "Cash.Deposit" => "إيداع نقد يدوي",
            "Cash.Withdraw" => "سحب نقد يدوي",
            "Cash.ReverseManualTransaction" => "إلغاء حركة نقدية يدوية",
            "Treasury.ReversePartyPayment" => "عكس دفعة عميل أو مورد",
            "Cash.ViewAllTransactions" => "عرض الحركات النقدية اليدوية للجميع",

            "Sales.Cancel" => "إلغاء فاتورة بيع",
            "Products.ViewCost" => "عرض تكلفة الأصناف",
            "Purchases.Cancel" => "إلغاء فاتورة شراء",
            "Purchases.Print" => "طباعة فاتورة شراء",
            "Production.Cancel" => "إلغاء أمر إنتاج",
            "Employees.ViewSalary" => "عرض الرواتب والأجور",
            "Employees.ManagePayroll" => "إدارة الرواتب والتسويات",
            "Reports.Production" => "تقارير الإنتاج",
            "Reports.Print" => "طباعة التقارير",
            "Reports.Export" => "تصدير التقارير",
            "WorkingDay.View" => "عرض يوم العمل",
            "Users.ResetPassword" => "إعادة تعيين كلمة المرور",
            "Roles.View" => "عرض الأدوار",
            "Roles.Add" => "إضافة دور",
            "Roles.Edit" => "تعديل دور",
            "Roles.Delete" => "حذف دور",
            "Roles.Assign" => "تعيين الأدوار للمستخدمين",
            "Audit.View" => "عرض سجل التدقيق",
            "Audit.Export" => "تصدير سجل التدقيق",
            "Backup.ViewStatus" => "عرض حالة النسخ الاحتياطي",
            "Backup.CreateManual" => "إنشاء نسخة احتياطية يدوية",
            "Backup.Restore" => "استعادة نسخة احتياطية",
            "Backup.Delete" => "حذف نسخة احتياطية",
            "Backup.ManageSettings" => "إدارة إعدادات النسخ الاحتياطي",
            "Backup.ConnectGoogleDrive" => "ربط Google Drive",
            "Backup.DisconnectGoogleDrive" => "فصل Google Drive",
            _ => defaultName
        };
    }

    public static string GetPermissionDescription(string key)
    {
        var lookupKey = KeyCasingMap.TryGetValue(key, out var exactKey) ? exactKey : key;
        return lookupKey switch
        {
            "Sales.View" => "عرض فواتير المبيعات وحركات العملاء.",
            "Sales.Create" => "إنشاء وإصدار فواتير المبيعات الجديدة.",
            "Sales.Edit" => "تعديل فواتير المبيعات المسودة وغير المرحلة.",
            "Sales.Delete" => "حذف وإلغاء فواتير المبيعات نهائياً من النظام (إجراء حساس).",
            "Sales.Print" => "طباعة فواتير المبيعات وإيصالات الدفع.",

            "Customers.View" => "عرض قائمة العملاء ومتابعة حساباتهم.",
            "Customers.Add" => "إضافة عميل جديد لدفاتر الحسابات.",
            "Customers.Edit" => "تعديل بيانات وحسابات العملاء وسقوف الدين.",
            "Customers.Delete" => "حذف سجل عميل من النظام نهائياً (إجراء حساس).",

            "Products.View" => "عرض المنتجات والأصناف والمواد الخام.",
            "Products.Add" => "إضافة منتج أو مادة خام جديدة للمستودع.",
            "Products.Edit" => "تعديل أسعار بيع وشراء وبيانات الأصناف.",
            "Products.Delete" => "حذف منتج أو صنف نهائياً من المستودع (إجراء حساس).",

            "Purchases.View" => "عرض فواتير الشراء وحركات الموردين.",
            "Purchases.Create" => "تسجيل فواتير الشراء وتوريد البضائع.",
            "Purchases.Edit" => "تعديل فواتير المشتريات المسودة وغير المرحلة.",
            "Purchases.Delete" => "حذف فواتير شراء من النظام (إجراء حساس).",

            "Production.View" => "عرض وجبات الإنتاج الجارية والمكتملة.",
            "Production.Create" => "إنشاء أمر إنتاج وصرف المواد الخام.",
            "Production.Edit" => "تعديل أو إلغاء أوامر الإنتاج الجارية.",
            "Production.Waste" => "تسجيل المواد التالفة والهوالك في خطوط الإنتاج.",

            "Inventory.View" => "عرض ومتابعة كميات وأرصدة المستودعات.",
            "Inventory.StockAdjustments" => "تسجيل حركات تسوية المخزون بالزيادة والنقصان.",
            "Inventory.Count" => "بدء وإتمام وتعديل حركات جرد المخازن.",

            "Treasury.View" => "عرض أرصدة وحركات الخزن النقدية.",
            "Treasury.CashIn" => "تسجيل وقبض واردات نقدية للخزينة.",
            "Treasury.CashOut" => "تسجيل وصرف نفقات مالية من الخزينة.",
            "Treasury.Transfer" => "تحويل أرصدة نقدية بين الخزن المختلفة.",
            "Treasury.ManageSafes" => "إدارة وتهيئة وتعديل الخزن بالنظام (حساس ومقيد).",

            "Accounting.View" => "عرض شجرة الحسابات والقيود والتقارير المالية.",
            "Accounting.JournalEntries" => "إنشاء وتعديل قيود اليومية اليدوية.",
            "Accounting.CustomerLedger" => "عرض كشف حساب تفصيلي للعملاء.",
            "Accounting.SupplierLedger" => "عرض كشف حساب تفصيلي للموردين.",

            "Employees.View" => "عرض قائمة الموظفين وبياناتهم الوظيفية.",
            "Employees.Add" => "تسجيل موظف جديد وتحديد وظيفته.",
            "Employees.Edit" => "تعديل بيانات ورواتب ومستحقات الموظفين.",
            "Employees.Delete" => "حذف سجل موظف من النظام نهائياً (إجراء حساس).",
            "Employees.Salaries" => "احتساب الرواتب والعمليات المالية الشهرية للعاملين.",
            "Employees.Advances" => "تسجيل وصرف السلف النقدية وتعديل الخصومات والمكافآت (حساس).",

            "Reports.Sales" => "عرض وتصدير تقارير مبيعات المحل.",
            "Reports.Inventory" => "عرض وتصدير تقارير حركة وقيم المخزون.",
            "Reports.Financial" => "عرض الموازين والأرباح والخسائر والتقارير المالية.",

            "WorkingDay.Open" => "فتح يوم عمل جديد لبدء الورديات وعمليات الصندوق.",
            "WorkingDay.Close" => "إغلاق يوم العمل الحالي وترحيل الصناديق.",
            "WorkingDay.Reopen" => "إعادة فتح يوم عمل تم إغلاقه لتعديل الأخطاء (إجراء خطير).",

            "Settings.System" => "تعديل خيارات النظام الأساسية والنسخ الاحتياطي.",
            "Settings.BranchManagement" => "إدارة وتعديل الفروع وربطها بالمستخدمين.",
            "Settings.ResetSystem" => "حذف كامل البيانات التشغيلية وإعادة تصفير النظام (إجراء خطير جداً).",

            "Users.View" => "عرض حسابات مستخدمي النظام.",
            "Users.Add" => "إنشاء وإضافة مستخدم جديد للنظام وصلاحياته.",
            "Users.Edit" => "تعديل بيانات المستخدمين وحالة التفعيل وكلمات المرور.",
            "Users.Delete" => "حذف مستخدم نهائياً من قاعدة البيانات (إجراء خطير ومقيد).",
            "Users.ChangePermissions" => "تعديل وتخصيص الصلاحيات المالية والتشغيلية الممنوحة للمستخدم (إجراء خطير).",

            "Branches.Switch" => "السماح للمستخدم بالتبديل بين الفروع المتاحة له.",

            "Cash.Deposit" => "صلاحية إيداع نقد يدوي في الخزن.",
            "Cash.Withdraw" => "صلاحية سحب نقد يدوي من الخزن.",
            "Cash.ReverseManualTransaction" => "صلاحية إلغاء الحركات النقدية اليدوية وعمل حركات عكسية لها.",
            "Treasury.ReversePartyPayment" => "صلاحية عكس دفعة عميل أو مورد واستعادة رصيد الخزنة وحساب الطرف.",
            "Cash.ViewAllTransactions" => "صلاحية عرض كافة الحركات النقدية اليدوية لجميع المستخدمين.",

            "Sales.Cancel" => "إلغاء فاتورة بيع مع تطبيق القيود وحركات العكس المطلوبة.",
            "Products.ViewCost" => "عرض تكلفة الشراء والتكلفة المحاسبية وقيمة المخزون.",
            "Purchases.Cancel" => "إلغاء فاتورة شراء مع عكس آثارها المالية والمخزنية.",
            "Purchases.Print" => "طباعة فواتير الشراء.",
            "Production.Cancel" => "إلغاء أمر إنتاج وفق حالته وإعادة أثر المواد.",
            "Employees.ViewSalary" => "عرض بيانات الرواتب والأجور الحساسة.",
            "Employees.ManagePayroll" => "إدارة الأجور والرواتب والتسويات المالية للموظفين.",
            "Reports.Production" => "عرض تقارير الإنتاج وكفاءته.",
            "Reports.Print" => "طباعة التقارير التي يملك المستخدم صلاحية عرضها.",
            "Reports.Export" => "تصدير التقارير المصرح بها إلى ملفات خارجية.",
            "WorkingDay.View" => "عرض حالة يوم العمل وتاريخه وملخصه.",
            "Users.ResetPassword" => "إعادة تعيين كلمة مرور مستخدم آخر إلى كلمة مؤقتة.",
            "Roles.View" => "عرض الأدوار الأمنية والصلاحيات الممنوحة لها.",
            "Roles.Add" => "إنشاء دور أمني قابل لإعادة الاستخدام.",
            "Roles.Edit" => "تعديل صلاحيات دور أمني وتطبيقها على مستخدميه.",
            "Roles.Delete" => "حذف دور غير محمي وغير مرتبط بمستخدمين.",
            "Roles.Assign" => "ربط المستخدمين بالأدوار الأمنية أو إلغاء الربط.",
            "Audit.View" => "عرض سجل التدقيق للعمليات الحساسة.",
            "Audit.Export" => "تصدير سجل التدقيق للمراجعة الخارجية.",
            "Backup.ViewStatus" => "عرض حالة النسخ المحلية والسحابية وملخص سلامتها.",
            "Backup.CreateManual" => "إنشاء نسخة احتياطية محلية عند الطلب.",
            "Backup.Restore" => "استبدال البيانات الحالية من نسخة صالحة بعد إنشاء نسخة أمان.",
            "Backup.Delete" => "حذف ملف نسخة محلية مع إبقاء سجلها.",
            "Backup.ManageSettings" => "تغيير مجلد النسخ الاحتياطي وإدارة إعداداته.",
            "Backup.ConnectGoogleDrive" => "ربط حساب Google Drive لرفع نسخة سحابية إضافية.",
            "Backup.DisconnectGoogleDrive" => "إزالة ربط Google Drive والبيانات الآمنة المحلية.",
            "WorkingDay.OverrideCloseBlockers" => "تجاوز موانع الإغلاق بعد توثيق السبب (إجراء حساس).",
            _ => "إذن بالوصول للعمليات المطلوبة."
        };
    }

    public static string GetPermissionCategoryName(string category)
    {
        return category switch
        {
            "Sales" => "المبيعات",
            "Customers" => "العملاء",
            "Products" => "المنتجات",
            "Purchases" => "المشتريات",
            "Production" => "الإنتاج",
            "Inventory" => "المخزون",
            "Treasury" => "الخزينة",
            "Accounting" => "الحسابات",
            "Employees" => "الموظفين",
            "Reports" => "التقارير",
            "Working Day" => "يوم العمل",
            "Settings" => "الإعدادات",
            "Users" => "المستخدمين",
            "Branches" => "الفروع",
            "Cash Operations" => "العمليات النقدية",
            "Roles" => "الأدوار",
            "Audit" => "سجل التدقيق",
            "Backup" => "النسخ الاحتياطي",
            _ => category
        };
    }

    private static readonly System.Collections.Generic.Dictionary<string, string> KeyCasingMap = new(System.StringComparer.OrdinalIgnoreCase)
    {
        { "Sales.View", "Sales.View" },
        { "Sales.Create", "Sales.Create" },
        { "Sales.Edit", "Sales.Edit" },
        { "Sales.Delete", "Sales.Delete" },
        { "Sales.Print", "Sales.Print" },
        { "Customers.View", "Customers.View" },
        { "Customers.Add", "Customers.Add" },
        { "Customers.Edit", "Customers.Edit" },
        { "Customers.Delete", "Customers.Delete" },
        { "Products.View", "Products.View" },
        { "Products.Add", "Products.Add" },
        { "Products.Edit", "Products.Edit" },
        { "Products.Delete", "Products.Delete" },
        { "Purchases.View", "Purchases.View" },
        { "Purchases.Create", "Purchases.Create" },
        { "Purchases.Edit", "Purchases.Edit" },
        { "Purchases.Delete", "Purchases.Delete" },
        { "Production.View", "Production.View" },
        { "Production.Create", "Production.Create" },
        { "Production.Edit", "Production.Edit" },
        { "Production.Waste", "Production.Waste" },
        { "Inventory.View", "Inventory.View" },
        { "Inventory.StockAdjustments", "Inventory.StockAdjustments" },
        { "Inventory.Count", "Inventory.Count" },
        { "Treasury.View", "Treasury.View" },
        { "Treasury.CashIn", "Treasury.CashIn" },
        { "Treasury.CashOut", "Treasury.CashOut" },
        { "Treasury.Transfer", "Treasury.Transfer" },
        { "Treasury.ManageSafes", "Treasury.ManageSafes" },
        { "Accounting.View", "Accounting.View" },
        { "Accounting.JournalEntries", "Accounting.JournalEntries" },
        { "Accounting.CustomerLedger", "Accounting.CustomerLedger" },
        { "Accounting.SupplierLedger", "Accounting.SupplierLedger" },
        { "Employees.View", "Employees.View" },
        { "Employees.Add", "Employees.Add" },
        { "Employees.Edit", "Employees.Edit" },
        { "Employees.Delete", "Employees.Delete" },
        { "Employees.Salaries", "Employees.Salaries" },
        { "Employees.Advances", "Employees.Advances" },
        { "Reports.Sales", "Reports.Sales" },
        { "Reports.Inventory", "Reports.Inventory" },
        { "Reports.Financial", "Reports.Financial" },
        { "WorkingDay.Open", "WorkingDay.Open" },
        { "WorkingDay.Close", "WorkingDay.Close" },
        { "WorkingDay.Reopen", "WorkingDay.Reopen" },
        { "Settings.System", "Settings.System" },
        { "Settings.BranchManagement", "Settings.BranchManagement" },
        { "Settings.ResetSystem", "Settings.ResetSystem" },
        { "Users.View", "Users.View" },
        { "Users.Add", "Users.Add" },
        { "Users.Edit", "Users.Edit" },
        { "Users.Delete", "Users.Delete" },
        { "Users.ChangePermissions", "Users.ChangePermissions" },
        { "Branches.Switch", "Branches.Switch" },
        { "Cash.Deposit", "Cash.Deposit" },
        { "Cash.Withdraw", "Cash.Withdraw" },
        { "Cash.ReverseManualTransaction", "Cash.ReverseManualTransaction" },
        { "Treasury.ReversePartyPayment", "Treasury.ReversePartyPayment" },
        { "Cash.ViewAllTransactions", "Cash.ViewAllTransactions" },
        { "Backup.ViewStatus", "Backup.ViewStatus" },
        { "Backup.CreateManual", "Backup.CreateManual" },
        { "Backup.Restore", "Backup.Restore" },
        { "Backup.Delete", "Backup.Delete" },
        { "Backup.ManageSettings", "Backup.ManageSettings" },
        { "Backup.ConnectGoogleDrive", "Backup.ConnectGoogleDrive" },
        { "Backup.DisconnectGoogleDrive", "Backup.DisconnectGoogleDrive" }
    };

    // ── Audit Log Localization Helpers ─────────────────────────────────────
    public static string LocalizeAuditEntity(string? entityName)
    {
        if (string.IsNullOrWhiteSpace(entityName)) return entityName ?? string.Empty;

        return entityName.Trim() switch
        {
            "User" => "مستخدم",
            "Role" => "دور وظيفي",
            "Permission" => "صلاحية",
            "UserSafePermission" => "صلاحية خزنة للمستخدم",
            "Branch" => "فرع",
            "Safe" or "Treasury" => "خزنة",
            "SafeTransaction" or "TreasuryTransaction" => "معاملة خزينة",
            "Product" or "Item" or "InventoryItem" => "صنف",
            "Customer" => "عميل",
            "Supplier" => "مورد",
            "Party" => "طرف متعامل",
            "PartyLedger" => "حساب جهة",
            "Invoice" => "فاتورة",
            "PurchaseInvoice" => "فاتورة شراء",
            "SaleInvoice" => "فاتورة بيع",
            "WorkingDay" => "دورة العمل",
            "Backup" or "BackupRecord" => "نسخة احتياطية",
            "GoogleDrive" => "جوجل درايف",
            "Settings" => "الإعدادات",
            "Employee" => "موظف",
            "EmployeeWage" => "أجر موظف",
            "JobRole" => "مسمى وظيفي",
            "Recipe" => "وصفة تصنيع",
            "ProductionOrder" => "أمر إنتاج",
            "WasteEntry" => "هالك",
            "StockCountSession" => "جرد مخزني",
            "SafeMovement" => "حركة خزينة",
            "InventoryMovement" => "حركة مخزنية",
            "System" => "النظام",
            _ => entityName
        };
    }

    public static string LocalizeAuditAction(string? action)
    {
        if (string.IsNullOrWhiteSpace(action)) return action ?? string.Empty;

        var trimmed = action.Trim();

        if (AuditActionArabicLocalizer.TryGet(trimmed, out var localized))
            return localized;

        return trimmed switch
        {
            // ── Authentication ────────────────────────────────────────────
            "Login" or "UserLogin" or "Auth.Login" => "تسجيل الدخول",
            "Logout" or "Auth.Logout" => "تسجيل الخروج",
            "LoginFailed" or "Failed login" => "فشل تسجيل الدخول",

            // ── Users ─────────────────────────────────────────────────────
            "Create" or "UserCreated" or "RoleCreated" or "CreateBranch" => "إضافة",
            "Update" or "UserUpdated" or "RoleUpdated" or "UpdateBranch" => "تعديل",
            "Delete" or "UserDeleted" or "RoleDeleted" or "DeleteBranch" => "حذف",
            "UserActiveStateChanged" => "تغيير حالة تفعيل المستخدم",
            "UserPasswordChanged" => "تغيير كلمة المرور",
            "UserPasswordReset" => "إعادة تعيين كلمة المرور",
            "UserSafePermissionsUpdated" => "تعديل صلاحيات خزائن المستخدم",

            // ── Branches ──────────────────────────────────────────────────
            "ActivateBranch" => "تنشيط فرع",
            "DeactivateBranch" => "إيقاف فرع",

            // ── Working Day ───────────────────────────────────────────────
            "Open Working Day" or "OpenWorkingDay" or "OpenDay" => "فتح دورة العمل",
            "AutoOpenDay" => "فتح دورة العمل تلقائياً",
            "Close Working Day" or "CloseWorkingDay" or "CloseDay" => "إغلاق دورة العمل",
            "ReopenWorkingDay" or "ReopenDay" => "إعادة فتح دورة العمل",
            "DiscardEmptySuccessorForReopen" => "حذف يوم عمل فارغ لإعادة الفتح",

            // ── Treasury / Safe ───────────────────────────────────────────
            "Deposit" or "TreasuryDeposit" or "Cash.Deposit" => "إيداع",
            "Withdraw" or "Withdrawal" or "TreasuryWithdrawal" or "Cash.Withdraw" => "سحب",
            "Transfer" or "TreasuryTransfer" => "تحويل",
            "ManualDeposit" => "إيداع يدوي",
            "ManualWithdrawal" => "سحب يدوي",
            "ReverseManualTransaction" => "عكس حركة يدوية",
            "CreateReverseTransaction" => "حركة عكسية",

            // ── Invoices ──────────────────────────────────────────────────
            "Post sale invoice" => "ترحيل فاتورة بيع",
            "Cancel sale invoice" => "إلغاء فاتورة بيع",
            "Post purchase invoice" => "ترحيل فاتورة شراء",
            "Cancel purchase invoice" => "إلغاء فاتورة شراء",
            "Process party payment" => "تسجيل دفعة حساب",

            // ── Inventory ─────────────────────────────────────────────────
            "Inventory adjustment" => "تسوية مخزنية",
            "Start stock count" => "بدء جرد مخزني",
            "Complete stock count" => "إتمام جرد مخزني",

            // ── Backup ────────────────────────────────────────────────────
            "Backup" or "CreateBackup" or "BackupCreated" => "إنشاء نسخة احتياطية",
            "BackupManualCreation" => "بدء نسخة احتياطية يدوية",
            "BackupManualSuccess" => "نجاح النسخة الاحتياطية اليدوية",
            "BackupAutomaticCreation" => "بدء نسخة احتياطية تلقائية",
            "BackupAutomaticSuccess" => "نجاح النسخة الاحتياطية التلقائية",
            "BackupFailure" => "فشل النسخة الاحتياطية",
            "BackupSettingsChanged" => "تغيير إعدادات النسخ الاحتياطي",
            "BackupManualDeletion" => "حذف نسخة احتياطية يدوياً",
            "BackupAutomaticDeletion" => "حذف نسخة احتياطية تلقائياً",

            // ── Restore ───────────────────────────────────────────────────
            "Restore" or "RestoreBackup" or "DatabaseRestored" => "استعادة",
            "BackupRestoreAttempt" => "محاولة استعادة نسخة احتياطية",
            "BackupRestoreSuccess" => "نجاح استعادة النسخة الاحتياطية",
            "BackupRestoreFailure" => "فشل استعادة النسخة الاحتياطية",

            // ── Google Drive ──────────────────────────────────────────────
            "GoogleDriveConnected" => "ربط جوجل درايف",
            "GoogleDriveDisconnected" => "فصل جوجل درايف",

            // ── System ────────────────────────────────────────────────────
            "FactoryReset" => "إعادة ضبط المصنع",

            // Fallback: unknown or legacy action strings display as-is
            _ => trimmed
        };
    }

    public static string LocalizeAuditValue(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return value ?? string.Empty;

        var str = value.Trim();

        if (str.Equals("True", StringComparison.OrdinalIgnoreCase)) return "نعم";
        if (str.Equals("False", StringComparison.OrdinalIgnoreCase)) return "لا";

        var translated = str switch
        {
            "OpeningBalance" => MovOpening,
            "SaleCollection" => SafeSale,
            "PurchasePayment" => SafePurchase,
            "ExpensePayment" => SafeExpense,
            "WagePayment" => SafeWage,
            "TransferIn" => SafeTransferIn,
            "TransferOut" => SafeTransferOut,
            "Adjustment" => MovAdjustment,
            "Draft" => StatusDraft,
            "Posted" => StatusPosted,
            "Cancelled" => StatusCancelled,
            "Open" => DayOpen,
            "Closed" => DayClosed,
            "Customer" => TypeCustomer,
            "Supplier" => TypeSupplier,
            "Employee" => TypeEmployee,
            "Mixed" => TypeMixed,
            "RawMaterial" => ItemRaw,
            "FinishedProduct" => ItemFinished,
            "Fuel" => ItemFuel,
            "Service" => ItemService,
            "Packaging" => ItemPackaging,
            "Monthly" => WageMonthly,
            "Daily" => WageDaily,
            "Production" => WagePiecework,
            "Earned" => TxEarned,
            "Advance" => TxAdvance,
            "Bonus" => TxBonus,
            "Deduction" => TxDeduction,
            "SalaryPayment" => TxSalaryPayment,
            "Automatic" => "تلقائي",
            "Manual" => "يدوي",
            "SafetyBeforeRestore" => "أمان قبل الاستعادة",
            "Success" => "ناجح",
            "Failed" => "فشل",
            _ => null
        };

        if (translated != null) return translated;

        return str
            .Replace("IsActive: True", "الحالة: نشط", StringComparison.OrdinalIgnoreCase)
            .Replace("IsActive: False", "الحالة: غير نشط", StringComparison.OrdinalIgnoreCase)
            .Replace(": True", ": نعم", StringComparison.OrdinalIgnoreCase)
            .Replace(": False", ": لا", StringComparison.OrdinalIgnoreCase)
            .Replace(": true", ": نعم", StringComparison.OrdinalIgnoreCase)
            .Replace(": false", ": لا", StringComparison.OrdinalIgnoreCase);
    }
}
