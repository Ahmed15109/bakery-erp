namespace Bakery.Domain.Constants;

public static class LedgerReferenceTypes
{
    public const string SaleInvoice = "SaleInvoice";
    public const string SaleCancel = "SaleCancel";
    public const string PurchaseInvoice = "PurchaseInvoice";
    public const string PurchaseCancel = "PurchaseCancel";
    public const string CustomerReceipt = "CustomerReceipt";
    public const string SupplierPayment = "SupplierPayment";
    public const string WorkingDayClose = "WorkingDayClose";
    public const string WorkingDayReopen = "WorkingDayReopen";
}
