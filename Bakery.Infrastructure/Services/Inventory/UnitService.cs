using Bakery.Application.DTOs.Inventory;
using Bakery.Application.Interfaces;
using Bakery.Application.Security;
using Bakery.Domain.Entities;
using Bakery.Infrastructure.Data;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Bakery.Shared.Helpers;

namespace Bakery.Infrastructure.Services;

public sealed class UnitService : IUnitService
{
    private readonly BakeryDbContext _dbContext;
    private readonly IValidator<SaveUnitRequest> _validator;
    private readonly IPermissionService _permissionService;

    public UnitService(BakeryDbContext dbContext, IValidator<SaveUnitRequest> validator, IPermissionService permissionService)
    {
        _dbContext = dbContext;
        _validator = validator;
        _permissionService = permissionService;
    }

    public async Task<IReadOnlyList<UnitDto>> ListAsync(CancellationToken cancellationToken = default)
    {
        _permissionService.EnsurePermission(PermissionKeys.ProductsView);
        return await _dbContext.Units.OrderBy(unit => unit.Name).Select(unit => new UnitDto(unit.Id, unit.Name, unit.Symbol, unit.IsActive)).ToListAsync(cancellationToken);
    }

    public async Task<(bool Succeeded, string? ErrorMessage, UnitDto? Unit)> SaveAsync(SaveUnitRequest request, CancellationToken cancellationToken = default)
    {
        _permissionService.EnsurePermission(request.Id is null or 0 ? PermissionKeys.ProductsAdd : PermissionKeys.ProductsEdit);
        var validation = await _validator.ValidateAsync(request, cancellationToken);
        if (!validation.IsValid) return (false, validation.Errors[0].ErrorMessage, null);

        var duplicate = await _dbContext.Units.AnyAsync(unit => unit.Symbol == request.Symbol && unit.Id != request.Id, cancellationToken);
        if (duplicate) return (false, Loc.ErrUnitSymbolExists, null);

        var unit = request.Id is null or 0 ? new Unit() : await _dbContext.Units.FirstAsync(entity => entity.Id == request.Id, cancellationToken);
        if (request.Id is null or 0) _dbContext.Units.Add(unit);
        unit.Name = request.Name.Trim();
        unit.Symbol = request.Symbol.Trim();
        unit.IsActive = request.IsActive;
        await _dbContext.SaveChangesAsync(cancellationToken);
        return (true, null, new UnitDto(unit.Id, unit.Name, unit.Symbol, unit.IsActive));
    }

    public async Task<(bool Succeeded, string? ErrorMessage)> DeleteAsync(int id, CancellationToken cancellationToken = default)
    {
        _permissionService.EnsurePermission(PermissionKeys.ProductsDelete);
        var unit = await _dbContext.Units.FirstOrDefaultAsync(entity => entity.Id == id, cancellationToken);
        if (unit is null) return (false, "الوحدة غير موجودة.");

        var isUsed = await _dbContext.Items.AnyAsync(item => item.BaseUnitId == id && !item.IsDeleted, cancellationToken)
            || await _dbContext.ItemUnits.AnyAsync(itemUnit => itemUnit.UnitId == id && !itemUnit.IsDeleted && !itemUnit.Item.IsDeleted, cancellationToken)
            || await _dbContext.InventoryMovements.AnyAsync(movement => movement.UnitId == id && !movement.IsDeleted, cancellationToken)
            || await _dbContext.PurchaseInvoiceLines.AnyAsync(line => line.UnitId == id && !line.IsDeleted, cancellationToken)
            || await _dbContext.SaleInvoiceLines.AnyAsync(line => line.UnitId == id && !line.IsDeleted, cancellationToken);

        if (isUsed) return (false, "لا يمكن حذف الوحدة لأنها مستخدمة في النظام");

        unit.IsDeleted = true;
        unit.IsActive = false;
        await _dbContext.SaveChangesAsync(cancellationToken);
        return (true, null);
    }

    public async Task<(bool Succeeded, string? ErrorMessage)> SaveItemUnitAsync(SaveItemUnitRequest request, CancellationToken cancellationToken = default)
    {
        _permissionService.EnsurePermission(PermissionKeys.ProductsEdit);
        if (request.ConversionFactorToBaseUnit <= 0) return (false, Loc.ErrQtyPositive);
        var item = await _dbContext.Items.FirstOrDefaultAsync(entity => entity.Id == request.ItemId, cancellationToken);
        if (item is null) return (false, "الصنف غير موجود.");
        if (!await _dbContext.Units.AnyAsync(entity => entity.Id == request.UnitId && entity.IsActive, cancellationToken))
            return (false, "الوحدة غير موجودة أو غير نشطة.");
        if (request.UnitId == item.BaseUnitId && request.ConversionFactorToBaseUnit != 1m)
            return (false, "معامل تحويل الوحدة الأساسية يجب أن يساوي 1.");
        var duplicate = await _dbContext.ItemUnits.AnyAsync(
            entity => entity.ItemId == request.ItemId &&
                      entity.UnitId == request.UnitId &&
                      entity.Id != request.Id,
            cancellationToken);
        if (duplicate) return (false, "الوحدة مرتبطة بهذا الصنف بالفعل.");
        var itemUnit = request.Id is null or 0 ? new ItemUnit() : await _dbContext.ItemUnits.FirstAsync(entity => entity.Id == request.Id, cancellationToken);
        if (request.Id is > 0 && itemUnit.ConversionFactorToBaseUnit != request.ConversionFactorToBaseUnit)
        {
            var usedByMovement = await _dbContext.InventoryMovements.AnyAsync(
                movement => movement.ItemId == itemUnit.ItemId && movement.UnitId == itemUnit.UnitId,
                cancellationToken);
            if (usedByMovement)
                return (false, "لا يمكن تغيير معامل التحويل بعد استخدام الوحدة في حركة مخزنية.");
        }
        if (request.Id is null or 0) _dbContext.ItemUnits.Add(itemUnit);
        itemUnit.ItemId = request.ItemId;
        itemUnit.UnitId = request.UnitId;
        itemUnit.ConversionFactorToBaseUnit = request.ConversionFactorToBaseUnit;
        itemUnit.IsDefaultUnit = request.IsDefaultUnit;
        itemUnit.IsDefaultPurchaseUnit = request.IsDefaultPurchaseUnit;
        itemUnit.IsDefaultSaleUnit = request.IsDefaultSaleUnit;
        await _dbContext.SaveChangesAsync(cancellationToken);
        return (true, null);
    }

    public async Task<IReadOnlyList<ItemUnitDto>> GetItemUnitsAsync(int itemId, CancellationToken cancellationToken = default)
    {
        _permissionService.EnsurePermission(PermissionKeys.ProductsView);
        var units = await _dbContext.ItemUnits.Include(itemUnit => itemUnit.Unit).Where(itemUnit => itemUnit.ItemId == itemId)
            .Select(itemUnit => new ItemUnitDto(itemUnit.Id, itemUnit.ItemId, itemUnit.UnitId, itemUnit.Unit.Name, itemUnit.ConversionFactorToBaseUnit, itemUnit.IsDefaultUnit, itemUnit.IsDefaultPurchaseUnit, itemUnit.IsDefaultSaleUnit))
            .ToListAsync(cancellationToken);
        var baseUnit = await _dbContext.Items
            .Where(item => item.Id == itemId)
            .Select(item => new { item.BaseUnitId, item.BaseUnit.Name })
            .SingleOrDefaultAsync(cancellationToken);
        if (baseUnit is not null && units.All(unit => unit.UnitId != baseUnit.BaseUnitId))
            units.Add(new ItemUnitDto(0, itemId, baseUnit.BaseUnitId, baseUnit.Name, 1m, true, true, true));
        return units;
    }
}
