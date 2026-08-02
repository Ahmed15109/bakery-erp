using Bakery.Domain.Entities;

namespace Bakery.Application.Interfaces;

public interface IProductionPostingEngine
{
    Task PostProductionAsync(int productionOrderId);
    Task CancelProductionAsync(int productionOrderId);
}
