namespace Raindrop.AspNetCore;

/// <summary>
/// API 响应消息常量。替代 IOptions 配置模式。
/// </summary>
public static class ApiMessages
{
    public const string Success = "success";

    public const string InternalError = "服务器内部错误";

    public const string BadRequest = "请求参数不正确";

    public const string Unauthorized = "未认证或登录已失效";

    public const string Forbidden = "没有权限访问该资源";

    public const string NotFound = "接口不存在";

    public const string RequestCanceled = "请求已取消";
}
