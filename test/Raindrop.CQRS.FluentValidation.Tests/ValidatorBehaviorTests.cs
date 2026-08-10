using FluentValidation;
using FluentValidation.Results;
using MediatR;
using Raindrop.CQRS.Behaviors;
using Raindrop.CQRS.Commands;
using Raindrop.CQRS.Context;
using Raindrop.CQRS.FluentValidation;
using Raindrop.CQRS.FluentValidation.Behaviors;
using Raindrop.Utility;

namespace Raindrop.CQRS.FluentValidation.Tests;

/// <summary>
/// ValidatorBehavior 单元测试。
/// </summary>
public class ValidatorBehaviorTests
{
    // ===================================================================
    //  验证通过
    // ===================================================================

    [Fact]
    public async Task ValidRequest_InvokesNext()
    {
        var accessor = new PipelineContextAccessor();
        var failureHandler = new StubFailureHandler();
        var validator = new AlwaysValidValidator();
        var behavior = new ValidatorBehavior<ValidCmd, string>(
            [validator], accessor, failureHandler);

        var response = await behavior.Handle(new ValidCmd { Name = "test" },
            ct => Task.FromResult("ok"),
            CancellationToken.None);

        Assert.Equal("ok", response);
        Assert.False(failureHandler.HandleCalled);
    }

    // ===================================================================
    //  验证失败
    // ===================================================================

    [Fact]
    public async Task InvalidRequest_CallsFailureHandler()
    {
        var accessor = new PipelineContextAccessor();
        var failureHandler = new StubFailureHandler();
        var validator = new AlwaysInvalidValidator();
        var behavior = new ValidatorBehavior<InvalidCmd, string>(
            [validator], accessor, failureHandler);

        await behavior.Handle(new InvalidCmd { Name = "" },
            ct => Task.FromResult("never"),
            CancellationToken.None);

        Assert.True(failureHandler.HandleCalled);
        Assert.Single(failureHandler.ReceivedErrors!);
    }

    [Fact]
    public async Task InvalidRequest_ThrowsFromFailureHandler()
    {
        var accessor = new PipelineContextAccessor();
        var throwingHandler = new ThrowingFailureHandler();
        var validator = new AlwaysInvalidValidator();
        var behavior = new ValidatorBehavior<InvalidCmd, string>(
            [validator], accessor, throwingHandler);

        var ex = await Assert.ThrowsAsync<FriendlyException>(
            () => behavior.Handle(new InvalidCmd { Name = "" },
                ct => Task.FromResult("never"),
                CancellationToken.None));

        Assert.Equal(400, ex.Code);
    }

    // ===================================================================
    //  多个 Validator
    // ===================================================================

    [Fact]
    public async Task MultipleValidators_CollectsAllErrors()
    {
        var accessor = new PipelineContextAccessor();
        var failureHandler = new StubFailureHandler();
        var validators = new IValidator<InvalidCmd>[]
        {
            new AlwaysInvalidValidator(),
            new AlwaysInvalidValidator()
        };
        var behavior = new ValidatorBehavior<InvalidCmd, string>(
            validators, accessor, failureHandler);

        await behavior.Handle(new InvalidCmd { Name = "" },
            ct => Task.FromResult("never"),
            CancellationToken.None);

        Assert.True(failureHandler.HandleCalled);
        Assert.Equal(2, failureHandler.ReceivedErrors!.Count);
    }

    [Fact]
    public async Task AsyncValidatorRule_IsSupported()
    {
        var accessor = new PipelineContextAccessor();
        var failureHandler = new StubFailureHandler();
        var validator = new AsyncInvalidValidator();
        var behavior = new ValidatorBehavior<AsyncInvalidCmd, string>(
            [validator], accessor, failureHandler);

        await behavior.Handle(new AsyncInvalidCmd { Name = "" },
            ct => Task.FromResult("never"),
            CancellationToken.None);

        Assert.True(failureHandler.HandleCalled);
        Assert.Single(failureHandler.ReceivedErrors!);
    }

    // ===================================================================
    //  设置请求上下文
    // ===================================================================

    [Fact]
    public async Task ValidatorBehavior_SetsRequestOnContext()
    {
        var accessor = new PipelineContextAccessor();
        var failureHandler = new StubFailureHandler();
        var validator = new AlwaysValidValidator();
        var behavior = new ValidatorBehavior<ValidCmd, string>(
            [validator], accessor, failureHandler);

        var request = new ValidCmd { Name = "test" };
        await behavior.Handle(request,
            ct => Task.FromResult("ok"),
            CancellationToken.None);

        Assert.Same(request, accessor.Context.Request);
    }

    // ===================================================================
    //  测试类型
    // ===================================================================

    private sealed record ValidCmd : ICommand<string> { public string Name { get; init; } = ""; }
    private sealed record InvalidCmd : ICommand<string> { public string Name { get; init; } = ""; }
    private sealed record AsyncInvalidCmd : ICommand<string> { public string Name { get; init; } = ""; }

    private sealed class AlwaysValidValidator : AbstractValidator<ValidCmd>
    {
        public AlwaysValidValidator()
        {
            RuleFor(x => x.Name).NotEmpty();
        }
    }

    private sealed class AlwaysInvalidValidator : AbstractValidator<InvalidCmd>
    {
        public AlwaysInvalidValidator()
        {
            RuleFor(x => x.Name).NotEmpty().WithMessage("Name 不能为空");
        }
    }

    private sealed class AsyncInvalidValidator : AbstractValidator<AsyncInvalidCmd>
    {
        public AsyncInvalidValidator()
        {
            RuleFor(x => x.Name)
                .MustAsync((_, _) => Task.FromResult(false))
                .WithMessage("Name 异步校验失败");
        }
    }

    private sealed class StubFailureHandler : IValidationFailureHandler
    {
        public bool HandleCalled { get; private set; }
        public List<ValidationFailure>? ReceivedErrors { get; private set; }

        public void Handle(List<ValidationFailure> errors)
        {
            HandleCalled = true;
            ReceivedErrors = errors;
        }
    }

    private sealed class ThrowingFailureHandler : IValidationFailureHandler
    {
        public void Handle(List<ValidationFailure> errors)
        {
            throw new FriendlyException("验证失败", 400);
        }
    }
}
