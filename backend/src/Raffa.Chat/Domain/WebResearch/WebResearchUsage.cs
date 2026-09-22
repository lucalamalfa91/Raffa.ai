using Raffa.SharedKernel;

namespace Raffa.Chat.Domain.WebResearch;

/// <summary>
/// One row per tenant per UTC day: how many web-research calls were spent (ADR-030, gate 3).
/// Table <c>chat_web_research_usage</c>, composite key <c>(tenant_id, day)</c> — the counter's own
/// identity, so the budget's conditional upsert is one atomic statement and never a
/// read-then-write. Deliberately not a <see cref="Conversations.TenantScopedEntity"/> (that base
/// type's client-generated <c>Id</c> would make a second key for a row that only ever has one);
/// it still carries a not-null, indexed <see cref="TenantId"/> as its leading key column and the
/// same forced RLS policy as every other table in this module (ADR-009).
/// </summary>
public sealed class WebResearchUsage
{
    public required TenantId TenantId { get; set; }

    public required DateOnly Day { get; set; }

    public int Calls { get; set; }
}
