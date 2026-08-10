using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Raindrop.Data.EFCore;
using Raindrop.Data.Entities;
using Raindrop.Data.Repository;
using Raindrop.UnitOfWork.Abstractions;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Raindrop.Data.EFCore 的 DI 注册扩展方法。
/// </summary>
public static class DataServiceCollectionExtensions
{
    /// <summary>
    /// 注册 Raindrop EFCore 数据服务（DbContext + 工作单元 + 仓储自动扫描）。
    /// 默认扫描 TDbContext 所在程序集中的所有聚合根并注册仓储。
    /// </summary>
    /// <param name="services">IServiceCollection。</param>
    /// <param name="dbContextOptions">配置 DbContext 选项的回调。</param>
    /// <typeparam name="TDbContext">用户的自定义 DbContext 类型。</typeparam>
    public static IServiceCollection AddRaindropData<TDbContext>(
        this IServiceCollection services,
        Action<DbContextOptionsBuilder>? dbContextOptions = null)
        where TDbContext : DbContext
    {
        return AddRaindropData<TDbContext>(services, dbContextOptions, typeof(TDbContext).Assembly);
    }

    /// <summary>
    /// 注册 Raindrop EFCore 数据服务（DbContext + 工作单元 + 仓储自动扫描）。
    /// </summary>
    /// <param name="services">IServiceCollection。</param>
    /// <param name="dbContextOptions">配置 DbContext 选项的回调。</param>
    /// <param name="assemblies">扫描聚合根的程序集。默认使用 TDbContext 所在程序集。</param>
    /// <typeparam name="TDbContext">用户的自定义 DbContext 类型。</typeparam>
    public static IServiceCollection AddRaindropData<TDbContext>(
        this IServiceCollection services,
        Action<DbContextOptionsBuilder>? dbContextOptions,
        params Assembly[] assemblies)
        where TDbContext : DbContext
    {
        ArgumentNullException.ThrowIfNull(services);

        // 注册 DbContext
        if (dbContextOptions is not null)
        {
            services.AddDbContext<TDbContext>(dbContextOptions);
        }
        else
        {
            services.AddDbContext<TDbContext>();
        }

        // 注册工作单元（scoped，与 DbContext 生命周期一致）
        services.TryAddScoped<EntityFrameworkCoreUnitOfWork<TDbContext>>();
        services.TryAddScoped<IUnitOfWork>(sp => sp.GetRequiredService<EntityFrameworkCoreUnitOfWork<TDbContext>>());
        services.TryAddScoped<IRelationalUnitOfWork>(sp => sp.GetRequiredService<EntityFrameworkCoreUnitOfWork<TDbContext>>());
        services.TryAddScoped<IUnitOfWorkFactory, EntityFrameworkCoreUnitOfWorkFactory<TDbContext>>();

        // 自动扫描聚合根并注册仓储
        var scanAssemblies = assemblies.Length > 0
            ? assemblies
            : [typeof(TDbContext).Assembly];

        RegisterRepositories<TDbContext>(services, scanAssemblies);

        return services;
    }

    private static void RegisterRepositories<TDbContext>(
        IServiceCollection services,
        Assembly[] assemblies)
        where TDbContext : DbContext
    {
        var aggregateRootTypes = assemblies
            .Distinct()
            .SelectMany(assembly => assembly.DefinedTypes)
            .Where(type => type is
            {
                IsAbstract: false,
                IsInterface: false,
                IsGenericType: false
            })
            .Select(type => type.AsType())
            .Where(type => IsAssignableToGenericType(type, typeof(AggregateRoot<>)));

        foreach (var type in aggregateRootTypes)
        {
            var keyType = ResolveKeyType(type);

            if (keyType is null)
            {
                continue;
            }

            var repoInterface = typeof(IRepository<,>).MakeGenericType(type, keyType);
            var repoImplementation = typeof(EntityFrameworkCoreRepository<,,>)
                .MakeGenericType(typeof(TDbContext), type, keyType);

            services.TryAddScoped(repoInterface, repoImplementation);
        }
    }

    private static bool IsAssignableToGenericType(Type givenType, Type genericType)
    {
        for (var type = givenType; type is not null; type = type.BaseType)
        {
            if (type.IsGenericType && type.GetGenericTypeDefinition() == genericType)
            {
                return true;
            }
        }

        return false;
    }

    private static Type? ResolveKeyType(Type aggregateRootType)
    {
        for (var type = aggregateRootType; type is not null; type = type.BaseType)
        {
            if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(AggregateRoot<>))
            {
                return type.GetGenericArguments()[0];
            }
        }

        return null;
    }
}
