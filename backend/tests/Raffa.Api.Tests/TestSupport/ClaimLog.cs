using Raffa.SharedKernel;

namespace Raffa.Api.Tests.TestSupport;

/// <summary>Task E16/F03/US02/T01: the order in which the in-memory claim store handed out claims,
/// across every DI scope the drain creates (the store itself is scoped, one per message). What a
/// priority test asserts on -- the host runs a FixedClock, so timestamps cannot tell first from
/// second.</summary>
internal sealed class ClaimLog
{
    private readonly List<EntityId> _claimed = [];

    public IReadOnlyList<EntityId> ClaimedInOrder => _claimed;

    public void Record(EntityId jobId)
    {
        lock (_claimed)
        {
            _claimed.Add(jobId);
        }
    }
}
