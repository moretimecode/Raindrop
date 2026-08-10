namespace Raindrop.CQRS;

/// <summary>
/// 无返回值命令的占位响应类型，替代 MediatR 的 Unit。
/// </summary>
public readonly struct NullableResponse
{
    private static readonly NullableResponse _value = new();

    /// <summary>
    /// 单例值。
    /// </summary>
    public static ref readonly NullableResponse Value => ref _value;

    /// <summary>
    /// 已完成的 <see cref="NullableResponse"/> Task。
    /// </summary>
    public static Task<NullableResponse> Task { get; } = System.Threading.Tasks.Task.FromResult(_value);
}
