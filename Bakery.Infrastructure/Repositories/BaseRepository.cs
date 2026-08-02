using Bakery.Application.Interfaces;
using Bakery.Domain.Entities;
using Bakery.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Bakery.Infrastructure.Repositories;

public class BaseRepository<TEntity> : IRepository<TEntity> where TEntity : BaseEntity
{
    protected internal readonly BakeryDbContext DbContext;
    protected readonly DbSet<TEntity> DbSet;

    public BaseRepository(BakeryDbContext dbContext)
    {
        DbContext = dbContext;
        DbSet = dbContext.Set<TEntity>();
    }

    public Task<TEntity?> GetByIdAsync(int id, CancellationToken cancellationToken = default)
    {
        return DbSet.FirstOrDefaultAsync(entity => entity.Id == id && !entity.IsDeleted, cancellationToken);
    }

    public async Task<IReadOnlyList<TEntity>> ListAsync(CancellationToken cancellationToken = default)
    {
        return await DbSet.Where(entity => !entity.IsDeleted).AsNoTracking().ToListAsync(cancellationToken);
    }

    public async Task<TEntity> AddAsync(TEntity entity, CancellationToken cancellationToken = default)
    {
        await DbSet.AddAsync(entity, cancellationToken);
        return entity;
    }

    public Task UpdateAsync(TEntity entity, CancellationToken cancellationToken = default)
    {
        entity.UpdatedAt = DateTime.UtcNow;

        // Check if an instance with the same ID is already tracked to avoid conflict
        var local = DbContext.Set<TEntity>()
            .Local
            .FirstOrDefault(entry => entry.Id.Equals(entity.Id));

        if (local != null)
        {
            // Detach the local tracked instance so we can update with the new one
            DbContext.Entry(local).State = EntityState.Detached;
        }

        DbSet.Update(entity);
        return Task.CompletedTask;
    }

    public Task DeleteAsync(TEntity entity, CancellationToken cancellationToken = default)
    {
        entity.IsDeleted = true;
        entity.UpdatedAt = DateTime.UtcNow;

        var local = DbContext.Set<TEntity>()
            .Local
            .FirstOrDefault(entry => entry.Id.Equals(entity.Id));

        if (local != null)
        {
            DbContext.Entry(local).State = EntityState.Detached;
        }

        DbSet.Update(entity);
        return Task.CompletedTask;
    }
}
