namespace Moongazing.OrionGuard.Core;

/// <summary>
/// Marker interface for request validators used in pipeline integration
/// (ASP.NET Core middleware, MediatR behaviors, etc.).
/// </summary>
public interface IRequestValidator<in TRequest>
{
    GuardResult Validate(TRequest request);
    Task<GuardResult> ValidateAsync(TRequest request, CancellationToken cancellationToken = default);
}
