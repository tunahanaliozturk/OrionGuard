# Moongazing.OrionGuard.Grpc

gRPC integration for [**OrionGuard**](https://github.com/tunahanaliozturk/OrionGuard). Adds a server-side interceptor that validates every incoming protobuf message with your OrionGuard validators before the service method runs.

## What this package adds

- **`OrionGuardInterceptor`** — a `grpc-dotnet` `Interceptor` that resolves `IValidator<TRequest>` from DI and runs it against every unary, client-streaming, server-streaming, and duplex call.
- **Status code translation** — a validation failure becomes `Status.FailedPrecondition` (`FAILED_PRECONDITION`, numeric `9`) with structured trailers carrying the field errors.
- **DI helper** — `AddOrionGuardGrpc()` registers the interceptor singleton and scans your assemblies for validators.

## Install

```bash
dotnet add package Moongazing.OrionGuard.Grpc
```

Requires `Grpc.AspNetCore` in your application. The core `OrionGuard` package is brought in transitively.

## Quick start

```csharp
using Moongazing.OrionGuard.Grpc;

builder.Services.AddOrionGuardGrpc();

builder.Services.AddGrpc(options =>
{
    options.Interceptors.Add<OrionGuardInterceptor>();
});

// Any IValidator<TRequest> registered in DI is applied automatically:
public sealed class CreateUserValidator : AbstractValidator<CreateUserRequest>
{
    public CreateUserValidator()
    {
        RuleFor(x => x.Email, "Email", p => p.NotEmpty().Email());
    }
}
```

## Targets

.NET 8.0, .NET 9.0, .NET 10.0.

## License

MIT. See the [main repository](https://github.com/tunahanaliozturk/OrionGuard) for full docs, CHANGELOG, and samples.
