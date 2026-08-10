using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Raindrop.Data.Entities;
using Raindrop.Data.Repository;
using Raindrop.Data.Specifications;
using Raindrop.UnitOfWork.Abstractions;

namespace Raindrop.Data.EFCore;

/// <summary>
/// EF Core 聚合根仓储实现。
/// </summary>
public class EntityFrameworkCoreRepository<TDbContext, TEntity, TPrimaryKey> : IRepository<TEntity, TPrimaryKey>
    where TDbContext : DbContext
    where TEntity : AggregateRoot<TPrimaryKey>
{
    private readonly TDbContext _context;
    private readonly IUnitOfWork _unitOfWork;

    public EntityFrameworkCoreRepository(TDbContext context, IUnitOfWork unitOfWork)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(unitOfWork);
        _context = context;
        _unitOfWork = unitOfWork;
    }

    protected DbSet<TEntity> Table => _context.Set<TEntity>();

    public IUnitOfWork UnitOfWork => _unitOfWork;

    // ── 写 ──

    public virtual async Task InsertAsync(TEntity entity)
    {
        ArgumentNullException.ThrowIfNull(entity);
        await Table.AddAsync(entity);
    }

    public virtual Task UpdateAsync(TEntity entity)
    {
        ArgumentNullException.ThrowIfNull(entity);
        AttachIfNot(entity);
        _context.Entry(entity).State = EntityState.Modified;
        return Task.CompletedTask;
    }

    public virtual Task DeleteAsync(TEntity entity)
    {
        ArgumentNullException.ThrowIfNull(entity);
        AttachIfNot(entity);
        Table.Remove(entity);
        return Task.CompletedTask;
    }

    public virtual async Task<TPrimaryKey> InsertAndGetIdAsync(TEntity entity)
    {
        ArgumentNullException.ThrowIfNull(entity);
        await Table.AddAsync(entity);

        if (entity.IsTransient())
        {
            await _context.SaveChangesAsync();
        }

        return entity.Id;
    }

    public virtual async Task ExecuteCmd(string sql, params object[] parameters)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sql);
        await _context.Database.ExecuteSqlRawAsync(sql, parameters);
    }

    // ── 查 ──

    public virtual async Task<List<TEntity>> GetAllAsync()
    {
        return await Table.ToListAsync();
    }

    public virtual async Task<TEntity?> GetAsync(TPrimaryKey id)
    {
        return await Table.FindAsync([id]);
    }

    public virtual async Task<List<TQuery>> QueryAsync<TQuery>(Expression<Func<TQuery, bool>> predicate)
        where TQuery : class
    {
        ArgumentNullException.ThrowIfNull(predicate);
        return await _context.Set<TQuery>().Where(predicate).ToListAsync();
    }

    public virtual async Task<TQuery?> FirstOrDefaultAsync<TQuery>(Expression<Func<TQuery, bool>> predicate)
        where TQuery : class
    {
        ArgumentNullException.ThrowIfNull(predicate);
        return await _context.Set<TQuery>().FirstOrDefaultAsync(predicate);
    }

    public virtual async Task<List<TEntity>> GetListAsync(ISpecification<TEntity> specification)
    {
        ArgumentNullException.ThrowIfNull(specification);
        return await ApplySpecification(specification).ToListAsync();
    }

    // ── 内部 ──

    protected virtual void AttachIfNot(TEntity entity)
    {
        var entry = _context.ChangeTracker.Entries<TEntity>()
            .FirstOrDefault(e => e.Entity == entity);

        if (entry is null)
        {
            Table.Attach(entity);
        }
    }

    protected virtual IQueryable<TEntity> ApplySpecification(ISpecification<TEntity> specification)
    {
        var query = Table.AsQueryable();

        if (specification.Criteria is not null)
        {
            query = query.Where(specification.Criteria);
        }

        query = specification.Includes
            .Aggregate(query, (current, include) => current.Include(include));

        query = specification.IncludeStrings
            .Aggregate(query, (current, include) => current.Include(include));

        if (specification.OrderBy is not null)
        {
            query = query.OrderBy(specification.OrderBy);
        }
        else if (specification.OrderByDescending is not null)
        {
            query = query.OrderByDescending(specification.OrderByDescending);
        }

        if (specification.IsPagingEnabled)
        {
            query = query.Skip(specification.Skip).Take(specification.Take);
        }

        return query;
    }
}
