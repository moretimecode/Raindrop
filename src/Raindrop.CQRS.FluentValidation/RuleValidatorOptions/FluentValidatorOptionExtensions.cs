namespace FluentValidation;

/// <summary>
/// FluentValidation 扩展方法。
/// </summary>
public static class FluentValidatorOptionExtensions
{
    /// <summary>
    /// 以整数形式指定自定义错误码。
    /// </summary>
    /// <param name="rule">当前验证规则</param>
    /// <param name="errorCode">整数错误码</param>
    /// <returns></returns>
    public static IRuleBuilderOptions<T, TProperty> WithErrorCode<T, TProperty>(
        this IRuleBuilderOptions<T, TProperty> rule, int errorCode)
    {
        return rule.WithErrorCode(errorCode.ToString());
    }
}
