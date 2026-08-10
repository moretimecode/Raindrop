using FluentValidation;
using MediatR;
using Raindrop.CQRS.Behaviors;
using Raindrop.CQRS.Context;

namespace Raindrop.CQRS.FluentValidation.Behaviors;

/// <summary>
/// FluentValidation 验证 Pipeline Behavior。自动调用所有注册的 <see cref="IValidator{TRequest}"/>。
/// 验证失败时委托 <see cref="IValidationFailureHandler"/> 处理。
/// </summary>
/// <typeparam name="TRequest">请求类型</typeparam>
/// <typeparam name="TResponse">响应类型</typeparam>
public class ValidatorBehavior<TRequest, TResponse> : GlobalPipelineBehavior<TRequest, TResponse>
    where TRequest : class
{
    private readonly IEnumerable<IValidator<TRequest>> _validators;
    private readonly IValidationFailureHandler _failureHandler;

    /// <summary>
    /// 初始化验证行为。
    /// </summary>
    public ValidatorBehavior(
        IEnumerable<IValidator<TRequest>> validators,
        IPipelineContextAccessor accessor,
        IValidationFailureHandler failureHandler)
        : base(accessor)
    {
        _validators = validators;
        _failureHandler = failureHandler;
    }

    /// <inheritdoc />
    public override async Task<TResponse> InvokeAsync(TRequest request, RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        var validationResults = await Task.WhenAll(_validators.Select(v =>
            v.ValidateAsync(request, options => options.IncludeAllRuleSets(), cancellationToken)));

        var errors = validationResults
            .SelectMany(result => result.Errors)
            .Where(e => e is not null)
            .ToList();

        if (errors.Count > 0)
        {
            _failureHandler.Handle(errors);
        }

        return await next(cancellationToken);
    }
}
