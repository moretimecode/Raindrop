namespace Raindrop.UnitOfWork.Abstractions;

/// <summary>
/// 自动工作单元标记。
/// 标记在应用服务、HTTP 处理器或事件处理器上，由外层执行管道负责开启、提交和回滚工作单元。
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, Inherited = true)]
public sealed class UnitOfWorkAttribute : Attribute
{
}
