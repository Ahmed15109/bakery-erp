using Bakery.Application.DTOs.Inventory;
using Bakery.Domain.Enums;

namespace Bakery.Application.Interfaces;

public interface IItemService
{
    Task<IReadOnlyList<ItemDto>> SearchAsync(string? search, ItemType? type, CancellationToken cancellationToken = default);
    Task<ItemDto?> GetByIdAsync(int id, CancellationToken cancellationToken = default);
    Task<(bool Succeeded, string? ErrorMessage, ItemDto? Item)> SaveAsync(SaveItemRequest request, CancellationToken cancellationToken = default);
    Task<(bool Succeeded, string? ErrorMessage)> SoftDeleteAsync(int id, CancellationToken cancellationToken = default);
    Task<(bool Succeeded, string? ErrorMessage)> SetActiveAsync(int id, bool isActive, CancellationToken cancellationToken = default);
}
