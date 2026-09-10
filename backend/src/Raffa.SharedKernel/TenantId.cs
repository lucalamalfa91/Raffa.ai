namespace Raffa.SharedKernel;

/// <summary>
/// Strongly-typed identifier for a tenant. Every tenant-scoped entity carries this.
/// RLS in the data layer enforces isolation; domain code passes the context.
/// </summary>
public readonly record struct TenantId(Guid Value)
{
    public static TenantId New() => new(Guid.NewGuid());

    public override string ToString() => Value.ToString();
}
