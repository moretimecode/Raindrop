using System.Data.Common;
using Raindrop.UnitOfWork.Abstractions;

namespace Raindrop.Data.Tests;

/// <summary>
/// 工作单元开启模型的契约测试。
/// 这些测试只描述公开接口，不依赖 EF Core 内部实现。
/// </summary>
public class UnitOfWorkNextDesignTests
{
    [Fact]
    public void UnitOfWorkFactory_IsManualTransactionEntry()
    {
        var factoryType = ResolveRequiredAbstraction("IUnitOfWorkFactory");
        var unitOfWorkType = typeof(IUnitOfWork);
        var expectedReturnType = typeof(ValueTask<>).MakeGenericType(unitOfWorkType);

        var beginMethods = factoryType
            .GetMethods()
            .Where(method => method.Name == "BeginAsync")
            .ToArray();

        Assert.Contains(beginMethods, method => method.ReturnType == expectedReturnType);
    }

    [Fact]
    public void RelationalUnitOfWork_ExposesConnectionAndTransaction()
    {
        var relationalType = ResolveRequiredAbstraction("IRelationalUnitOfWork");

        Assert.True(
            typeof(IUnitOfWork).IsAssignableFrom(relationalType),
            "关系型工作单元必须同时是一个普通工作单元。");

        var connectionProperty = relationalType.GetProperty("Connection");
        var transactionProperty = relationalType.GetProperty("Transaction");

        Assert.NotNull(connectionProperty);
        Assert.Equal(typeof(DbConnection), connectionProperty!.PropertyType);
        Assert.NotNull(transactionProperty);
        Assert.Equal(typeof(DbTransaction), transactionProperty!.PropertyType);
    }

    [Fact]
    public void UnitOfWorkAttribute_IsClassLevelMarker()
    {
        var attributeType = ResolveRequiredAbstraction("UnitOfWorkAttribute");

        Assert.True(typeof(Attribute).IsAssignableFrom(attributeType));

        var usage = attributeType
            .GetCustomAttributes(typeof(AttributeUsageAttribute), inherit: false)
            .OfType<AttributeUsageAttribute>()
            .SingleOrDefault();

        Assert.NotNull(usage);
        Assert.True(usage!.ValidOn.HasFlag(AttributeTargets.Class));
    }

    private static Type ResolveRequiredAbstraction(string typeName)
    {
        var type = Type.GetType($"Raindrop.UnitOfWork.Abstractions.{typeName}, Raindrop.UnitOfWork.Abstractions");

        Assert.NotNull(type);
        return type!;
    }
}
