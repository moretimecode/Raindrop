using FluentValidation;

namespace Raindrop.CQRS.FluentValidation.Tests;

/// <summary>
/// FluentValidatorOptionExtensions 单元测试。
/// </summary>
public class FluentValidatorOptionExtensionsTests
{
    private sealed class TestModel { public string Name { get; set; } = ""; }

    private sealed class TestValidator : AbstractValidator<TestModel>
    {
        public TestValidator()
        {
            RuleFor(x => x.Name)
                .NotEmpty()
                .WithErrorCode(10001);  // 扩展方法：int → string
        }
    }

    [Fact]
    public void WithErrorCode_Int_ConvertsToString()
    {
        var validator = new TestValidator();

        var result = validator.Validate(new TestModel { Name = "" });

        Assert.False(result.IsValid);
        Assert.Single(result.Errors);
        Assert.Equal("10001", result.Errors[0].ErrorCode);
    }

    [Fact]
    public void WithErrorCode_Int_ValidatesAsInteger()
    {
        var validator = new TestValidator();
        var result = validator.Validate(new TestModel { Name = "" });

        var parsed = int.TryParse(result.Errors[0].ErrorCode, out var code);

        Assert.True(parsed);
        Assert.Equal(10001, code);
    }
}
