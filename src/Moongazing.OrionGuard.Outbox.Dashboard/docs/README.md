# OrionGuard.Outbox.Dashboard

Read-only operator dashboard for the OrionGuard outbox. Maps an authorized HTTP endpoint group that lists failed / poisoned messages from your `DbContext`. Replay and discard actions stage to v6.5.5.

## Install

```bash
dotnet add package OrionGuard.Outbox.Dashboard
```

## Wire-up

```csharp
app.MapOutboxDashboard<AppDbContext>(o =>
{
    o.RoutePrefix = "/_orion/outbox";        // default
    o.AuthorizationPolicyName = "OutboxOps"; // optional - else uses fallback policy
    o.FailedRetryThreshold = 3;              // matches v6.5.0 dispatcher default
});
```

## Endpoints

| Method | Path | Description |
|--------|------|-------------|
| GET    | `/_orion/outbox/failed?page=1&size=25` | Paginated failed-messages listing |

The failed listing returns:

```json
{
  "page": 1,
  "size": 25,
  "total": 42,
  "items": [
    {
      "id": "...",
      "eventType": "Acme.OrderShipped, Acme",
      "occurredOnUtc": "2026-06-09T...",
      "retryCount": 3,
      "error": "(truncated)",
      "correlationId": "..."
    }
  ]
}
```

Payload bodies are deliberately omitted to limit blast-radius if dashboard authorization is misconfigured.

## Security

The route group calls `RequireAuthorization()` by default (host's fallback policy applies). For an explicit named policy, set `AuthorizationPolicyName`. To opt out entirely, set `AllowAnonymous = true` (NOT recommended for production).
