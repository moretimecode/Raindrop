using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Raindrop.AppConfiguration.Abstractions;

namespace Raindrop.AppConfiguration;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// 注册 <typeparamref name="TOptions"/> 到 <see cref="IAppConfigurationManager{TOptions}"/> 模式中。
    /// 如果 <typeparamref name="TOptions"/> 继承自 <see cref="BaseAppConfiguration{TOptions}"/>，
    /// 则自动调用 <see cref="BaseAppConfiguration{TOptions}.Bind"/>；否则使用 <see cref="IConfiguration.Bind(TOptions)"/>。
    /// </summary>
    public static OptionsBuilder<TOptions> AddAppConfiguration<TOptions>(
        this IServiceCollection services,
        IConfiguration config) where TOptions : class
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(config);

        return services.AddAppConfiguration<TOptions>(config, BindOptions);
    }

    /// <summary>
    /// 注册 <typeparamref name="TOptions"/> 到 <see cref="IAppConfigurationManager{TOptions}"/> 模式中，
    /// 使用指定的 <paramref name="configureOptions"/> 委托进行配置绑定。
    /// </summary>
    public static OptionsBuilder<TOptions> AddAppConfiguration<TOptions>(
        this IServiceCollection services,
        IConfiguration config,
        Action<TOptions, IConfiguration> configureOptions) where TOptions : class
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(configureOptions);

        // 注册错误通知处理器，失败时写入日志
        services.AddConfigLoadErrorNotification<TOptions>(
            (sp, ex) =>
            {
                var logger = sp.GetRequiredService<ILogger<ConfigLoadErrorNotification<TOptions>>>();
                logger.LogError(ex, "配置加载失败，类型: {OptionsType}", typeof(TOptions).Name);
            }
        );

        services.AddSingleton<IAppConfigurationManager<TOptions>, AppConfigurationManager<TOptions>>();

        services.AddOptions();
        services.AddSingleton<IOptionsChangeTokenSource<TOptions>>(
            new ConfigurationChangeTokenSource<TOptions>(Options.DefaultName, config));

        return services.AddOptions<TOptions>()
            .Configure(o => configureOptions(o, config))
            .ValidateDataAnnotationsRecursively()
            .ValidateOnStart();
    }

    /// <summary>
    /// 注册自定义配置加载错误通知处理器。
    /// </summary>
    public static IServiceCollection AddConfigLoadErrorNotification<TOptions>(
        this IServiceCollection services,
        Action<IServiceProvider, Exception> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);

        services.AddSingleton<IConfigLoadErrorNotification<TOptions>>(
            sp => new ConfigLoadErrorNotification<TOptions>(sp, handler));
        return services;
    }

    private static void BindOptions<TOptions>(TOptions options, IConfiguration config) where TOptions : class
    {
        // 如果 TOptions 继承自 BaseAppConfiguration<T>，通过反射调用其 Bind 方法
        var optionsType = options.GetType();
        var baseType = optionsType.BaseType;

        if (baseType is { IsGenericType: true } &&
            baseType.GetGenericTypeDefinition() == typeof(BaseAppConfiguration<>))
        {
            var bindMethod = baseType.GetMethod("Bind")!;
            bindMethod.Invoke(options, [config]);
        }
        else
        {
            config.Bind(options);
        }
    }
}
