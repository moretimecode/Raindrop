using Raindrop.Data.Rules;

namespace Raindrop.Data.Entities;

public static class EntityBusinessRuleExtensions
{
    public static void CheckRule(this IEntity entity, IBusinessRule rule)
    {
        ArgumentNullException.ThrowIfNull(entity);
        ArgumentNullException.ThrowIfNull(rule);

        if (rule.IsBroken())
        {
            throw new BusinessRuleValidationException(rule);
        }
    }
}
