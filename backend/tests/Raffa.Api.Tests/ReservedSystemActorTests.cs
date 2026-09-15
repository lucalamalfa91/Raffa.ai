using Raffa.Chat.Application;
using Raffa.Savings.Application;

namespace Raffa.Api.Tests;

/// <summary>
/// S-T29 (task E18/F03/US02/T01, NW-32; ADR-011 w16 clause 16): "the two caller-less sites write
/// the reserved <c>system:&lt;component&gt;</c> principal, and that string is rejected as a token
/// subject — the two namespaces provably cannot collide." <see cref="RagAnswerService.AnswerAsync"/>
/// and <see cref="SavingsOpportunityService.CreateAsync"/> are the two write sites with no HTTP
/// caller (see each type's own <c>SystemActor</c> doc comment).
///
/// <para>
/// This proves the <b>structural</b> half of the disjointness claim (ADR-011 w16 clause 16a): a
/// resolved token subject is always an Entra object id, and an Entra <c>oid</c> is always a GUID —
/// <see cref="Raffa.Api.Infrastructure.TokenCallerIdentity"/> reads it via
/// <c>ClaimsPrincipal.GetObjectId()</c> and returns it verbatim, with no reformatting (see that
/// type's own doc comment: "opaque, case-sensitive... deliberately not lower-cased"). Neither
/// reserved constant below parses as a GUID and both carry a <c>:</c>, a character no GUID can
/// contain — so no value <see cref="Raffa.Api.Infrastructure.ICallerIdentity.Resolve"/> could ever
/// return collides with either constant. The <b>rejection itself</b> — nobody can present a
/// self-chosen <c>oid</c> — is enforced upstream of this application, by JWT signature validation
/// against Entra's own signing keys (ADR-010); that boundary is not re-tested here.
/// </para>
/// </summary>
public sealed class ReservedSystemActorTests
{
    [Theory]
    [InlineData(RagAnswerService.SystemActor)]
    [InlineData(SavingsOpportunityService.SystemActor)]
    public void Reserved_principal_is_never_a_valid_guid_so_it_can_never_collide_with_a_resolved_token_subject(
        string reservedActor)
    {
        Assert.False(Guid.TryParse(reservedActor, out _));
        Assert.Contains(':', reservedActor);
    }

    [Fact]
    public void Reserved_principals_use_the_documented_system_prefix_and_are_themselves_distinct()
    {
        Assert.StartsWith("system:", RagAnswerService.SystemActor, StringComparison.Ordinal);
        Assert.StartsWith("system:", SavingsOpportunityService.SystemActor, StringComparison.Ordinal);
        Assert.NotEqual(RagAnswerService.SystemActor, SavingsOpportunityService.SystemActor);
    }
}
