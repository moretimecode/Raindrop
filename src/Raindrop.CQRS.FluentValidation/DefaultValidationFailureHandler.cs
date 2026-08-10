using System.Text;
using FluentValidation.Results;
using Microsoft.Extensions.Options;
using Raindrop.Utility;

namespace Raindrop.CQRS.FluentValidation;

/// <summary>
/// 默认验证失败处理器。将所有错误消息拼接后抛出 <see cref="FriendlyException"/>。
/// </summary>
internal sealed class DefaultValidationFailureHandler : IValidationFailureHandler
{
    private readonly CQRSValidationOptions _options;

    public DefaultValidationFailureHandler(IOptions<CQRSValidationOptions> options)
    {
        _options = options.Value;
    }

    public void Handle(List<ValidationFailure> errors)
    {
        var builder = new StringBuilder();

        foreach (var error in errors)
        {
            builder.Append(error.ErrorMessage + ";");
        }

        // 取第一条错误的 ErrorCode，解析失败则使用 DefaultErrorCode
        var errorCode = _options.DefaultErrorCode;
        if (int.TryParse(errors.FirstOrDefault()?.ErrorCode, out var parsed) && parsed != 0)
        {
            errorCode = parsed;
        }

        var errorMessage = builder.ToString();
        if (_options.ErrorMessageMaxLength > 0 && errorMessage.Length > _options.ErrorMessageMaxLength)
        {
            errorMessage = errorMessage[.._options.ErrorMessageMaxLength];
        }

        throw new FriendlyException($"Invalid arguments: {errorMessage}", errorCode);
    }
}
