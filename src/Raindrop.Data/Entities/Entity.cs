using Raindrop.Data.Events;

namespace Raindrop.Data.Entities;

public abstract class Entity<TPrimaryKey> : IEntity<TPrimaryKey>
{
    private readonly List<IEvent> _domainEvents = [];

    public void AddDomainEvent(IEvent @event)
    {
        _domainEvents.Add(@event);
    }

    public IReadOnlyCollection<IEvent> GetDomainEvents()
    {
        return _domainEvents;
    }

    public void ClearDomainEvents()
    {
        _domainEvents.Clear();
    }

    public virtual TPrimaryKey Id { get; set; } = default!;

    public virtual bool IsTransient()
    {
        if (EqualityComparer<TPrimaryKey>.Default.Equals(Id, default))
        {
            return true;
        }

        // EF Core attach 实体时可能把 int/long 临时主键设成最小值。
        if (typeof(TPrimaryKey) == typeof(int))
        {
            return Convert.ToInt32(Id) <= 0;
        }

        if (typeof(TPrimaryKey) == typeof(long))
        {
            return Convert.ToInt64(Id) <= 0;
        }

        return false;
    }
}

public abstract class Entity : IEntity
{
    private readonly List<IEvent> _domainEvents = [];

    public void AddDomainEvent(IEvent @event)
    {
        _domainEvents.Add(@event);
    }

    public IReadOnlyCollection<IEvent> GetDomainEvents()
    {
        return _domainEvents;
    }

    public void ClearDomainEvents()
    {
        _domainEvents.Clear();
    }
}
