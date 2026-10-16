using System.Collections.Frozen;

namespace Raindrop.Data.EFCore;

internal sealed class OutboxContractCatalog(Dictionary<Type, (string Name, string Version)> contracts)
{
    private readonly FrozenDictionary<Type, (string Name, string Version)> _contracts = contracts.ToFrozenDictionary();

    public (string Name, string Version) Resolve(Type type) => _contracts.TryGetValue(type, out var contract)
        ? contract
        : throw new InvalidOperationException($"集成事件 {type.Name} 尚未注册稳定契约名称和版本。");
}
