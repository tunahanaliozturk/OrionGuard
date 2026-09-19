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
| POST | `/{id:guid}/replay` | | Re-queue a failed or dead-lettered row (needs the mutation header) |
| POST | `/{id:guid}/discard` | | Mark an unprocessed row as processed without dispatching it (needs the mutation header) |

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
- `OnMutation` (`Func<OutboxMutationEvent, Task>`) runs after each successful replay or discard is saved, with `Action` (`"replay"` or `"discard"`), `OutboxMessageId`, `HttpContext`, and `OccurredAtUtc`. It does not run for a 404, a 409, an already-processed discard, or a request rejected for a missing mutation header. If it throws, the request fails but the change stays saved.

## Cross-site request forgery

Replay and discard take no body, so without a further check a page on another origin could call them with `fetch(url, { method: "POST", credentials: "include" })`: the browser sends that as a simple cross-origin request, without a CORS preflight, and attaches the operator's cookies. The dashboard therefore requires a custom request header on both endpoints, `X-OrionGuard-Dashboard` by default, with any non-empty value. A request without it gets 400 with `error: "missing-mutation-header"` and changes nothing. A page can only add a custom header to a cross-origin request after a CORS preflight, which fails unless the host's CORS policy allows that origin and header, so a cross-site page cannot send it. The read endpoints do not need the header.

```bash
curl -X POST "https://ops.example.com/_orion/outbox/3f2b8a52-5d1c-4c1e-9f0e-2a7c0e1d4b6a/replay" \
  -H "Authorization: Bearer $TOKEN" \
  -H "X-OrionGuard-Dashboard: 1"
```

From a same-origin operator page:

```js
await fetch(`/_orion/outbox/${id}/discard`, {
  method: "POST",
  headers: { "X-OrionGuard-Dashboard": "1" },
});
```

- `MutationHeaderName` changes the header name. It must be a custom header: CORS-safelisted headers (`Accept`, `Accept-Language`, `Content-Language`, `Content-Type`, `Range`) and headers the browser sends itself (`Cookie`, `Origin`, `Referer`, `Host`, `Sec-*`, `Proxy-*`, ...) make `MapOutboxDashboard` throw `InvalidOperationException`.
- The protection holds only while your CORS policy does not allow credentialed requests with this header from origins you do not trust. A policy that combines `AllowCredentials()` with `AllowAnyHeader()` for such origins, or reflects any origin, re-opens the endpoints to them.
- `RequireMutationHeader = false` turns the check off. Do that only when no caller authenticates with a cookie, for example when every caller sends a bearer token.

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
| `RequireMutationHeader` | `true` |
| `MutationHeaderName` | `X-OrionGuard-Dashboard` |
| `OnMutation` | `null` |
| `SecurityHeaders` | `X-Frame-Options: DENY`, `X-Content-Type-Options: nosniff`, `Referrer-Policy: no-referrer`, `Cache-Control: no-store` |

`MapOutboxDashboard` throws `InvalidOperationException` for an empty `RoutePrefix`, a page size or `FailedRetryThreshold` below 1, a negative `ErrorTruncationLength`, or, while `RequireMutationHeader` is on, a `MutationHeaderName` that is empty or not a custom header.

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
