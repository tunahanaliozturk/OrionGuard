# OrionGuard.Templates

`dotnet new` templates that start from a project with OrionGuard already wired up — request validation answering RFC 9457 ProblemDetails, optionally over an EF Core transactional outbox — instead of an empty `WebApplication.CreateBuilder`.

```bash
dotnet new install OrionGuard.Templates
```

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

You get a 422 `ValidationProblemDetails` naming both fields, produced by the endpoint filter *before* the handler runs; a valid body echoes back, and `GET /health` reports the OrionGuard health check. The generated `Program.cs` is about thirty lines with a comment on each moving part, so it reads as a worked example rather than a black box.

`dotnet new uninstall OrionGuard.Templates` removes the templates again.

## The two templates

| Short name | What it generates |
| --- | --- |
| `orionguard-webapi` | A minimal API with `AddOrionGuardAspNetCore()`, one validator registered by hand, `UseOrionGuardValidation()` for ProblemDetails, `.WithValidation<T>()` on the endpoint, and `AddOrionGuardCheck()` on `/health` |
| `orionguard-outbox` | The same, plus EF Core: an `AggregateRoot<Guid>` that raises a domain event, `AddOrionGuardEfCore<T>(o => o.UseOutbox())`, and the dispatcher hosted service that delivers the rows |

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

`Order.Place` raises `OrderPlaced`; `SaveChangesAsync` writes the order row and the outbox row in one transaction; `OutboxDispatcherHostedService` picks the row up on its next poll and runs `OrderPlacedHandler`, which logs it. That is the whole outbox round trip in a project you can step through.

## Parameters

| Parameter | Applies to | Default | Values |
| --- | --- | --- | --- |
| `-n`, `--name` | both | `OrionGuardApi` / `OrionGuardOutbox` | Any name; it renames the project, the root namespace and the folder |
| `--database` | `orionguard-outbox` | `sqlite` | `sqlite`, `sqlserver`, `postgres` |

`sqlite` is the default because it needs no server, so the generated project runs unchanged. `sqlserver` and `postgres` read `ConnectionStrings:Orders` from configuration and fall back to a localhost default — `(localdb)\MSSQLLocalDB` and `Host=localhost`, neither of which is guaranteed to be there.

## Changing the target framework

Both templates generate a `net10.0` project.

**`orionguard-webapi`** moves to `net9.0` or `net8.0` by editing `TargetFramework` and nothing else: `OrionGuard.AspNetCore` targets all three.

**`orionguard-outbox`** needs the EF Core provider moved with it. `OrionGuard.EntityFrameworkCore` builds against EF Core 10 on `net10.0` and EF Core 9 on `net8.0` and `net9.0`, and the provider the template pins is a 10.x one, which supports `net10.0` only. Lowering `TargetFramework` on its own fails the restore before anything is compiled:

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

Every cell above was restored to check it: each 10.x provider fails `NU1202` on both `net9.0` and `net8.0`, and each replacement restores on both. This is the one table here with version numbers in it, because a rule alone does not tell you which version to type.

The rule behind it is the one in the [OrionGuard.EntityFrameworkCore readme](https://www.nuget.org/packages/OrionGuard.EntityFrameworkCore): the provider package has to come from the same EF Core major version as the one the integration was built against for your target framework.

## What this does not do

- **A generated project is a starting point, not a reference deployment.** It calls `EnsureCreatedAsync()` at startup so the sample runs; replace that with an EF Core migration so `OrionGuard_Outbox` and `OrionGuard_OutboxLocks` are versioned with the rest of your schema.
- **No validator is discovered for you.** `AddOrionGuardAspNetCore()` does not scan assemblies, and the template registers its one validator by hand — on purpose, so the generated code shows the registration you will have to repeat.
- **The pinned OrionGuard version is the one the pack shipped with.** The template pack's version tracks the OrionGuard release it scaffolds against, so a project generated from an older pack references older packages; `dotnet add package` afterwards, or reinstall the pack.
- **`--database` is the only choice offered.** There is no switch for authentication, Docker, tests, Aspire or a solution file; add what you need to the generated project.
- **Only `sqlite` runs with no setup.** `sqlserver` and `postgres` start against a server that has to exist, and fall back to a localhost connection string that is a guess about your machine.

## Why the pack ships on its own

The release job packs the solution and then asserts that the number of `.nupkg` files equals the number of packable `.csproj` files under `src/`, refusing to publish a partial set if they disagree. The template pack lives in `templates/` and is deliberately outside both `src/` and the solution: packing it with the solution would produce one package more than that count and fail the check on every release. It is packed and pushed as its own project instead, which is also why it carries its own `docs/README.md` — the one you are reading.

## Documentation

- [Repository and full documentation](https://github.com/tunahanaliozturk/OrionGuard)
- [Changelog](https://github.com/tunahanaliozturk/OrionGuard/blob/master/CHANGELOG.md)
- Packages the templates wire up: [OrionGuard](https://www.nuget.org/packages/OrionGuard), [OrionGuard.AspNetCore](https://www.nuget.org/packages/OrionGuard.AspNetCore), [OrionGuard.EntityFrameworkCore](https://www.nuget.org/packages/OrionGuard.EntityFrameworkCore)

## License

MIT. See [LICENSE.txt](https://github.com/tunahanaliozturk/OrionGuard/blob/master/src/Moongazing.OrionGuard/docs/LICENSE.txt).
