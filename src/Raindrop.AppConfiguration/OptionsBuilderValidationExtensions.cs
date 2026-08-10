using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Raindrop.AppConfiguration;

public static class OptionsBuilderValidationExtensions
{
    /// <summary>
    /// 注册递归 DataAnnotations 校验，对嵌套对象的验证约束也会进行检查。
    /// </summary>
    /// <typeparam name="TOptions">要配置的选项类型。</typeparam>
    /// <param name="optionsBuilder">要添加校验的选项构建器。</param>
    /// <returns>可用于链式调用的 <see cref="OptionsBuilder{TOptions}"/>。</returns>
    public static OptionsBuilder<TOptions> ValidateDataAnnotationsRecursively<TOptions>(
        this OptionsBuilder<TOptions> optionsBuilder) where TOptions : class
    {
        ArgumentNullException.ThrowIfNull(optionsBuilder);

        optionsBuilder.Services.AddSingleton<IValidateOptions<TOptions>>(
            new DataAnnotationsValidateRecursiveOptions<TOptions>(optionsBuilder.Name));

        return optionsBuilder;
    }
}
