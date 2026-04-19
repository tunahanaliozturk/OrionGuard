namespace Moongazing.OrionGuard.AspNetCore.Attributes;

/// <summary>
/// Marks an endpoint or controller for automatic OrionGuard validation.
/// When applied, the OrionGuard MVC filter will resolve and execute
/// the registered <c>IValidator&lt;TRequest&gt;</c> for the request model.
/// </summary>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class, AllowMultiple = false, Inherited = true)]
public sealed class ValidateRequestAttribute : Attribute;
