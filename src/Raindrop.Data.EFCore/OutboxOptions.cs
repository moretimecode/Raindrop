using Raindrop.EventBus.Abstractions;

namespace Raindrop.Data.EFCore;

/// <summary>声明稳定的集成事件契约；配置结束后会复制冻结。</summary>
public sealed class OutboxOptions
{
    private readonly Dictionary<Type, (string Name, string Version)> _contracts = [];

    public OutboxOptions AddContract<TEvent>(string name, string version) where TEvent : IIntegrationEvent
    {
        if (string.IsNullOrWhiteSpace(name) || name.Length > 200)
            throw new ArgumentException("契约名称不能为空且不得超过 200 个字符。", nameof(name));
        if (string.IsNullOrWhiteSpace(version) || version.Length > 64)
            throw new ArgumentException("契约版本不能为空且不得超过 64 个字符。", nameof(version));
        if (_contracts.ContainsKey(typeof(TEvent)) || _contracts.Values.Contains((name, version)))
            throw new ArgumentException("事件类型或契约名称/版本重复。");
        _contracts.Add(typeof(TEvent), (name, version));
        return this;
    }

    internal OutboxContractCatalog Freeze() => new(_contracts);
}
