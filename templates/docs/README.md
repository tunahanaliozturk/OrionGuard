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

Both templates generate a `net10.0` project.

**`orionguard-webapi`** drops to `net9.0` or `net8.0` by editing `TargetFramework` and nothing else:
`OrionGuard.AspNetCore` targets all three.

**`orionguard-outbox`** needs the EF Core provider moved with it. `OrionGuard.EntityFrameworkCore`
builds against EF Core 10 on `net10.0` and EF Core 9 on `net8.0` and `net9.0`, and the provider
package the template pins is a 10.x one, which supports `net10.0` only. Lowering `TargetFramework`
on its own fails the restore before it ever compiles:

```text
error NU1202: Package Microsoft.EntityFrameworkCore.Sqlite 10.0.12 is not compatible with
net8.0 (.NETCoreApp,Version=v8.0). Package Microsoft.EntityFrameworkCore.Sqlite 10.0.12
supports: net10.0 (.NETCoreApp,Version=v10.0)
```

Change the provider version in the same edit:

| `--database` | Generated, for `net10.0` | For `net9.0` and `net8.0` |
| --- | --- | --- |
| `sqlite` | `Microsoft.EntityFrameworkCore.Sqlite` 10.0.12 | 9.0.20 |
| `sqlserver` | `Microsoft.EntityFrameworkCore.SqlServer` 10.0.12 | 9.0.20 |
| `postgres` | `Npgsql.EntityFrameworkCore.PostgreSQL` 10.0.3 | 9.0.4 |

The rule behind the table is the one in the
[OrionGuard.EntityFrameworkCore readme](https://www.nuget.org/packages/OrionGuard.EntityFrameworkCore):
the provider package has to come from the same EF Core major version as the one the integration was
built against for your target framework.

## Documentation

- [Repository and full documentation](https://github.com/tunahanaliozturk/OrionGuard)
- [Changelog](https://github.com/tunahanaliozturk/OrionGuard/blob/master/CHANGELOG.md)
- Packages the templates wire up: [OrionGuard.AspNetCore](https://www.nuget.org/packages/OrionGuard.AspNetCore), [OrionGuard.EntityFrameworkCore](https://www.nuget.org/packages/OrionGuard.EntityFrameworkCore)

## License

MIT. See [LICENSE.txt](https://github.com/tunahanaliozturk/OrionGuard/blob/master/src/Moongazing.OrionGuard/docs/LICENSE.txt).
