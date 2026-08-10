using System.ComponentModel.DataAnnotations;
using Microsoft.Extensions.Options;

namespace Raindrop.AppConfiguration;

/// <summary>
/// 使用 DataAnnotations 对命名选项实例进行递归验证。
/// </summary>
public class DataAnnotationsValidateRecursiveOptions<TOptions>(string? name) : IValidateOptions<TOptions> where TOptions : class
{
    /// <summary>
    /// 要验证的选项名称，为 null 时验证所有命名实例。
    /// </summary>
    public string? Name { get; } = name;

    /// <summary>
    /// 验证指定名称的选项实例（name 为 null 时验证所有实例）。
    /// </summary>
    /// <param name="name">要验证的选项实例名称。</param>
    /// <param name="options">选项实例。</param>
    /// <returns><see cref="ValidateOptionsResult"/> 结果。</returns>
    public ValidateOptionsResult Validate(string? name, TOptions options)
    {
        if (Name is null || name == Name)
        {
            var validator = new DataAnnotationsValidator();
            var validationResults = new List<ValidationResult>();
            if (validator.TryValidateObjectRecursive(options, validationResults))
            {
                return ValidateOptionsResult.Success;
            }

            return ValidateOptionsResult.Fail(string.Join(
                Environment.NewLine,
                validationResults.Select(r =>
                    "DataAnnotation 验证失败，成员 '" +
                    string.Join(", ", r.MemberNames) +
                    "'，错误信息: '" + r.ErrorMessage + "'。")));
        }

        return ValidateOptionsResult.Skip;
    }
}
