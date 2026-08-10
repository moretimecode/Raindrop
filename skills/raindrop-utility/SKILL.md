---
name: raindrop-utility
description: Raindrop 通用工具。当用户需要 FriendlyException 业务异常、字符串/字典/枚举/时间/集合等扩展方法时使用。
---

# Raindrop.Utility — 通用工具

## NuGet 包

```
Raindrop.Utility
```

所有 Raindrop 包共同依赖此包。

## FriendlyException — 业务异常

Raindrop 统一业务异常，由 `Raindrop.AspNetCore` 自动转为统一错误响应。

```csharp
// 带错误码
throw new FriendlyException("余额不足", 10001);
// → { "code": 10001, "message": "余额不足", "data": null, "success": false }

// 无错误码默认 400
throw new FriendlyException("参数错误");
```

CQRS ValidatorBehavior 和 HTTP ExceptionHandlingMiddleware 统一处理此异常。

## 扩展方法

| 类别 | 方法 | 说明 |
|------|------|------|
| String | `IsNullOrEmpty()` | 判断 null/空 |
| String | `ToSnakeCase()` | 下划线命名 |
| Dictionary | `TryGetOrAdd(key, factory)` | 获取或添加 |
| List | `ToPageableResult(pageable)` | 转分页结果 |
| Enum | `GetDescription()` | Description 特性值 |
| DateTime | `ToUnixTimestamp()` | 转时间戳 |
| Config | `GetSectionValue<T>()` | 强类型读取配置节 |
| SemaphoreSlim | `WaitAsync(ct)` | 简化 token 等待 |
| Queryable | `OrderBy(propertyName, desc)` | 动态排序 |
| Convert | `To<T>(obj)` / `ToList<T>(obj)` | 类型转换 |

## AsyncDisposable

```csharp
public abstract class AsyncDisposable : IAsyncDisposable
{
    protected virtual ValueTask DisposeAsyncCore() => ValueTask.CompletedTask;
    // 标准 Dispose 模式
}
```

## DateTimePeriod

```csharp
var period = new DateTimePeriod(start, end);
bool contains = period.Contains(dateTime);
bool overlaps = period.Overlaps(other);
```

## 使用原则

- 不引入重量级依赖，只放最通用的工具
- `FriendlyException` 是唯一业务异常类型
- 所有 Raindrop 包隐式引用此包
