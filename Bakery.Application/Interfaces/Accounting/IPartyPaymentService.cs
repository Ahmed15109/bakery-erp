using System.Threading;
using System.Threading.Tasks;

namespace Bakery.Application.Interfaces;

public interface IPartyPaymentService
{
    Task<(bool Succeeded, string? ErrorMessage)> ProcessPaymentAsync(int partyId, int safeId, decimal amount, string description, bool? isReceipt = null, string? idempotencyKey = null, CancellationToken cancellationToken = default);
    Task<(bool Succeeded, string? ErrorMessage, int? ReversalMovementId)> ReversePaymentAsync(
        int originalSafeMovementId,
        string reason,
        Guid correlationId,
        bool fromWorkingDayReopenWorkflow,
        CancellationToken cancellationToken = default)
        => Task.FromResult((false, (string?)"عكس دفعة الحساب غير مدعوم بواسطة هذه الخدمة.", (int?)null));
}
