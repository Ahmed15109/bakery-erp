using Bakery.Application.DTOs;
using Bakery.Application.DTOs.Inventory;
using Bakery.Application.Interfaces;
using Bakery.Application.Security;
using Bakery.Domain.Entities;
using Bakery.Domain.Enums;
using Bakery.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Bakery.Infrastructure.Services;

public sealed class ItemService : IItemService
{
    private readonly BakeryDbContext _dbContext;
    private readonly IValidationService _validationService;
    private readonly IPermissionService _permissionService;
    private readonly IStockCalculationService _stockCalculationService;

    public ItemService(
        BakeryDbContext dbContext,
        IValidationService validationService,
        IPermissionService permissionService,
        IStockCalculationService stockCalculationService)
    {
        _dbContext = dbContext;
        _validationService = validationService;
        _permissionService = permissionService;
        _stockCalculationService = stockCalculationService;
    }

    public async Task<IReadOnlyList<ItemDto>> SearchAsync(string? searchText, ItemType? type, CancellationToken cancellationToken = default)
    {
        _permissionService.EnsurePermission(PermissionKeys.ProductsView);
        var query = _dbContext.Items
            .Include(item => item.BaseUnit)
            .AsNoTracking();

        if (!string.IsNullOrWhiteSpace(searchText))
        {
            query = query.Where(item => 
                EF.Functions.Like(item.Name, $"%{searchText}%") || 
                EF.Functions.Like(item.Code, $"%{searchText}%") ||
                (item.Barcode != null && EF.Functions.Like(item.Barcode, $"%{searchText}%")));
        }

        if (type.HasValue)
        {
            query = query.Where(item => item.Type == type.Value);
        }

        var items = await query.ToListAsync(cancellationToken);
        var itemIds = items.Select(item => item.Id).ToArray();
        var stockByItem = await _stockCalculationService.GetCurrentStockAsync(itemIds, cancellationToken);
        var canViewCost = _permissionService.HasPermission(PermissionKeys.ProductsViewCost);
        return items.Select(item => ToDto(item, stockByItem.GetValueOrDefault(item.Id), canViewCost)).ToArray();
    }

    public async Task<ItemDto?> GetByIdAsync(int id, CancellationToken cancellationToken = default)
    {
        _permissionService.EnsurePermission(PermissionKeys.ProductsView);
        var item = await _dbContext.Items
            .Include(item => item.BaseUnit)
            .Include(item => item.ItemUnits)
            .ThenInclude(iu => iu.Unit)
            .FirstOrDefaultAsync(item => item.Id == id, cancellationToken);

        if (item is null) return null;

        var currentStock = await GetCurrentStockAsync(item.Id, cancellationToken);
        return ToDto(item, currentStock, _permissionService.HasPermission(PermissionKeys.ProductsViewCost));
    }

    public async Task<(bool Succeeded, string? ErrorMessage, ItemDto? Item)> SaveAsync(SaveItemRequest request, CancellationToken cancellationToken = default)
    {
        _permissionService.EnsurePermission(request.Id.HasValue ? PermissionKeys.ProductsEdit : PermissionKeys.ProductsAdd);
        _permissionService.EnsurePermission(PermissionKeys.ProductsViewCost);

        if (await _validationService.IsItemCodeUsedAsync(request.Code, request.Id))
            return (false, "كود الصنف مستخدم بالفعل", null);

        if (!string.IsNullOrWhiteSpace(request.Barcode) && await _validationService.IsBarcodeUsedAsync(request.Barcode, request.Id))
            return (false, "الباركود مستخدم بالفعل", null);

        Item? item;
        if (request.Id.HasValue)
        {
            item = await _dbContext.Items.Include(x => x.ItemUnits).FirstOrDefaultAsync(x => x.Id == request.Id.Value, cancellationToken);
            if (item == null) return (false, "الصنف غير موجود", null);
            if (item.BaseUnitId != request.BaseUnitId &&
                await _dbContext.InventoryMovements.AnyAsync(movement => movement.ItemId == item.Id, cancellationToken))
            {
                return (false, "لا يمكن تغيير الوحدة الأساسية بعد تسجيل حركات مخزنية للصنف.", null);
            }
        }
        else
        {
            item = new Item();
            _dbContext.Items.Add(item);
        }

        item.Code = request.Code;
        item.Name = request.Name;
        item.Barcode = request.Barcode;
        item.Type = request.Type;
        item.BaseUnitId = request.BaseUnitId;
        item.PurchasePrice = request.PurchasePrice;
        item.SalePrice = request.SalePrice;
        item.MinStockLevel = request.MinStockLevel;
        item.ReorderLevel = request.ReorderLevel;
        item.Notes = request.Notes;
        item.IsActive = request.IsActive;

        if (!request.Id.HasValue)
        {
            item.ItemUnits.Add(new ItemUnit
            {
                UnitId = request.BaseUnitId,
                ConversionFactorToBaseUnit = 1,
                IsDefaultUnit = true,
                IsDefaultPurchaseUnit = true,
                IsDefaultSaleUnit = request.Type == ItemType.FinishedProduct
            });
        }
        else
        {
            var baseRelation = item.ItemUnits.FirstOrDefault(unit => unit.UnitId == request.BaseUnitId);
            if (baseRelation is null)
            {
                item.ItemUnits.Add(new ItemUnit
                {
                    UnitId = request.BaseUnitId,
                    ConversionFactorToBaseUnit = 1,
                    IsDefaultUnit = true
                });
            }
            else
            {
                baseRelation.ConversionFactorToBaseUnit = 1;
                baseRelation.IsDefaultUnit = true;
            }
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
        return (true, null, ToDto(item, 0, true));
    }

    public async Task<(bool Succeeded, string? ErrorMessage)> SoftDeleteAsync(int id, CancellationToken cancellationToken = default)
    {
        _permissionService.EnsurePermission(PermissionKeys.ProductsDelete);
        var item = await _dbContext.Items.FindAsync(new object[] { id }, cancellationToken);
        if (item == null) return (false, "الصنف غير موجود");

        if (await _dbContext.InventoryMovements.AnyAsync(x => x.ItemId == id, cancellationToken))
            return (false, "لا يمكن حذف صنف له حركات مخزنية. يمكنك إيقاف تنشيطه بدلاً من ذلك.");

        _dbContext.Items.Remove(item);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return (true, null);
    }

    public async Task<(bool Succeeded, string? ErrorMessage)> SetActiveAsync(int id, bool isActive, CancellationToken cancellationToken = default)
    {
        _permissionService.EnsurePermission(PermissionKeys.ProductsEdit);
        var item = await _dbContext.Items.FindAsync(new object[] { id }, cancellationToken);
        if (item == null) return (false, "الصنف غير موجود");

        item.IsActive = isActive;
        await _dbContext.SaveChangesAsync(cancellationToken);
        return (true, null);
    }

    public async Task<decimal> GetCurrentStockAsync(int itemId, CancellationToken cancellationToken = default)
    {
        _permissionService.EnsureAnyPermission(PermissionKeys.ProductsView, PermissionKeys.InventoryView, PermissionKeys.ReportsInventory);
        return await _stockCalculationService.GetCurrentStockAsync(itemId, cancellationToken);
    }

    private static ItemDto ToDto(Item item, decimal currentStock, bool canViewCost)
    {
        return new ItemDto(item.Id, item.Code, item.Name, item.Barcode, item.Type, item.BaseUnitId, item.BaseUnit?.Name ?? item.BaseUnit?.Symbol ?? "بدون", canViewCost ? item.PurchasePrice : 0, item.SalePrice, item.MinStockLevel, item.ReorderLevel, item.IsActive, item.Notes, currentStock);
    }
}
