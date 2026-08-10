namespace Raindrop.Data.Entities;

public abstract class AggregateRoot<TPrimaryKey> : Entity<TPrimaryKey>, IAggregateRoot
{
}

public abstract class AggregateRoot : AggregateRoot<int>
{
}
