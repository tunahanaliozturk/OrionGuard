# OrionGuard.Templates

`dotnet new` templates for [OrionGuard](https://github.com/tunahanaliozturk/OrionGuard). Two project
templates that start from working, wired-up code instead of an empty `WebApplication.CreateBuilder`.

## Install

```bash
dotnet new install OrionGuard.Templates
```

`dotnet new uninstall OrionGuard.Templates` removes them again.

## Templates

| Short name | What you get |
| --- | --- |
| `orionguard-webapi` | A minimal API with `AddOrionGuardAspNetCore()`, one validator registered by hand, `UseOrionGuardValidation()` for RFC 9457 ProblemDetails, `.WithValidation<T>()` on the endpoint, and `AddOrionGuardCheck()` on `/health` |
| `orionguard-outbox` | The same, plus EF Core: an `AggregateRoot<Guid>` that raises a domain event, the outbox and lock tables mapped in `OnModelCreating`, `AddOrionGuardEfCore<T>(o => o.UseOutbox())`, and the dispatcher hosted service it registers |

## orionguard-webapi

```bash
dotnet new orionguard-webapi -n Acme.Api
cd Acme.Api
dotnet run
```

```bash
curl -X POST http://localhost:5000/customers \
  -H 'Content-Type: application/json' \
  -d '{"email":"not-an-email","displayName":"A"}'
```

The endpoint filter rejects that body with a 422 `ValidationProblemDetails` before the handler runs.
A valid body echoes back. `GET /health` reports the OrionGuard health check.

## orionguard-outbox

```bash
dotnet new orionguard-outbox -n Acme.Orders
cd Acme.Orders
dotnet run
```

```bash
curl -X POST http://localhost:5000/orders \
  -H 'Content-Type: application/json' \
  -d '{"sku":"ABC-123","quantity":2}'
```

`Order.Place` raises `OrderPlaced`; `SaveChangesAsync` writes the order row and the outbox row in one
transaction; `OutboxDispatcherHostedService` picks the row up on its next poll and runs
`OrderPlacedHandler`, which logs it.

| Option | Default | Values |
| --- | --- | --- |
| `--database` | `sqlite` | `sqlite`, `sqlserver`, `postgres` |

`sqlite` is the default because it needs no server, so the generated project runs unchanged. The
other two read `ConnectionStrings:Orders` from configuration and fall back to a localhost default.

The generated project calls `EnsureCreatedAsync()` at startup to keep the sample runnable. Replace it
with an EF Core migration so that `OrionGuard_Outbox` and `OrionGuard_OutboxLocks` are versioned with
the rest of the schema.

## Targets

The generated projects target `net10.0`. The packages they reference support `net8.0`, `net9.0` and
`net10.0`, so lowering `TargetFramework` after generating works.

## Documentation

- [Repository and full documentation](https://github.com/tunahanaliozturk/OrionGuard)
- [Changelog](https://github.com/tunahanaliozturk/OrionGuard/blob/master/CHANGELOG.md)
- Packages the templates wire up: [OrionGuard.AspNetCore](https://www.nuget.org/packages/OrionGuard.AspNetCore), [OrionGuard.EntityFrameworkCore](https://www.nuget.org/packages/OrionGuard.EntityFrameworkCore)

## License

MIT. See [LICENSE.txt](https://github.com/tunahanaliozturk/OrionGuard/blob/master/src/Moongazing.OrionGuard/docs/LICENSE.txt).
