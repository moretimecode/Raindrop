namespace Raindrop.Data.Rules;

/// <summary>
/// 业务规则被破坏时抛出的异常。
/// </summary>
public class BusinessRuleValidationException : Exception
{
    public BusinessRuleValidationException(IBusinessRule brokenRule)
        : base(brokenRule.ErrorMessage)
    {
        BrokenRule = brokenRule;
        ErrorCode = brokenRule.ErrorCode;
    }

    public IBusinessRule BrokenRule { get; }

    public int ErrorCode { get; }

    public string RuleName => BrokenRule.GetType().Name;
}
