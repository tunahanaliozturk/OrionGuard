# OrionGuard.Outbox.Dashboard

Operator endpoints for the outbox in `OrionGuard.EntityFrameworkCore`, part of [OrionGuard](https://github.com/tunahanaliozturk/OrionGuard). It maps an ASP.NET Core route group that lists failed and dead-lettered outbox rows as JSON and lets an operator replay or discard a row. There is no HTML UI.

## Install

```bash
dotnet add package OrionGuard.Outbox.Dashboard
```

`OrionGuard.EntityFrameworkCore` is installed as a dependency. The package uses the ASP.NET Core shared framework.

## Quick start

```csharp
using Moongazing.OrionGuard.Outbox.Dashboard;

builder.Services.AddAuthorization(options =>
    options.AddPolicy("OutboxOps", policy => policy.RequireRole("ops")));

var app = builder.Build();

app.UseAuthentication();
app.UseAuthorization();

app.MapOutboxDashboard<AppDbContext>(o => o.AuthorizationPolicyName = "OutboxOps");
```

`AppDbContext` must be registered in DI and map `OutboxMessage` (see the `OrionGuard.EntityFrameworkCore` README). Authentication setup is up to you.

## Authorization

The dashboard can replay and discard rows, and it shows event types and error text, so decide explicitly how it is protected:

- `AuthorizationPolicyName` set: the group calls `RequireAuthorization(policyName)`.
- Neither option set (the default), and the host has an `AuthorizationOptions.FallbackPolicy`: the group adds no authorization metadata, so the fallback policy applies. (Calling `RequireAuthorization()` here would replace a stricter fallback with the default policy.)
- Neither option set, and the host has no fallback policy: the group calls `RequireAuthorization()`, so the host's default policy applies, which requires an authenticated user. The dashboard is never anonymous unless you opt out.
- `AllowAnonymous = true`: the group calls `AllowAnonymous()`. Not recommended outside local development.

Because the group can carry authorization metadata, call `app.UseAuthentication()` and `app.UseAuthorization()` before mapping it.

`MapOutboxDashboard` returns the `RouteGroupBuilder`, so you can add more conventions, for example `.RequireHost(...)`. Set `EnableMutations = false` to map only the read endpoints.

## Endpoints

Routes are relative to `RoutePrefix` (default `/_orion/outbox`):

| Method | Route | Query | Result |
| --- | --- | --- | --- |
| GET | `/failed` | `page`, `size`, `sort` | Offset page of failed rows |
| GET | `/failed/cursor` | `cursor`, `size`, `sort` | Keyset page of failed rows |
| POST | `/{id:guid}/replay` | | Re-queue a failed or dead-lettered row |
| POST | `/{id:guid}/discard` | | Mark an unprocessed row as processed without dispatching it |

- `page` defaults to 1. `size` defaults to `DefaultPageSize` (25) and is clamped to `MaxPageSize` (100). Values below 1 fall back to the defaults. A page past the last one, however large, returns an empty `items`.
- `sort` is `OldestFirst` (by `OccurredOnUtc`, the default), `NewestFirst`, or `MostRetries` (by `RetryCount` descending, then `OccurredOnUtc`), matched case-insensitively. Anything else falls back to `DefaultSort`.
- `/failed` returns `page`, `size`, `total`, `totalPages`, `hasNextPage`, `hasPreviousPage`, `sort`, and `items`.
- `/failed/cursor` returns `size`, `sort`, `items`, `nextCursor`, and `hasNextPage`. Pass `nextCursor` back as `cursor` for the next page. A valid cursor fixes the sort it was issued with, and `sort` is then ignored; an invalid cursor starts from the first page. The cursor is opaque but not signed.

Each item has `id`, `eventType`, `occurredOnUtc`, `retryCount`, `error` (cut to `ErrorTruncationLength`, default 1024 characters), and `correlationId`. The EF Core interceptor does not set `CorrelationId`, so it is null unless your code writes it. Payloads are never returned.

## What counts as failed

A row is listed when `Error` is set and `RetryCount >= FailedRetryThreshold` (default 3). That covers rows still being retried and rows the dispatcher dead-lettered.

- Keep `FailedRetryThreshold` at or below `OutboxOptions.MaxRetries` (default 5). A row dead-lettered with fewer retries than the threshold is not listed.
- Rows dead-lettered because their type could not be resolved or deserialized (`TYPE_NOT_FOUND`, `TYPE_NOT_DOMAIN_EVENT`, `DESERIALIZE_FAILED`) are not counted as retries, so such a row that fails on its first attempt has `RetryCount` 0 and is not listed.

## Replay and discard

- **Replay** sets `RetryCount` to 0 and clears `Error` and `ProcessedOnUtc`, so the dispatcher picks the row up on its next poll and runs every handler again. It returns 200 `{ id, action }`, 404 for an unknown id, and 409 with `error: "already-processed-success"` for a row that was processed without an error, including one the dispatcher finished while the request was running.
- **Discard** stamps `ProcessedOnUtc` and keeps `Error` and `RetryCount`. It works on any unprocessed row, not only failed ones. It returns 200 `{ id, action }`, 200 with `note: "already processed"` if the row was already processed (by the dispatcher, meanwhile or earlier, or by a previous discard), and 404 for an unknown id. With `OutboxArchivalOptions.PreserveDeadLetters` on, a discarded row that has an `Error` is never archived.
- Both are a single conditional `UPDATE` (`ExecuteUpdate`) on the row's current state, and the dispatcher updates rows the same way, so a replay or discard is never undone by a dispatch that was in flight, and a dispatch that finished first is never overwritten. They therefore need a relational EF Core provider; the EF Core InMemory provider can serve the read endpoints only.
- `OnMutation` (`Func<OutboxMutationEvent, Task>`) runs after each successful replay or discard is saved, with `Action` (`"replay"` or `"discard"`), `OutboxMessageId`, `HttpContext`, and `OccurredAtUtc`. It does not run for a 404, a 409, or an already-processed discard. If it throws, the request fails but the change stays saved.

## Options

| `OutboxDashboardOptions` | Default |
| --- | --- |
| `RoutePrefix` | `/_orion/outbox` |
| `AuthorizationPolicyName` | `null` |
| `AllowAnonymous` | `false` |
| `DefaultPageSize` / `MaxPageSize` | 25 / 100 |
| `FailedRetryThreshold` | 3 |
| `ErrorTruncationLength` | 1024 |
| `DefaultSort` | `OutboxFailedListingSort.OldestFirst` |
| `EnableMutations` | `true` |
| `OnMutation` | `null` |
| `SecurityHeaders` | `X-Frame-Options: DENY`, `X-Content-Type-Options: nosniff`, `Referrer-Policy: no-referrer`, `Cache-Control: no-store` |

`MapOutboxDashboard` throws `InvalidOperationException` for an empty `RoutePrefix`, a page size or `FailedRetryThreshold` below 1, or a negative `ErrorTruncationLength`.

## Security headers

An endpoint filter adds `SecurityHeaders` to every response the dashboard handlers produce. Assign an empty dictionary to turn it off. Responses that authentication or authorization short-circuit (401, 403) never reach that filter; to cover them, add the middleware before `UseAuthentication()`:

```csharp
var dashboardOptions = new OutboxDashboardOptions();
app.UseOutboxDashboardSecurityHeaders(dashboardOptions.RoutePrefix, dashboardOptions.SecurityHeaders);
```

## Targets

- `net8.0`, `net9.0`, `net10.0`, with the `Microsoft.AspNetCore.App` shared framework
- `OrionGuard.EntityFrameworkCore` of the same version (6.7.0), which uses EF Core 9.0.20 on net8.0/net9.0 and EF Core 10.0.12 on net10.0

## Documentation

- [Repository and full documentation](https://github.com/tunahanaliozturk/OrionGuard)
- [Changelog](https://github.com/tunahanaliozturk/OrionGuard/blob/master/CHANGELOG.md)
- Related packages: [OrionGuard.EntityFrameworkCore](https://www.nuget.org/packages/OrionGuard.EntityFrameworkCore) (the outbox and dead-letter handling)

## License

MIT. See [LICENSE.txt](https://github.com/tunahanaliozturk/OrionGuard/blob/master/src/Moongazing.OrionGuard/docs/LICENSE.txt).
