using FluentValidation.Results;

namespace Raindrop.CQRS.FluentValidation;

/// <summary>
/// 验证失败处理器接口。当 ValidatorBehavior 收集到错误时调用。
/// </summary>
public interface IValidationFailureHandler
{
    /// <summary>
    /// 处理验证失败列表。默认实现抛出 <see cref="Raindrop.Utility.FriendlyException"/>。
    /// </summary>
    void Handle(List<ValidationFailure> errors);
}
