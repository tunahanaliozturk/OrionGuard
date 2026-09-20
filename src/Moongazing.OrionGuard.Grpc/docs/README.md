# OrionGuard.Grpc

A grpc-dotnet server interceptor that validates incoming messages with your `IValidator<T>` registrations and rejects a bad one with `StatusCode.InvalidArgument` before the service method runs.

```bash
dotnet add package OrionGuard.Grpc
```

```csharp
using Microsoft.Extensions.DependencyInjection;
using Moongazing.OrionGuard.DependencyInjection;
using Moongazing.OrionGuard.Grpc;

// protoc generates this from your .proto; shown here so the sample stands on its own.
public sealed class CreateUserRequest
{
    public string Email { get; set; } = string.Empty;
}

public sealed class CreateUserRequestValidator : AbstractValidator<CreateUserRequest>
{
    public CreateUserRequestValidator()
    {
        RuleFor(x => x.Email, nameof(CreateUserRequest.Email), p => p.NotEmpty().Email());
    }
}

public static class GrpcSetup
{
    public static void Add(IServiceCollection services)
    {
        services.AddOrionGuardGrpc();
        services.AddValidator<CreateUserRequest, CreateUserRequestValidator>();
        services.AddGrpc(options => options.Interceptors.Add<OrionGuardInterceptor>());
    }
}
```

A caller sending an empty `email` gets an `RpcException` with status `InvalidArgument` (code 3), the error messages joined with `"; "` as the status detail, and one trailer, `validation-errors-json`, holding a JSON array such as `[{"ParameterName":"Email","Message":"Email must be a valid email."}]`. The service method is not entered. The core `OrionGuard` package comes along as a dependency; keep using `Grpc.AspNetCore` (or `Grpc.Tools` with `Google.Protobuf`) for code generation.

## Reading the errors on the client

```csharp
using Grpc.Core;

public static class ClientSide
{
    public static string? ErrorsFrom(RpcException ex) =>
        ex.StatusCode == StatusCode.InvalidArgument
            ? ex.Trailers.GetValue("validation-errors-json")
            : null;
}
```

## What the interceptor does

- Unary and server-streaming calls: the request is validated before the service method runs.
- Client-streaming and duplex calls: each incoming message is validated as the service reads it (`MoveNext`).
- Every `IValidator<T>` registered for the message's runtime type runs through `ValidateAsync`, one after another, so `RuleForAsync` rules run and the errors are combined.
- Validators come from the call's request scope (`HttpContext.RequestServices`), so a scoped validator, or one holding a scoped `DbContext`, works.

`AddOrionGuardGrpc()` only registers `OrionGuardInterceptor` as a singleton; you add it to the pipeline in `AddGrpc` yourself, and register each validator yourself.

## What this does not do

- **It does not find your validators.** No assembly scanning; a message type with no registered validator passes through unvalidated.
- **On a client-streaming or duplex call it fails at the first bad message** — the messages read before it have already been handed to your code. There is no all-or-nothing over a stream.
- **It never validates what the server sends back.** Response messages and streamed replies are not checked.
- **The errors are a string and a trailer, not `google.rpc.BadRequest`.** A non-.NET client has to parse `validation-errors-json` itself; nothing in the standard richer-error model is emitted.
- **proto3 has no "absent" for scalars.** A missing `string` arrives as `""` and a missing `int32` as `0`, so `NotEmpty()` cannot distinguish "not sent" from "sent empty". Use `optional` fields or a wrapper type in the `.proto` when that difference matters.
- **Outside an ASP.NET Core host there is no request scope.** A service hosted some other way falls back to the provider the interceptor was constructed with, where a scoped validator cannot be resolved.

## Targets

`net8.0`, `net9.0`, `net10.0`; grpc-dotnet 2.x (`Grpc.AspNetCore.Server`).

## With the rest of OrionGuard

[OrionGuard](https://www.nuget.org/packages/OrionGuard) · [OrionGuard.AspNetCore](https://www.nuget.org/packages/OrionGuard.AspNetCore) · [OrionGuard.OpenTelemetry](https://www.nuget.org/packages/OrionGuard.OpenTelemetry)

## Documentation

- [Repository and full documentation](https://github.com/tunahanaliozturk/OrionGuard)
- [Changelog](https://github.com/tunahanaliozturk/OrionGuard/blob/master/CHANGELOG.md)

## License

MIT. See [LICENSE.txt](https://github.com/tunahanaliozturk/OrionGuard/blob/master/src/Moongazing.OrionGuard/docs/LICENSE.txt).
