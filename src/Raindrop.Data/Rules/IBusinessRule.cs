namespace Raindrop.Data.Rules;

/// <summary>
/// 业务规则契约，规则对象负责判断自己是否被破坏。
/// </summary>
public interface IBusinessRule
{
    string ErrorMessage { get; }

    int ErrorCode { get; }

    bool IsBroken();
}
