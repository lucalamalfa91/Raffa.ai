namespace Raffa.SharedKernel;

/// <summary>
/// Strongly-typed identifier for domain entities.
/// Wraps a GUID to prevent accidental mixing of entity IDs across bounded contexts.
/// </summary>
public readonly record struct EntityId(Guid Value) : IComparable<EntityId>, IComparable
{
    public static EntityId New() => new(Guid.NewGuid());

    /// <summary>
    /// Ordering by id is never meaningful on its own, but it is the standard deterministic
    /// tie-break behind a real sort key (a paged list ordered by <c>CreatedAt</c> needs one, or two
    /// rows written in the same instant can swap places between pages). Delegates to
    /// <see cref="Guid"/>'s own comparison; declared explicitly because a struct without
    /// <see cref="IComparable{T}"/> makes an in-memory <c>ThenBy(x =&gt; x.Id)</c> throw
    /// "At least one object must implement IComparable" (task E13/F04/US01/T02).
    /// </summary>
    public int CompareTo(EntityId other) => Value.CompareTo(other.Value);

    int IComparable.CompareTo(object? obj) => obj switch
    {
        null => 1,
        EntityId other => CompareTo(other),
        _ => throw new ArgumentException($"Object must be of type {nameof(EntityId)}.", nameof(obj)),
    };

    public override string ToString() => Value.ToString();
}
