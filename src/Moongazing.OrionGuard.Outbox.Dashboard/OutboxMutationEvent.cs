namespace Moongazing.OrionGuard.Outbox.Dashboard;

using Microsoft.AspNetCore.Http;

/// <summary>
/// Audit hook payload raised by the dashboard's replay / discard endpoints. Surface the
/// who-did-what context to the consumer's audit pipeline; the dashboard itself never
/// writes audit rows so consumers can pick any storage shape.
/// </summary>
/// <param name="Action">Either <c>"replay"</c> or <c>"discard"</c>.</param>
/// <param name="OutboxMessageId">Stable id of the row that was mutated.</param>
/// <param name="HttpContext">Inbound request context (route, user, IP) so consumers can stamp <c>User.Identity.Name</c> or claim-based identifiers.</param>
/// <param name="OccurredAtUtc">Server clock at the moment the mutation was applied.</param>
public sealed record OutboxMutationEvent(
    string Action,
    Guid OutboxMessageId,
    HttpContext HttpContext,
    DateTime OccurredAtUtc);
