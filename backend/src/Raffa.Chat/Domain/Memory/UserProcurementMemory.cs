using Raffa.SharedKernel;
using Raffa.Chat.Domain.Conversations;

namespace Raffa.Chat.Domain.Memory;

/// <summary>One privacy-bounded cross-conversation memory profile per tenant and user.</summary>
public sealed class UserProcurementMemory : TenantScopedEntity
{
    public required string UserId { get; set; }
    public required string ProfileJson { get; set; }
    public required DateTimeOffset UpdatedAt { get; set; }
    public required DateTimeOffset ExpiresAt { get; set; }
}
