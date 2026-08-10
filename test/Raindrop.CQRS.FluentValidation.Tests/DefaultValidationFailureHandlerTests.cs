using FluentValidation;
using FluentValidation.Results;
using Microsoft.Extensions.Options;
using Raindrop.CQRS.FluentValidation;
using Raindrop.Utility;

namespace Raindrop.CQRS.FluentValidation.Tests;

/// <summary>
/// DefaultValidationFailureHandler 单元测试。
/// </summary>
public class DefaultValidationFailureHandlerTests
{
    [Fact]
    public void Handle_SingleError_ThrowsFriendlyExceptionWithCorrectMessage()
    {
        var options = Options.Create(new CQRSValidationOptions
        {
            DefaultErrorCode = 400,
            ErrorMessageMaxLength = 0
        });
        var handler = new DefaultValidationFailureHandler(options);

        var errors = new List<ValidationFailure>
        {
            new("Name", "Name 不能为空") { ErrorCode = "10001" }
        };

        var ex = Assert.Throws<FriendlyException>(() => handler.Handle(errors));

        Assert.Contains("Invalid arguments:", ex.Message);
        Assert.Contains("Name 不能为空", ex.Message);
        Assert.Equal(10001, ex.Code);
    }

    [Fact]
    public void Handle_MultipleErrors_ConcatenatesWithSemicolon()
    {
        var options = Options.Create(new CQRSValidationOptions
        {
            DefaultErrorCode = 400,
            ErrorMessageMaxLength = 0
        });
        var handler = new DefaultValidationFailureHandler(options);

        var errors = new List<ValidationFailure>
        {
            new("A", "错误A") { ErrorCode = "400" },
            new("B", "错误B") { ErrorCode = "400" }
        };

        var ex = Assert.Throws<FriendlyException>(() => handler.Handle(errors));

        Assert.Contains("错误A;", ex.Message);
        Assert.Contains("错误B;", ex.Message);
    }

    [Fact]
    public void Handle_ErrorCodeZero_UsesDefaultErrorCode()
    {
        var options = Options.Create(new CQRSValidationOptions
        {
            DefaultErrorCode = 422,
            ErrorMessageMaxLength = 0
        });
        var handler = new DefaultValidationFailureHandler(options);

        var errors = new List<ValidationFailure>
        {
            new("X", "invalid") { ErrorCode = "0" }
        };

        var ex = Assert.Throws<FriendlyException>(() => handler.Handle(errors));

        Assert.Equal(422, ex.Code);
    }

    [Fact]
    public void Handle_NoErrorCode_UsesDefaultErrorCode()
    {
        var options = Options.Create(new CQRSValidationOptions
        {
            DefaultErrorCode = 422,
            ErrorMessageMaxLength = 0
        });
        var handler = new DefaultValidationFailureHandler(options);

        var errors = new List<ValidationFailure>
        {
            new("X", "invalid") // ErrorCode 默认为 null
        };

        var ex = Assert.Throws<FriendlyException>(() => handler.Handle(errors));

        Assert.Equal(422, ex.Code);
    }

    [Fact]
    public void Handle_TruncatesMessageWhenExceedsMaxLength()
    {
        var options = Options.Create(new CQRSValidationOptions
        {
            DefaultErrorCode = 400,
            ErrorMessageMaxLength = 20
        });
        var handler = new DefaultValidationFailureHandler(options);

        var errors = new List<ValidationFailure>
        {
            new("X", "这是一条非常长的错误消息超过了限制长度")
        };

        var ex = Assert.Throws<FriendlyException>(() => handler.Handle(errors));

        // "Invalid arguments:" 前缀 + 截断后的错误消息
        Assert.True(ex.Message.Length <= "Invalid arguments:".Length + 20 + 1); // +1 for ;
    }

    [Fact]
    public void Handle_EmptyErrors_ThrowsFriendlyExceptionWithEmptyMessage()
    {
        var options = Options.Create(new CQRSValidationOptions
        {
            DefaultErrorCode = 400,
            ErrorMessageMaxLength = 0
        });
        var handler = new DefaultValidationFailureHandler(options);

        var ex = Assert.Throws<FriendlyException>(() => handler.Handle([]));

        // 空错误列表时只有前缀 "Invalid arguments: "（尾随空格）
        Assert.StartsWith("Invalid arguments:", ex.Message);
        Assert.Equal(400, ex.Code);
    }
}
