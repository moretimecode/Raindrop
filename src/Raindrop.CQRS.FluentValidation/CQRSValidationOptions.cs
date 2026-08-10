namespace Raindrop.CQRS.FluentValidation;

/// <summary>
/// CQRS 验证配置选项。
/// </summary>
public class CQRSValidationOptions
{
    /// <summary>
    /// 默认错误码。验证失败但未指定 ErrorCode 时使用。
    /// </summary>
    public int DefaultErrorCode { get; set; } = 400;

    /// <summary>
    /// 错误消息最大长度。为 0 时不截断。
    /// </summary>
    public int ErrorMessageMaxLength { get; set; }
}
