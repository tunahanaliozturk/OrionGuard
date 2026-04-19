using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Jobs;
using Moongazing.OrionGuard.Domain.Events;
using Moongazing.OrionGuard.Domain.Primitives;

namespace Moongazing.OrionGuard.Benchmarks;

/// <summary>
/// Measures DDD primitives: ValueObject equality (abstract class vs record marker) and
/// AggregateRoot event raise/pull overhead.
/// </summary>
[MemoryDiagnoser]
[SimpleJob(RuntimeMoniker.Net80)]
[SimpleJob(RuntimeMoniker.Net90)]
public class DomainPrimitivesBenchmark
{
    private sealed class Money : ValueObject
    {
        public decimal Amount { get; }
        public string Currency { get; }

        public Money(decimal amount, string currency)
        {
            Amount = amount;
            Currency = currency;
        }

        protected override IEnumerable<object?> GetEqualityComponents()
        {
            yield return Amount;
            yield return Currency;
        }
    }

    private sealed record RecordMoney(decimal Amount, string Currency) : IValueObject;

    private sealed record OrderShippedEvent : IDomainEvent
    {
        public Guid EventId { get; } = Guid.NewGuid();
        public DateTime OccurredOnUtc { get; } = DateTime.UtcNow;
    }

    private sealed class Order : AggregateRoot<int>
    {
        public Order() : base(1) { }
        public void Ship() => RaiseEvent(new OrderShippedEvent());
    }

    private Money _a = null!;
    private Money _b = null!;
    private RecordMoney _ra = null!;
    private RecordMoney _rb = null!;
    private Order _order = null!;

    [GlobalSetup]
    public void Setup()
    {
        _a = new Money(100m, "TRY");
        _b = new Money(100m, "TRY");
        _ra = new RecordMoney(100m, "TRY");
        _rb = new RecordMoney(100m, "TRY");
        _order = new Order();
    }

    [Benchmark(Baseline = true)]
    public bool ValueObject_ClassEquality() => _a.Equals(_b);

    [Benchmark]
    public bool ValueObject_RecordEquality() => _ra.Equals(_rb);

    [Benchmark]
    public int AggregateRoot_RaiseAndPull()
    {
        _order.Ship();
        return _order.PullDomainEvents().Count;
    }
}
