using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace Raindrop.AspNetCore.Filters;

/// <summary>
/// MVC 结果包装过滤器。
/// 将普通 ObjectResult 和 EmptyResult 包装为统一的 ApiResponse。
/// </summary>
public sealed class ApiResponseResultFilter : IResultFilter
{
    public void OnResultExecuting(ResultExecutingContext context)
    {
        switch (context.Result)
        {
            case ObjectResult objectResult when ShouldWrap(objectResult.Value):
                objectResult.Value = ApiResponse.Success(
                    objectResult.Value,
                    objectResult.DeclaredType,
                    ApiMessages.Success);
                break;
            case EmptyResult:
                context.Result = new ObjectResult(ApiResponse.Success<object>(null, ApiMessages.Success));
                break;
        }
    }

    public void OnResultExecuted(ResultExecutedContext context)
    {
    }

    private static bool ShouldWrap(object? value)
    {
        if (value is null)
            return true;

        var type = value.GetType();
        return !(type.IsGenericType && type.GetGenericTypeDefinition() == typeof(ApiResponse<>));
    }
}
