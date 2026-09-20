# OrionGuard.Outbox.Dashboard

Gives an on-call operator the two things they need when the outbox has failures: a list of what is stuck, and a way to replay or discard a row — as JSON endpoints in your own app, behind your own auth.

```bash
dotnet add package OrionGuard.Outbox.Dashboard
```

```csharp
using Microsoft.EntityFrameworkCore;
using Moongazing.OrionGuard.Outbox.Dashboard;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDbContext<AppDbContext>(o => o.UseSqlServer("..."));
builder.Services.AddAuthorization(options =>
    options.AddPolicy("OutboxOps", policy => policy.RequireRole("ops")));

var app = builder.Build();

app.UseAuthentication();
app.UseAuthorization();

app.MapOutboxDashboard<AppDbContext>(o => o.AuthorizationPolicyName = "OutboxOps");

app.Run();

public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options);
```

`GET /_orion/outbox/failed` now returns a page of stuck rows — id, event type, when it happened, retry count and the truncated error — and `POST /_orion/outbox/{id}/replay` puts one back in the queue. `OrionGuard.EntityFrameworkCore` comes along as a dependency; your `AppDbContext` must be registered and must map `OutboxMessage`. There is no HTML UI, and authentication is yours to set up.

## Endpoints

Relative to `RoutePrefix` (default `/_orion/outbox`):

| Method | Route | Query | Result |
| --- | --- | --- | --- |
| GET | `/failed` | `page`, `size`, `sort` | Offset page of failed rows |
| GET | `/failed/cursor` | `cursor`, `size`, `sort` | Keyset page of failed rows |
| POST | `/{id:guid}/replay` | | Re-queue a failed or dead-lettered row (needs the mutation header) |
| POST | `/{id:guid}/discard` | | Mark an unprocessed row processed without dispatching it (needs the mutation header) |

`page` defaults to 1; `size` defaults to `DefaultPageSize` and is clamped to `MaxPageSize`; values below 1 fall back to the defaults, and a page past the last one returns an empty `items` however large it is. `sort` is `OldestFirst` (default), `NewestFirst` or `MostRetries`, matched case-insensitively, with anything else falling back to `DefaultSort`.

`/failed` returns `page`, `size`, `total`, `totalPages`, `hasNextPage`, `hasPreviousPage`, `sort` and `items`. `/failed/cursor` returns `size`, `sort`, `items`, `nextCursor` and `hasNextPage`; pass `nextCursor` back as `cursor`. A valid cursor fixes the sort it was issued with, so `sort` is then ignored; an invalid one starts from the first page.

Each item carries `id`, `eventType`, `occurredOnUtc`, `retryCount`, `error` (cut to `ErrorTruncationLength`) and `correlationId`. The EF Core interceptor does not set `CorrelationId`, so it is null unless your own code writes it.

### What counts as failed

A row is listed when `Error` is set **and** `RetryCount >= FailedRetryThreshold` (default 3) — that covers rows still being retried and rows the dispatcher dead-lettered.

### Replay and discard

**Replay** sets `RetryCount` to 0 and clears `Error` and `ProcessedOnUtc`, so the dispatcher picks the row up next poll and runs every handler again. 200 `{ id, action }`; 404 for an unknown id; 409 with `error: "already-processed-success"` for a row that was processed without an error, including one the dispatcher finished while the request was in flight.

**Discard** stamps `ProcessedOnUtc` and keeps `Error` and `RetryCount`. It works on any unprocessed row, not only failed ones. 200 `{ id, action }`; 200 with `note: "already processed"` when the row was already processed; 404 for an unknown id. With `OutboxArchivalOptions.PreserveDeadLetters` on, a discarded row that has an `Error` is never archived.

Both are a single conditional `ExecuteUpdate` on the row's current state, and the dispatcher updates rows the same way, so a replay or discard is never undone by an in-flight dispatch and a dispatch that finished first is never overwritten.

`OnMutation` (`Func<OutboxMutationEvent, Task>`) runs after each successful replay or discard is saved, with `Action` (`"replay"` or `"discard"`), `OutboxMessageId`, `HttpContext` and `OccurredAtUtc` — the hook for writing an audit record with the operator's name.

## Authorization

The dashboard can replay and discard rows and shows event types and error text, so how it is protected is decided explicitly:

- `AuthorizationPolicyName` set → the group calls `RequireAuthorization(policyName)`.
- Neither option set and the host has an `AuthorizationOptions.FallbackPolicy` → no authorization metadata is added, so the fallback applies. (Calling `RequireAuthorization()` here would *replace* a stricter fallback with the default policy.)
- Neither option set and no fallback policy → the group calls `RequireAuthorization()`, so the host's default policy applies and an authenticated user is required.
- `AllowAnonymous = true` → the group calls `AllowAnonymous()`. The explicit opt-out, for local development.

Because the group carries authorization metadata, call `UseAuthentication()` and `UseAuthorization()` before mapping it. `MapOutboxDashboard` returns the `RouteGroupBuilder`, so you can add conventions of your own such as `.RequireHost(...)`; `EnableMutations = false` maps the read endpoints only.

## Cross-site request forgery

Replay and discard take no body, so without a further check a page on another origin could call them with `fetch(url, { method: "POST", credentials: "include" })` — the browser sends that as a simple cross-origin request, with no CORS preflight, carrying the operator's cookies. Both endpoints therefore require a custom header, `X-OrionGuard-Dashboard` by default, with any non-empty value; without it the request gets 400 `error: "missing-mutation-header"` and changes nothing. A cross-origin page can only add a custom header after a preflight, which fails unless your CORS policy allows that origin *and* that header. The read endpoints do not need it.

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

`MutationHeaderName` renames the header and **must start with `X-`** — any other name makes `MapOutboxDashboard` throw. The check only works with a header a cross-site page has to ask permission for, which rules out the CORS-safelisted names (`Accept`, `Accept-Language`, `Content-Language`, `Content-Type`, `Range`) and everything the browser attaches by itself (`Cookie`, `Origin`, `Referer`, `Sec-*`, client hints, a list that keeps growing) — while no browser adds an `X-` request header on its own.

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

`MapOutboxDashboard` throws `InvalidOperationException` for an empty `RoutePrefix`, a page size or `FailedRetryThreshold` below 1, a negative `ErrorTruncationLength`, or — while `RequireMutationHeader` is on — a `MutationHeaderName` that is empty or not a custom header.

An endpoint filter adds `SecurityHeaders` to every response the dashboard's own handlers produce; assign an empty dictionary to turn it off.

## What this does not do

- **There is no UI.** These are JSON endpoints. Anything an operator clicks, you build.
- **Payloads are never returned.** You see the event type and the error, not the event body — deliberately, since payloads carry business data and the listing is a read for on-call, not an export.
- **Replay and discard need a relational EF Core provider.** They are conditional `ExecuteUpdate` statements; the InMemory provider can serve the read endpoints only.
- **Replay re-runs every handler of the event**, including the ones that already succeeded. It is the outbox's at-least-once contract surfaced as a button — handlers must be idempotent.
- **Some dead letters never appear in the list.** A row dead-lettered because its type could not be resolved or deserialized (`TYPE_NOT_FOUND`, `TYPE_NOT_DOMAIN_EVENT`, `DESERIALIZE_FAILED`) is not counted as a retry, so it sits at `RetryCount` 0 and falls below `FailedRetryThreshold`. Keep the threshold at or below `OutboxOptions.MaxRetries`, and drop it to 1 if you want those rows listed.
- **The cursor is opaque but not signed.** Treat it as a pagination token, not as a capability.
- **`OnMutation` cannot veto anything.** It runs *after* the change is saved, and an exception in it fails the request while leaving the change in place. It also does not run for a 404, a 409, an already-processed discard, or a request rejected for a missing header.
- **The CSRF header is not a defence on its own.** It holds only while your CORS policy does not allow credentialed requests with that header from origins you do not trust; `AllowCredentials()` combined with `AllowAnyHeader()`, or a reflected origin, re-opens the endpoints. `RequireMutationHeader = false` turns the check off, which is reasonable only when no caller authenticates with a cookie.
- **Security headers miss 401 and 403.** Responses short-circuited by authentication or authorization never reach the endpoint filter. Add the middleware ahead of `UseAuthentication()` to cover them:

```csharp
using Moongazing.OrionGuard.Outbox.Dashboard;

public static class DashboardSecurityHeaders
{
    public static void Use(IApplicationBuilder app)
    {
        var dashboardOptions = new OutboxDashboardOptions();
        app.UseOutboxDashboardSecurityHeaders(dashboardOptions.RoutePrefix, dashboardOptions.SecurityHeaders);
    }
}
```

## Targets

`net8.0`, `net9.0`, `net10.0`, on the ASP.NET Core shared framework. Use the `OrionGuard.EntityFrameworkCore` build from the same OrionGuard release.

## With the rest of OrionGuard

[OrionGuard.EntityFrameworkCore](https://www.nuget.org/packages/OrionGuard.EntityFrameworkCore) (the outbox and its dead letters) · [OrionGuard.Outbox.PostgresNotify](https://www.nuget.org/packages/OrionGuard.Outbox.PostgresNotify) · [OrionGuard.Outbox.SqlServerBroker](https://www.nuget.org/packages/OrionGuard.Outbox.SqlServerBroker)

## Documentation

- [Repository and full documentation](https://github.com/tunahanaliozturk/OrionGuard)
- [Changelog](https://github.com/tunahanaliozturk/OrionGuard/blob/master/CHANGELOG.md)

## License

MIT. See [LICENSE.txt](https://github.com/tunahanaliozturk/OrionGuard/blob/master/src/Moongazing.OrionGuard/docs/LICENSE.txt).
