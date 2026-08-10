using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using Raindrop.CQRS;
using Raindrop.CQRS.Bus;
using Raindrop.CQRS.Commands;
using Raindrop.CQRS.Context;
using Raindrop.CQRS.FluentValidation;
using Raindrop.CQRS.Queries;
using Raindrop.Utility;

namespace Raindrop.CQRS.FluentValidation.Tests;

/// <summary>
/// CQRS 与 FluentValidation 的真实 DI 注册测试。
/// </summary>
public class ServiceRegistrationTests
{
    [Fact]
    public async Task AddRaindropCQRS_WithValidation_RunsCommandQueryAndValidator()
    {
        var services = new ServiceCollection();
        services.AddRaindropCQRS([typeof(ServiceRegistrationTests).Assembly])
            .AddValidation();

        await using var provider = services.BuildServiceProvider();

        var commandBus = provider.GetRequiredService<ICommandBus>();
        var queryBus = provider.GetRequiredService<IQueryBus>();

        var commandResult = await commandBus.Send(new RegisteredCommand("ok"));
        var queryResult = await queryBus.Send(new RegisteredQuery(6));

        Assert.Equal("ok-handled", commandResult.Value);
        Assert.Equal(12, queryResult);

        var ex = await Assert.ThrowsAsync<FriendlyException>(
            () => commandBus.Send(new RegisteredCommand("")));

        Assert.Equal(400, ex.Code);
        Assert.Contains("Name 不能为空", ex.Message);
    }

    public sealed record RegisteredCommand(string Name) : ICommand<RegisteredCommandResult>;

    public sealed record RegisteredCommandResult(string Value);

    public sealed class RegisteredCommandHandler(IPipelineContextAccessor accessor)
        : CommandHandler<RegisteredCommand, RegisteredCommandResult>(accessor)
    {
        public override Task<RegisteredCommandResult> Handle(
            RegisteredCommand request,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(new RegisteredCommandResult($"{request.Name}-handled"));
        }
    }

    public sealed class RegisteredCommandValidator : AbstractValidator<RegisteredCommand>
    {
        public RegisteredCommandValidator()
        {
            RuleFor(x => x.Name).NotEmpty().WithMessage("Name 不能为空");
        }
    }

    public sealed record RegisteredQuery(int Value) : IQuery<int>;

    public sealed class RegisteredQueryHandler(IPipelineContextAccessor accessor)
        : QueryHandler<RegisteredQuery, int>(accessor)
    {
        public override Task<int> Handle(RegisteredQuery request, CancellationToken cancellationToken)
        {
            return Task.FromResult(request.Value * 2);
        }
    }
}
