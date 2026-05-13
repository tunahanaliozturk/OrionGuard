using Moongazing.OrionGuard.Domain.Events;

namespace Moongazing.OrionGuard.Testing.DomainEvents;

/// <summary>Fluent assertions over a <see cref="DomainEventCapture"/>.</summary>
public sealed class DomainEventAssertions
{
    private readonly DomainEventCapture capture;

    internal DomainEventAssertions(DomainEventCapture capture) => this.capture = capture;

    /// <summary>Asserts that at least one event of <typeparamref name="TEvent"/> matching <paramref name="predicate"/> was captured.</summary>
    public DomainEventAssertions HaveRaised<TEvent>(Func<TEvent, bool>? predicate = null)
        where TEvent : IDomainEvent
    {
        var match = predicate is null ? capture.Contains<TEvent>() : capture.Contains(predicate);
        if (!match)
            throw new DomainEventAssertionException(
                $"Expected {typeof(TEvent).Name} to be raised but it was not. " +
                $"Captured events: [{FormatCaptured()}]");
        return this;
    }

    /// <summary>Asserts that no event of <typeparamref name="TEvent"/> was captured.</summary>
    public DomainEventAssertions NotHaveRaised<TEvent>() where TEvent : IDomainEvent
    {
        if (capture.Contains<TEvent>())
            throw new DomainEventAssertionException(
                $"Expected {typeof(TEvent).Name} NOT to be raised but it was. " +
                $"Captured events: [{FormatCaptured()}]");
        return this;
    }

    /// <summary>Begins a count-then-type fluent assertion (e.g. <c>HaveRaisedExactly(1).Of&lt;X&gt;()</c>).</summary>
    public CountAssertion HaveRaisedExactly(int expected) => new(this, capture, expected);

    private string FormatCaptured()
        => string.Join(", ", capture.All.Select(e => e.GetType().Name));

    /// <summary>Continuation that pairs an expected count with an event type.</summary>
    public sealed class CountAssertion
    {
        private readonly DomainEventAssertions parent;
        private readonly DomainEventCapture capture;
        private readonly int expected;

        internal CountAssertion(DomainEventAssertions parent, DomainEventCapture capture, int expected)
        {
            this.parent = parent;
            this.capture = capture;
            this.expected = expected;
        }

        /// <summary>Specifies the event type whose captured count must equal the previously supplied number.</summary>
        public DomainEventAssertions Of<TEvent>() where TEvent : IDomainEvent
        {
            var actual = capture.OfType<TEvent>().Count();
            if (actual != expected)
                throw new DomainEventAssertionException(
                    $"Expected exactly {expected} {typeof(TEvent).Name} event(s), but found {actual}.");
            return parent;
        }
    }
}
