using MediatR;
using Raindrop.CQRS.Context;

namespace Raindrop.CQRS.Behaviors;

/// <summary>
/// Pipeline Behavior 抽象基类。自动在调用前后捕获 Request / Response 到 <see cref="PipelineContext"/>。
/// </summary>
/// <typeparam name="TRequest">请求类型（必须是 class）</typeparam>
/// <typeparam name="TResponse">响应类型</typeparam>
public abstract class AbstractPipelineBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : class
{
    private readonly IPipelineContextAccessor _contextAccessor;

    /// <summary>
    /// 当前管道上下文。
    /// </summary>
    public PipelineContext Context => _contextAccessor.Context;

    /// <summary>
    /// 初始化行为基类。
    /// </summary>
    protected AbstractPipelineBehavior(IPipelineContextAccessor accessor)
    {
        _contextAccessor = accessor;
    }

    /// <summary>
    /// 自定义行为的入口方法。
    /// </summary>
    public abstract Task<TResponse> InvokeAsync(TRequest request, RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken);

    /// <summary>
    /// MediatR 管道入口。设置 Request → 调用子类实现 → 设置 Response。
    /// </summary>
    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        Context.SetRequest(request);

        var response = await InvokeAsync(request, next, cancellationToken).ConfigureAwait(false);

        Context.SetResponse(response!);

        return response;
    }
}
