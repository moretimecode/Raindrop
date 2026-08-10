using System.ComponentModel;

namespace Raindrop.Utility.Extensions;

public static class EnumExtensions
{
    /// <summary>
    /// 获取枚举的列表表示，包含名称、值和 Description 特性内容。
    /// </summary>
    /// <param name="enumType">枚举类型。</param>
    /// <param name="excludeZero">是否排除值为 0 的成员。</param>
    public static List<ListItem> GetItemList(this Type enumType, bool excludeZero = true)
    {
        var list = new List<ListItem>();

        foreach (var field in enumType.GetFields())
        {
            // 跳过编译器生成的嵌套类型成员（如通过 switch 表达式生成的值）
            if (field.IsSpecialName)
                continue;

            var name = field.Name;
            var value = (int)Enum.Parse(enumType, name);

            if (excludeZero && value == 0)
                continue;

            var description = field
                .GetCustomAttributes(typeof(DescriptionAttribute), false)
                .OfType<DescriptionAttribute>()
                .FirstOrDefault()?.Description ?? string.Empty;

            list.Add(new ListItem
            {
                Name = name,
                Value = value.ToString(),
                Description = description
            });
        }

        return list;
    }
}
