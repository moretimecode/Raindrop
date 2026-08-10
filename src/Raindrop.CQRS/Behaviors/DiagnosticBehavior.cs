using System.Diagnostics;
using MediatR;
using Raindrop.CQRS.Context;

namespace Raindrop.CQRS.Behaviors;

/// <summary>
/// OpenTelemetry 诊断 Pipeline Behavior。为每个请求创建一个 Activity span。
/// 作为全局行为自动应用于所有请求。
/// </summary>
/// <typeparam name="TRequest">请求类型</typeparam>
/// <typeparam name="TResponse">响应类型</typeparam>
public class DiagnosticBehavior<TRequest, TResponse> : GlobalPipelineBehavior<TRequest, TResponse>
    where TRequest : class
{
    /// <summary>
    /// 初始化诊断行为。
    /// </summary>
    public DiagnosticBehavior(IPipelineContextAccessor accessor) : base(accessor)
    {
    }

    /// <inheritdoc />
    public override async Task<TResponse> InvokeAsync(TRequest request, RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        using var activity = CQRSActivitySource.Source.StartActivity(
            $"{request.GetType().Name}", ActivityKind.Internal);

        try
        {
            var response = await next.Invoke();

            activity?.SetStatus(ActivityStatusCode.Ok);

            return response;
        }
        catch (Exception ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            throw;
        }
    }
}
