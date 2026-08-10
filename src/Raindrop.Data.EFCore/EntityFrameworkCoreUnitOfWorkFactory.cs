using Raindrop.UnitOfWork.Abstractions;

namespace Raindrop.Data.EFCore;

/// <summary>
/// EF Core 工作单元工厂。
/// 返回当前 DI scope 中的 EF Core 工作单元，并在返回前显式开启事务。
/// </summary>
public sealed class EntityFrameworkCoreUnitOfWorkFactory<TDbContext>(
    EntityFrameworkCoreUnitOfWork<TDbContext> unitOfWork) : IUnitOfWorkFactory
    where TDbContext : Microsoft.EntityFrameworkCore.DbContext
{
    public async ValueTask<IUnitOfWork> BeginAsync(CancellationToken cancellationToken = default)
    {
        await unitOfWork.BeginAsync(cancellationToken).ConfigureAwait(false);
        return unitOfWork;
    }
}
