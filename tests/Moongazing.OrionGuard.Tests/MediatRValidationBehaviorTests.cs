using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Moongazing.OrionGuard.Core;
using Moongazing.OrionGuard.DependencyInjection;
using Moongazing.OrionGuard.MediatR;

namespace Moongazing.OrionGuard.Tests;

public sealed class MediatRValidationBehaviorTests
{
    public sealed record ReserveCode(string Code) : IRequest<string>;

    public sealed record ListOrders(int PageSize) : IStreamRequest<int>;

    public sealed class ListOrdersHandler : IStreamRequestHandler<ListOrders, int>
    {
        public static int Runs;

        public async IAsyncEnumerable<int> Handle(
            ListOrders request,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref Runs);
            for (var i = 1; i <= request.PageSize; i++)
            {
                await Task.Yield();
                yield return i;
            }
        }
    }

    private sealed class FirstLookupValidator : AbstractValidator<ReserveCode>
    {
        public FirstLookupValidator(SingleOperationLookup lookup)
        {
            RuleForAsync(r => lookup.IsFreeAsync(r.Code), "Code is taken (first check).", "Code");
        }
    }

    private sealed class SecondLookupValidator : AbstractValidator<ReserveCode>
    {
        public SecondLookupValidator(SingleOperationLookup lookup)
        {
            RuleForAsync(r => lookup.IsFreeAsync(r.Code), "Code is taken (second check).", "Code");
        }
    }

    private sealed class PageSizeValidator : AbstractValidator<ListOrders>
    {
        public static int Runs;

        public PageSizeValidator()
        {
            RuleFor(r =>
            {
                Interlocked.Increment(ref Runs);
                return r.PageSize is > 0 and <= 100;
            }, "PageSize must be between 1 and 100.", "PageSize");
        }
    }

    [Fact]
    public async Task Handle_runs_validators_one_at_a_time_so_they_can_share_a_scoped_dependency()
    {
        var lookup = new SingleOperationLookup();
        var behavior = new ValidationBehavior<ReserveCode, string>(new IValidator<ReserveCode>[]
        {
            new FirstLookupValidator(lookup),
            new SecondLookupValidator(lookup),
        });

        var response = await behavior.Handle(new ReserveCode("free"), () => Task.FromResult("reserved"), CancellationToken.None);

        Assert.Equal("reserved", response);
    }

    [Fact]
    public async Task Handle_throws_AggregateValidationException_with_errors_from_every_validator()
    {
        var lookup = new SingleOperationLookup();
        var behavior = new ValidationBehavior<ReserveCode, string>(new IValidator<ReserveCode>[]
        {
            new FirstLookupValidator(lookup),
            new SecondLookupValidator(lookup),
        });

        var exception = await Assert.ThrowsAsync<AggregateValidationException>(
            () => behavior.Handle(new ReserveCode("taken"), () => Task.FromResult("reserved"), CancellationToken.None));

        Assert.Equal(
            new[] { "Code is taken (first check).", "Code is taken (second check)." },
            exception.Errors.Select(e => e.Message));
    }

    // One test covers both stream outcomes because the handler and validator counters are static.
    [Fact]
    public async Task CreateStream_validates_the_request_before_the_handler_yields()
    {
        var services = new ServiceCollection();
        services.AddMediatR(c => c.RegisterServicesFromAssemblyContaining<MediatRValidationBehaviorTests>());
        services.AddOrionGuardMediatR();
        services.AddValidator<ListOrders, PageSizeValidator>();
        await using var provider = services.BuildServiceProvider();
        var mediator = provider.GetRequiredService<IMediator>();
        ListOrdersHandler.Runs = 0;
        PageSizeValidator.Runs = 0;

        var exception = await Assert.ThrowsAsync<AggregateValidationException>(async () =>
        {
            await foreach (var _ in mediator.CreateStream(new ListOrders(0)))
            {
            }
        });

        Assert.Equal("PageSize must be between 1 and 100.", Assert.Single(exception.Errors).Message);
        Assert.Equal(0, ListOrdersHandler.Runs);

        var items = new List<int>();
        await foreach (var item in mediator.CreateStream(new ListOrders(3)))
        {
            items.Add(item);
        }

        Assert.Equal(new[] { 1, 2, 3 }, items);
        Assert.Equal(2, PageSizeValidator.Runs);
    }
}
