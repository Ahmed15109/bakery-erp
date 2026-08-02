using Bakery.Application.DTOs.Accounting;
using Bakery.Domain.Enums;

namespace Bakery.Application.Interfaces;

public interface IInvoiceNumberAllocator
{
    Task<string> AllocateSaleNumberAsync(
        int branchId,
        DateOnly businessDate,
        CancellationToken cancellationToken = default);
    Task<string> AllocatePurchaseNumberAsync(
        int branchId,
        DateOnly businessDate,
        CancellationToken cancellationToken = default);
}

public interface ISaleInvoiceService
{
    Task<IReadOnlyList<InvoiceDto>> ListAsync(InvoiceStatus? status = null, CancellationToken cancellationToken = default);
    Task<(bool Succeeded, string? ErrorMessage, int? InvoiceId)> SaveDraftAsync(SaveSaleInvoiceRequest request, CancellationToken cancellationToken = default);
    Task<(bool Succeeded, string? ErrorMessage)> PostAsync(int invoiceId, CancellationToken cancellationToken = default);
    Task<(bool Succeeded, string? ErrorMessage)> CancelAsync(int invoiceId, string reason, CancellationToken cancellationToken = default);
    Task<(bool Succeeded, string? ErrorMessage)> DeleteDraftAsync(int invoiceId, string reason, CancellationToken cancellationToken = default)
        => Task.FromResult((false, (string?)"حذف مسودة فاتورة البيع غير مدعوم بواسطة هذه الخدمة."));
    Task<InvoicePrintDto?> GetPrintAsync(int invoiceId, string layout, CancellationToken cancellationToken = default);
}

public interface IPurchaseInvoiceService
{
    Task<IReadOnlyList<InvoiceDto>> ListAsync(InvoiceStatus? status = null, CancellationToken cancellationToken = default);
    Task<(bool Succeeded, string? ErrorMessage, int? InvoiceId)> SaveDraftAsync(SavePurchaseInvoiceRequest request, CancellationToken cancellationToken = default);
    Task<(bool Succeeded, string? ErrorMessage)> PostAsync(int invoiceId, CancellationToken cancellationToken = default);
    Task<(bool Succeeded, string? ErrorMessage)> CancelAsync(int invoiceId, string reason, CancellationToken cancellationToken = default);
    Task<(bool Succeeded, string? ErrorMessage)> DeleteDraftAsync(int invoiceId, string reason, CancellationToken cancellationToken = default)
        => Task.FromResult((false, (string?)"حذف مسودة فاتورة المشتريات غير مدعوم بواسطة هذه الخدمة."));
    Task<InvoicePrintDto?> GetPrintAsync(int invoiceId, string layout, CancellationToken cancellationToken = default);
}
