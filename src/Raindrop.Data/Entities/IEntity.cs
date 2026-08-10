using Raindrop.Data.Events;

namespace Raindrop.Data.Entities;

public interface IEntity
{
    void AddDomainEvent(IEvent @event);

    IReadOnlyCollection<IEvent> GetDomainEvents();

    void ClearDomainEvents();
}

/// <summary>
/// 带主键的实体。
/// </summary>
public interface IEntity<TPrimaryKey> : IEntity
{
    TPrimaryKey Id { get; set; }

    bool IsTransient();
}
