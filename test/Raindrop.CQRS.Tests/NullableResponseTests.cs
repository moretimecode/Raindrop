using Raindrop.CQRS;

namespace Raindrop.CQRS.Tests;

/// <summary>
/// NullableResponse 单元测试。
/// </summary>
public class NullableResponseTests
{
    [Fact]
    public void Value_ReturnsSameInstance()
    {
        var a = NullableResponse.Value;
        var b = NullableResponse.Value;

        Assert.True(a.Equals(b));
    }

    [Fact]
    public async Task Task_ReturnsCompletedTaskWithValue()
    {
        var result = await NullableResponse.Task;

        Assert.Equal(NullableResponse.Value, result);
    }

    [Fact]
    public void Value_IsDefaultStruct()
    {
        var v = NullableResponse.Value;

        Assert.Equal(v, NullableResponse.Value);
    }
}
