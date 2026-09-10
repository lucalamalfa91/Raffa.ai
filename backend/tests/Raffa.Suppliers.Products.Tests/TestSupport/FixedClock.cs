using Raffa.SharedKernel;

namespace Raffa.Suppliers.Products.Tests.TestSupport;

/// <summary>
/// Fixed <see cref="IClock"/> for deterministic <see cref="Raffa.Suppliers.Products.Application.SupplierResolver"/>
/// <c>CreatedAt</c>/<c>UpdatedAt</c> assertions. <c>SystemClock</c>'s own doc comment notes "every
/// test project in this solution already follows that pattern with its own local fake rather than
/// [SystemClock]" — this is this project's copy of that pattern (mirrors
/// <c>Raffa.Renewals.Tests.TestSupport.FixedClock</c>).
/// </summary>
public sealed class FixedClock(DateTimeOffset now) : IClock
{
    public DateTimeOffset UtcNow { get; } = now;
}
