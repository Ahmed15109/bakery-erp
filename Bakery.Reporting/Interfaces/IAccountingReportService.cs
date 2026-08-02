using Bakery.Application.DTOs.Accounting;

namespace Bakery.Reporting.Interfaces;

public interface IAccountingReportService
{
    Task<decimal> GetDailySalesAsync(DateOnly date, CancellationToken cancellationToken = default);
    Task<decimal> GetDailyPurchasesAsync(DateOnly date, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<SalesByItemDto>> GetSalesByItemAsync(DateOnly date, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<PartyDto>> GetCustomerBalancesAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<PartyDto>> GetSupplierBalancesAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<InvoiceDto>> GetInvoiceHistoryAsync(CancellationToken cancellationToken = default);
    Task<decimal> GetCashMovementSummaryAsync(DateOnly date, CancellationToken cancellationToken = default);
}
