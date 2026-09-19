using global::MassTransit;
using Moongazing.OrionGuard.Core;
using Moongazing.OrionGuard.DependencyInjection;

namespace Moongazing.OrionGuard.MassTransit;

/// <summary>
/// MassTransit consume filter that runs every <see cref="IValidator{T}"/> registered for
/// <typeparamref name="TMessage"/> before the message reaches its consumer. When any validator reports
/// an error the filter throws <see cref="MessageValidationException"/> and the consumer never runs;
/// MassTransit's retry and error pipeline then handle the fault, which by default moves the message to
/// the endpoint's <c>_error</c> queue. Message types with no registered validator pass through.
/// </summary>
/// <remarks>
/// <para>
/// <b>Scoped.</b> Register it with <c>UseOrionGuardValidation(context)</c>, which adds it as a MassTransit
/// scoped filter. MassTransit creates the filter from the consume scope, the same scope the consumer is
/// resolved from, so scoped validators and validators with scoped dependencies (a <c>DbContext</c>, for
/// example) get the lifetime of the message being consumed.
/// </para>
/// <para>
/// <b>Why validators are resolved for <typeparamref name="TMessage"/>, not the runtime type.</b>
/// MassTransit deserializes an interface message contract into a generated implementation type, so
/// <c>context.Message.GetType()</c> is not the contract type. A lookup keyed on the runtime type, such as
/// <see cref="ValidatorInvoker"/>, would find no validator and let an invalid message through.
/// <typeparamref name="TMessage"/> is always the consumed contract type, so validators registered for it
/// are found.
/// </para>
/// </remarks>
/// <typeparam name="TMessage">The consumed message type.</typeparam>
public sealed class OrionGuardConsumeFilter<TMessage> : IFilter<ConsumeContext<TMessage>>
    where TMessage : class
{
    private readonly IEnumerable<IValidator<TMessage>> validators;

    /// <summary>Initializes a new instance of the <see cref="OrionGuardConsumeFilter{TMessage}"/> class.</summary>
    /// <param name="validators">The validators registered for <typeparamref name="TMessage"/>, resolved from the consume scope.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="validators"/> is <see langword="null"/>.</exception>
    public OrionGuardConsumeFilter(IEnumerable<IValidator<TMessage>> validators)
    {
        this.validators = validators ?? throw new ArgumentNullException(nameof(validators));
    }

    /// <summary>
    /// Validates <c>context.Message</c> and, when it is valid, passes the context to the next filter.
    /// </summary>
    /// <param name="context">The consume context.</param>
    /// <param name="next">The rest of the consume pipe, ending with the consumer.</param>
    /// <exception cref="MessageValidationException">Thrown when one or more validators report an error.</exception>
    public async Task Send(ConsumeContext<TMessage> context, IPipe<ConsumeContext<TMessage>> next)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(next);

        List<ValidationError>? errors = null;

        // Why: validators run one after another, not concurrently. Validators resolved from the same
        // scope can share scoped dependencies such as a DbContext, which does not allow concurrent use.
        foreach (var validator in validators)
        {
            var result = await validator
                .ValidateAsync(context.Message, context.CancellationToken)
                .ConfigureAwait(false);

            if (result.IsInvalid)
            {
                (errors ??= new List<ValidationError>()).AddRange(result.Errors);
            }
        }

        if (errors is not null)
        {
            throw new MessageValidationException(errors, typeof(TMessage));
        }

        await next.Send(context).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public void Probe(ProbeContext context)
    {
        context.CreateFilterScope("orionGuardValidation");
    }
}
