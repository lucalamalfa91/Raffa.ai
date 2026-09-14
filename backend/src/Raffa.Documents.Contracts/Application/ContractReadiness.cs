namespace Raffa.Documents.Contracts.Application;

/// <summary>
/// ADR-027 §D9 (task E16/F02/US03/T01): the one top-level answer to "can this contract be shown
/// yet?", derived from the single definition of <em>validated</em> the wave keeps (§D8, #1: a
/// linked document reached <c>Completed</c>) over every document linked to the contract.
/// </summary>
public enum ContractReadinessState
{
    /// <summary>At least one linked document is <c>Completed</c>: the aggregate is real.</summary>
    Ready,

    /// <summary>None is <c>Completed</c> and at least one is still non-terminal
    /// (<c>Uploaded</c>/<c>Processing</c>): the empty tab tree is "not yet", not "nothing".</summary>
    Processing,

    /// <summary>Every linked document is terminal and none is <c>Completed</c> — or there is no
    /// linked document at all: the empty tab tree really is empty.</summary>
    Unavailable,
}

/// <summary>
/// <c>GET /api/contracts/{id}</c>'s <c>readiness</c> object (ADR-027 §D9). <paramref name="Stage"/>
/// mirrors the Documents list's live stage string and is <see langword="null"/> unless
/// <paramref name="State"/> is <see cref="ContractReadinessState.Processing"/> — it is nullable and
/// therefore carries no <c>enum</c> on the wire (ADR-012 w15 §9: the generator would drop the
/// <c>null</c>). One object, so a screen asks one question instead of scanning arrays for the
/// difference between "extraction found nothing" and "extraction has not run".
/// </summary>
public sealed record ContractReadiness(
    ContractReadinessState State,
    string? Stage,
    int DocumentCount,
    int CompletedDocumentCount)
{
    /// <summary>The wire value: <c>ready</c> | <c>processing</c> | <c>unavailable</c>.</summary>
    public string StateApiValue => State switch
    {
        ContractReadinessState.Ready => "ready",
        ContractReadinessState.Processing => "processing",
        _ => "unavailable",
    };
}
