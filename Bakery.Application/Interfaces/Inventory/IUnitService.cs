using Bakery.Application.DTOs.Inventory;

namespace Bakery.Application.Interfaces;

public interface IUnitService
{
    Task<IReadOnlyList<UnitDto>> ListAsync(CancellationToken cancellationToken = default);
    Task<(bool Succeeded, string? ErrorMessage, UnitDto? Unit)> SaveAsync(SaveUnitRequest request, CancellationToken cancellationToken = default);
    Task<(bool Succeeded, string? ErrorMessage)> DeleteAsync(int id, CancellationToken cancellationToken = default);
    Task<(bool Succeeded, string? ErrorMessage)> SaveItemUnitAsync(SaveItemUnitRequest request, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ItemUnitDto>> GetItemUnitsAsync(int itemId, CancellationToken cancellationToken = default);
}
