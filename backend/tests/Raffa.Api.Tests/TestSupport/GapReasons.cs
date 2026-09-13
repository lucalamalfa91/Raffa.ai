namespace Raffa.Api.Tests.TestSupport;

/// <summary>
/// Named reasons for a deliberately-authored-but-skipped test (task E15/F01/US01/T01, wave w14) —
/// the same "write it now, skip it with a named reason, never a silent gap" discipline ADR-025 §H
/// already applies to T14 (activated by NW-05/NW-08 in W15), extended here to the one other place
/// this task's own review found the wave's interim architecture cannot yet support a non-vacuous
/// proof.
/// </summary>
internal static class GapReasons
{
    /// <summary>
    /// ADR-025 Rule D.5b/D.5e claim that a removed member's next document read/write and Ask call
    /// are structurally refused ("nothing caches authorization... the tenant used for retrieval
    /// becomes a verified membership fact") — true of every endpoint <b>this task itself owns</b>
    /// (the roster, workspace discovery, accept/issue/revoke/remove), but <b>not yet true</b> of
    /// <c>DocumentsEndpointExtensions.GetDocumentAsync</c>/<c>ListDocumentsAsync</c> or
    /// <c>Raffa.Api.AskCopilotService.AskAsync</c> (via <c>ChatEndpointExtensions</c>/
    /// <c>ConversationsEndpointExtensions</c>): both resolve their tenant from the interim
    /// <c>X-Tenant-Id</c> header plus RLS alone, with <b>no <c>workspace_membership</c> check at
    /// all</b> — confirmed by reading every handler in both files. Only the two Admin-only actions
    /// on documents (reprocess, delete) consult <c>WorkspaceRoleResolver</c>, and even those answer
    /// 403 for a removed member (a real membership check, but not the ADR's own 404), never 404.
    ///
    /// <para>
    /// Closing this for real means adding a membership-verified tenant resolution to
    /// <c>Raffa.Api.DocumentsEndpointExtensions</c>'s plain read path and to
    /// <c>Raffa.Api.AskCopilotService</c>/its two callers — three files this task does not own
    /// (none is in its own "Files to create or modify", and <c>DocumentsEndpointExtensions.cs</c>
    /// is a same-phase sibling task's file). Retrofitting it here would also flip the answer for
    /// every <b>other</b> existing test in this solution that calls those endpoints with an
    /// identity that has no <c>workspace_user</c> row at all (the cross-tenant-isolation suites in
    /// <c>Raffa.IntegrationTests</c>, which predate the membership model), which is a strictly
    /// larger, cross-cutting change than this task's own scope. ADR-025 §I already reserves exactly
    /// this class of change — authorization becoming uniformly database-verified instead of
    /// claim/header-trusting — for W15 (NW-05/NW-08). Recorded here, the same way T14 is, rather
    /// than either silently weakening the test or silently expanding this task's file scope into
    /// three files it was not asked to own.
    /// </para>
    /// </summary>
    public const string MembershipVerifiedReadsNotYetWired =
        "requires membership-verified tenant resolution on DocumentsEndpointExtensions' plain " +
        "read path and on AskCopilotService/its callers (ChatEndpointExtensions, " +
        "ConversationsEndpointExtensions) -- today both resolve the tenant from X-Tenant-Id + RLS " +
        "alone, with no workspace_membership check; ADR-025 Rule D.5b/D.5e's claim is structural " +
        "only for the endpoints this task owns. Closing it is a cross-cutting change to three " +
        "files outside this task's scope, reserved by ADR-025 §I for W15 (NW-05/NW-08).";
}
