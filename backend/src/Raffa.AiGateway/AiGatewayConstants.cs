namespace Raffa.AiGateway;

/// <summary>
/// Cross-cutting AI Gateway constants that dependents outside this project's own reference graph
/// must stay in sync with. ADR-002 forbids <c>Raffa.AiGateway</c> from referencing a domain
/// module (module-map "dependency graph": domain modules depend on the gateway, never the other
/// way around), so this value cannot be shared as a type/constant reference — it is duplicated by
/// agreement and called out at both ends instead.
/// </summary>
public static class AiGatewayConstants
{
    /// <summary>
    /// Vector width the `embed` role returns. Fixed at ADR-004's chosen embed candidate
    /// (`text-embedding-3-small`'s native 1536 dimensions — "dimension fixed at schema time;
    /// small dimension preferred for cost/size"). MUST equal
    /// <c>Raffa.Documents.Contracts.Domain.Embedding.VectorDimensions</c> — that constant's own
    /// doc comment cites this same ADR-004 decision. If either changes, change both; on the
    /// Documents/Contracts side that is a schema migration, not a simple constant edit.
    /// </summary>
    public const int EmbeddingDimensions = 1536;
}
