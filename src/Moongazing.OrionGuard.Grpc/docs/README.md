# OrionGuard.Grpc

A grpc-dotnet server interceptor for [OrionGuard](https://github.com/tunahanaliozturk/OrionGuard). It validates incoming request messages with your `IValidator<TRequest>` registrations and rejects invalid ones with `StatusCode.InvalidArgument`.

## Install

```bash
dotnet add package OrionGuard.Grpc
```

The package depends on `Grpc.AspNetCore.Server`, and the core `OrionGuard` package is installed as a dependency. Keep using `Grpc.AspNetCore` (or `Grpc.Tools` and `Google.Protobuf`) for code generation.

## Quick start

```csharp
using Moongazing.OrionGuard.DependencyInjection;
using Moongazing.OrionGuard.Grpc;

builder.Services.AddOrionGuardGrpc();
builder.Services.AddValidator<CreateUserRequest, CreateUserRequestValidator>();
builder.Services.AddGrpc(options => options.Interceptors.Add<OrionGuardInterceptor>());

// CreateUserRequest is the protobuf-generated message.
public sealed class CreateUserRequestValidator : AbstractValidator<CreateUserRequest>
{
    public CreateUserRequestValidator()
    {
        RuleFor(x => x.Email, nameof(CreateUserRequest.Email), p => p.NotEmpty().Email());
    }
}
```

`AddOrionGuardGrpc()` only registers `OrionGuardInterceptor` as a singleton. You still add the interceptor in `AddGrpc` and register each validator yourself, because the package does not scan assemblies.

## Behaviour

- For unary and server-streaming calls, the request is validated before the service method runs.
- For client-streaming and duplex calls, each incoming message is validated as the service reads it (`MoveNext`). The call fails at the first invalid message, and messages read before it have already been processed.
- The interceptor calls the synchronous `IValidator<TRequest>.Validate`, so `RuleForAsync` rules do not run.
- Messages whose type has no registered validator pass through.
- The interceptor is a singleton and resolves validators from the root service provider. Validators must not be registered as scoped, and must not depend on scoped services. `AddValidator` registers them as transient.

## Error format

A failed validation throws `RpcException` with:

- status `InvalidArgument` (code 3)
- status detail: the error messages joined with `"; "`
- one trailer, `validation-errors-json`, holding a JSON array such as `[{"ParameterName":"Email","Message":"Email must be a valid email."}]`

Reading it on the client:

```csharp
using Grpc.Core;

try
{
    await client.CreateUserAsync(request);
}
catch (RpcException ex) when (ex.StatusCode == StatusCode.InvalidArgument)
{
    string? errorsJson = ex.Trailers.GetValue("validation-errors-json");
}
```

## Targets

- `net8.0`, `net9.0`, `net10.0`
- `Grpc.AspNetCore.Server` 2.83.0 or later

## Documentation

- [Repository and full documentation](https://github.com/tunahanaliozturk/OrionGuard)
- [Changelog](https://github.com/tunahanaliozturk/OrionGuard/blob/master/CHANGELOG.md)
- Related packages: [OrionGuard](https://www.nuget.org/packages/OrionGuard), [OrionGuard.OpenTelemetry](https://www.nuget.org/packages/OrionGuard.OpenTelemetry)

## License

MIT. See [LICENSE.txt](https://github.com/tunahanaliozturk/OrionGuard/blob/master/src/Moongazing.OrionGuard/docs/LICENSE.txt).
