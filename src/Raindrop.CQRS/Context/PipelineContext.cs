namespace Raindrop.CQRS.Context;

/// <summary>
/// 管道上下文。在一次 Bus 调用期间，在 Pipeline Behavior 链中传递请求、响应与自定义数据。
/// </summary>
public class PipelineContext
{
    /// <summary>
    /// 当前请求对象。
    /// </summary>
    public object? Request { get; set; }

    /// <summary>
    /// 当前响应对象。
    /// </summary>
    public object? Response { get; set; }

    /// <summary>
    /// 自定义键值数据存储。
    /// </summary>
    protected Dictionary<object, object?> Properties { get; private set; } = [];

    /// <summary>
    /// 只读数据视图。
    /// </summary>
    public IReadOnlyDictionary<object, object?> Data => Properties;

    protected PipelineContext()
    {
    }

    /// <summary>
    /// 创建一个新的空上下文。
    /// </summary>
    public static PipelineContext CreateNew()
    {
        return new PipelineContext();
    }

    /// <summary>
    /// 清空所有状态。
    /// </summary>
    public void Clear()
    {
        Request = null;
        Response = null;
        Properties.Clear();
    }

    /// <summary>
    /// 设置请求对象。
    /// </summary>
    public void SetRequest(object request)
    {
        Request = request;
    }

    /// <summary>
    /// 设置响应对象。
    /// </summary>
    public void SetResponse(object response)
    {
        Response = response;
    }

    /// <summary>
    /// 写入自定义数据。如果 key 已存在且 isOverWrite 为 false，则忽略。
    /// </summary>
    public void SetData(object key, object value, bool isOverWrite = false)
    {
        if (isOverWrite)
        {
            Properties.Remove(key);
        }

        Properties.TryAdd(key, value);
    }

    /// <summary>
    /// 读取自定义数据。key 不存在时返回 null。
    /// </summary>
    public object? GetData(object key)
    {
        return Properties.TryGetValue(key, out var data) ? data : null;
    }
}
