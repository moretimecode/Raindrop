using System.Reflection;

namespace Raindrop.AspNetCore;

/// <summary>
/// 统一 API 响应模型。
/// </summary>
public sealed class ApiResponse<T>
{
    public int Code { get; init; }

    public string Message { get; init; } = string.Empty;

    public T? Data { get; init; }

    public bool Success { get; init; }
}

/// <summary>
/// 统一 API 响应工厂。
/// </summary>
public static class ApiResponse
{
    private static readonly MethodInfo CreateSuccessMethod =
        typeof(ApiResponse).GetMethod(nameof(CreateSuccess), BindingFlags.NonPublic | BindingFlags.Static)!;

    public static ApiResponse<T> Success<T>(T? data, string message = "success")
    {
        return new ApiResponse<T>
        {
            Code = 200,
            Message = message,
            Data = data,
            Success = true
        };
    }

    public static ApiResponse<object> Fail(int code, string message, object? data = null)
    {
        return Fail<object>(code, message, data);
    }

    public static ApiResponse<T> Fail<T>(int code, string message, T? data = default)
    {
        return new ApiResponse<T>
        {
            Code = code,
            Message = message,
            Data = data,
            Success = false
        };
    }

    internal static object Success(object? data, Type? dataType, string message = "success")
    {
        dataType ??= data?.GetType() ?? typeof(object);
        return CreateSuccessMethod
            .MakeGenericMethod(dataType)
            .Invoke(null, [data, message])!;
    }

    private static ApiResponse<T> CreateSuccess<T>(object? data, string message)
    {
        return Success((T?)data, message);
    }
}
