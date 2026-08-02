using System.Globalization;
using System.Windows.Data;
using Bakery.Domain.Enums;
using Bakery.Shared.Helpers;

namespace Bakery.WPF.Converters;

public sealed class EnumToLocConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is null) return string.Empty;

        return value switch
        {
            InvoiceStatus s => s switch
            {
                InvoiceStatus.Draft => Loc.StatusDraft,
                InvoiceStatus.Posted => Loc.StatusPosted,
                InvoiceStatus.Cancelled => Loc.StatusCancelled,
                _ => s.ToString()
            },
            PartyType t => t switch
            {
                PartyType.Customer => Loc.TypeCustomer,
                PartyType.Supplier => Loc.TypeSupplier,
                PartyType.Employee => Loc.TypeEmployee,
                PartyType.Mixed => Loc.TypeMixed,
                _ => t.ToString()
            },
            PaymentType p => p switch
            {
                PaymentType.Cash => Loc.PaymentCash,
                PaymentType.Credit => Loc.PaymentCredit,
                PaymentType.Mixed => Loc.PaymentMixed,
                _ => p.ToString()
            },
            WorkingDayStatus w => w switch
            {
                WorkingDayStatus.Open => Loc.DayOpen,
                WorkingDayStatus.Closed => Loc.DayClosed,
                WorkingDayStatus.Cancelled => Loc.StatusCancelled,
                _ => w.ToString()
            },
            ItemType i => i switch
            {
                ItemType.RawMaterial => Loc.ItemRaw,
                ItemType.FinishedProduct => Loc.ItemFinished,
                ItemType.Fuel => Loc.ItemFuel,
                ItemType.Service => Loc.ItemService,
                ItemType.Packaging => Loc.ItemPackaging,
                _ => i.ToString()
            },
            InventoryMovementType m => m switch
            {
                InventoryMovementType.OpeningBalance => Loc.MovOpening,
                InventoryMovementType.Purchase => Loc.MovPurchase,
                InventoryMovementType.Sale => Loc.MovSale,
                InventoryMovementType.ProductionConsume => Loc.MovConsume,
                InventoryMovementType.ProductionProduce => Loc.MovProduce,
                InventoryMovementType.Waste => Loc.MovWaste,
                InventoryMovementType.Adjustment => Loc.MovAdjustment,
                InventoryMovementType.Transfer => Loc.MovTransfer,
                _ => m.ToString()
            },
            SafeMovementType sm => sm switch
            {
                SafeMovementType.OpeningBalance => Loc.MovOpening,
                SafeMovementType.SaleCollection => Loc.SafeSale,
                SafeMovementType.PurchasePayment => Loc.SafePurchase,
                SafeMovementType.ExpensePayment => Loc.SafeExpense,
                SafeMovementType.WagePayment => Loc.SafeWage,
                SafeMovementType.TransferIn => Loc.SafeTransferIn,
                SafeMovementType.TransferOut => Loc.SafeTransferOut,
                SafeMovementType.Adjustment => Loc.MovAdjustment,
                _ => sm.ToString()
            },
            WageType wt => wt switch
            {
                WageType.Monthly => Loc.WageMonthly,
                WageType.Daily => Loc.WageDaily,
                WageType.Production => Loc.WagePiecework,
                _ => wt.ToString()
            },
            EmployeeTransactionType ett => ett switch
            {
                EmployeeTransactionType.Earned => Loc.TxEarned,
                EmployeeTransactionType.Advance => Loc.TxAdvance,
                EmployeeTransactionType.Bonus => Loc.TxBonus,
                EmployeeTransactionType.Deduction => Loc.TxDeduction,
                EmployeeTransactionType.SalaryPayment => Loc.TxSalaryPayment,
                _ => ett.ToString()
            },
            BackupType backupType => backupType switch
            {
                BackupType.Automatic => "تلقائي",
                BackupType.Manual => "يدوي",
                BackupType.SafetyBeforeRestore => "أمان قبل الاستعادة",
                _ => backupType.ToString()
            },
            BackupStatus backupStatus => backupStatus switch
            {
                BackupStatus.Creating => "جاري الإنشاء",
                BackupStatus.Validating => "جاري التحقق",
                BackupStatus.Success => "ناجح",
                BackupStatus.Failed => "فشل",
                BackupStatus.Restoring => "جاري الاستعادة",
                _ => backupStatus.ToString()
            },
            CloudBackupStatus cloudStatus => cloudStatus switch
            {
                CloudBackupStatus.NotEnabled => "غير مفعّل",
                CloudBackupStatus.PendingUpload => "بانتظار الرفع",
                CloudBackupStatus.Uploading => "جاري الرفع",
                CloudBackupStatus.Uploaded => "تم الرفع",
                CloudBackupStatus.UploadFailed => "تعذر الرفع",
                _ => cloudStatus.ToString()
            },
            _ => value.ToString() ?? string.Empty
        };
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotImplementedException();
}
