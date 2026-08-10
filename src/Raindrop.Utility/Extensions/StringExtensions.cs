using System.Text;

namespace Raindrop.Utility.Extensions;

public static class StringExtensions
{
    public static StringBuilder AppendIf(this StringBuilder sb, bool condition, string value)
    {
        if (condition && value != null)
        {
            return sb.Append(value);
        }
        return sb;
    }

    public static StringBuilder AppendIf(this StringBuilder sb, bool condition, ReadOnlySpan<char> value)
    {
        if (condition)
        {
            return sb.Append(value);
        }
        return sb;
    }

    public static StringBuilder AppendIf(this StringBuilder sb, bool condition, Func<string> valueFactory)
    {
        if (condition)
        {
            var value = valueFactory.Invoke();
            if (value != null)
            {
                return sb.Append(value);
            }
        }
        return sb;
    }
}