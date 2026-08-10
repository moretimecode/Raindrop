using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Raindrop.AspNetCore.Filters;
using Raindrop.AspNetCore.Logging;
using Raindrop.AspNetCore.Middlewares;
using Raindrop.Observability.Abstractions;

namespace Microsoft.Extensions.DependencyInjection
{
    /// <summary>
    /// Raindrop ASP.NET Core 服务注册扩展。
    /// </summary>
    public static class RaindropAspNetCoreServiceCollectionExtensions
    {
        /// <summary>
        /// 注册 Raindrop ASP.NET Core 管道服务。
        /// </summary>
        /// <param name="services">IServiceCollection。</param>
        /// <param name="logResponseBodyOnSuccess">
        /// 是否在正常 200 响应时也记录响应体。默认 false，仅记录入参、耗时、状态码和错误信息。
        /// </param>
        public static IServiceCollection AddRaindropAspNetCore(
            this IServiceCollection services,
            bool logResponseBodyOnSuccess = false)
        {
            ArgumentNullException.ThrowIfNull(services);

            services.TryAddSingleton<IAccessLogWriter, LoggerAccessLogWriter>();
            services.TryAddSingleton(new AccessLogFilterConfig(logResponseBodyOnSuccess));

            services.Configure<MvcOptions>(options =>
            {
                options.Filters.Add<ApiResponseResultFilter>();
                options.Filters.Add<AccessLogFilter>();
            });

            return services;
        }
    }
}

namespace Microsoft.AspNetCore.Builder
{
    /// <summary>
    /// Raindrop ASP.NET Core 管道扩展。
    /// </summary>
    public static class RaindropAspNetCoreApplicationBuilderExtensions
    {
        public static IApplicationBuilder UseRaindropAspNetCore(this IApplicationBuilder app)
        {
            ArgumentNullException.ThrowIfNull(app);

            // AccessLog 必须在最外层，才能记录异常处理和状态码包装后的最终结果。
            app.UseMiddleware<AccessLogMiddleware>();
            app.UseMiddleware<RaindropExceptionHandlingMiddleware>();
            app.UseMiddleware<StatusCodeResponseMiddleware>();
            return app;
        }
    }
}
