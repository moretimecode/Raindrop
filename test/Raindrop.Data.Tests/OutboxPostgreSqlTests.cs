using Microsoft.EntityFrameworkCore;

namespace Raindrop.Data.Tests;

[Collection(PostgreSqlCollection.Name)]
public sealed class OutboxPostgreSqlTests(PostgreSqlTestFixture database)
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task PhysicalTransaction_CommitsOrRollsBackBusinessAndMessagesTogether(bool commit)
    {
        await using var f = await OutboxRegistrationTests.Fixture.CreateAsync(postgres: database.ConnectionString);
        await f.BeginAsync();
        var business = new OutboxRegistrationTests.BusinessRow();
        business.AddDomainEvent(new OutboxRegistrationTests.Fact { Text = "聚合" });
        f.Context.Add(business);
        await f.Bus.PublishAsync(new OutboxRegistrationTests.Fact { Text = "主动" });
        if (commit) await f.Uow.CommitAsync();
        else
        {
            // 主动发布已经登记；显式保存后回滚，验证不是另一个连接上的独立写入。
            await f.Context.SaveChangesAsync();
            await f.Uow.RollbackAsync();
        }
        using var independent = new OutboxRegistrationTests.TestContext(
            new DbContextOptionsBuilder<OutboxRegistrationTests.TestContext>().UseNpgsql(database.ConnectionString).Options);
        Assert.Equal(commit ? 1 : 0, await independent.Rows.CountAsync());
        Assert.Equal(commit ? 2 : 0, await independent.Set<Raindrop.Data.EFCore.OutboxMessage>().CountAsync());
        Assert.Equal(0, f.Transport.SendCount);
    }
}
