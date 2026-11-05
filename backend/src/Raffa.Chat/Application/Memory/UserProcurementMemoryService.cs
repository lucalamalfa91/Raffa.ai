using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Raffa.Chat.Application.Conversations;
using Raffa.Chat.Domain.Memory;
using Raffa.Chat.Infrastructure;
using Raffa.SharedKernel;
using Raffa.SharedKernel.Tenancy;

namespace Raffa.Chat.Application.Memory;

/// <summary>Learns only allowlisted behavioural counters; no prompts, answers, amounts or document text.</summary>
public sealed class UserProcurementMemoryService(
    ChatDbContext db, ITenantContext tenantContext, IClock clock, IAuditWriter audit)
{
    public static readonly TimeSpan Retention = TimeSpan.FromDays(180);
    private const int SuggestionThreshold = 3;

    public async Task<UserMemorySnapshot> GetAsync(TenantId tenantId, string userId, IReadOnlyCollection<string>? authorisedSuppliers = null, string language = "it", CancellationToken ct = default)
    {
        using var scope = tenantContext.BeginScope(tenantId);
        var row = await db.UserProcurementMemories.AsNoTracking().SingleOrDefaultAsync(
            x => x.TenantId == tenantId && x.UserId == userId, ct).ConfigureAwait(false);
        if (row is null || row.ExpiresAt <= clock.UtcNow) return UserMemorySnapshot.Empty;
        var profile = UserMemoryProfile.Parse(row.ProfileJson);
        if (!profile.Enabled) return UserMemorySnapshot.Empty;
        var supplierSet = authorisedSuppliers is null ? null : new HashSet<string>(authorisedSuppliers, StringComparer.OrdinalIgnoreCase);
        var suppliers = supplierSet is null ? [] : (profile.RecentSuppliers ?? []).Where(supplierSet.Contains).Take(5).ToList();
        var workflows = Top(profile.WorkflowCounts).ToList();
        var intents = Top(profile.IntentCounts).ToList();
        var suggestions = UserMemorySnapshot.SuggestionsFor(workflows, language);
        return new UserMemorySnapshot(profile.PreferredOutput, workflows, intents, suppliers, suggestions, row.ExpiresAt);
    }

    public async Task ObserveAsync(TenantId tenantId, string userId, ConversationState state, string question, CancellationToken ct = default)
    {
        using var scope = tenantContext.BeginScope(tenantId);
        var row = await db.UserProcurementMemories.SingleOrDefaultAsync(
            x => x.TenantId == tenantId && x.UserId == userId, ct).ConfigureAwait(false);
        var profile = UserMemoryProfile.Parse(row?.ProfileJson);
        // A deletion is an opt-out, not a temporary reset that silently starts learning again.
        if (row is not null && !profile.Enabled) return;
        var intents = new Dictionary<string, int>(profile.IntentCounts ?? new Dictionary<string, int>(), StringComparer.OrdinalIgnoreCase);
        var workflows = new Dictionary<string, int>(profile.WorkflowCounts ?? new Dictionary<string, int>(), StringComparer.OrdinalIgnoreCase);
        var transitions = new Dictionary<string, int>(profile.TransitionCounts ?? new Dictionary<string, int>(), StringComparer.OrdinalIgnoreCase);
        var workflow = ClassifyWorkflow(question);
        Increment(intents, state.LastIntent);
        Increment(workflows, workflow);
        if (profile.LastWorkflow is not null && workflow is not null) Increment(transitions, profile.LastWorkflow + "->" + workflow);
        var suppliers = (profile.RecentSuppliers ?? []).Where(x => !string.Equals(x, state.PrimarySupplier, StringComparison.OrdinalIgnoreCase)).ToList();
        if (state.PrimarySupplier is not null) suppliers.Insert(0, state.PrimarySupplier);
        var preferredOutput = ExplicitOutputPreference(question) ?? profile.PreferredOutput;
        var next = new UserMemoryProfile(intents, workflows, transitions, workflow ?? profile.LastWorkflow, preferredOutput, suppliers.Take(5).ToList());
        var now = clock.UtcNow;
        if (row is null)
        {
            row = new UserProcurementMemory { TenantId = tenantId, UserId = userId, ProfileJson = next.Serialize(), UpdatedAt = now, ExpiresAt = now + Retention };
            db.UserProcurementMemories.Add(row);
        }
        else
        {
            row.ProfileJson = next.Serialize(); row.UpdatedAt = now; row.ExpiresAt = now + Retention;
        }
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        await audit.WriteAsync(new AuditEntry(
            tenantId, userId, "chat.memory.updated", "user_procurement_memory", row.Id.Value.ToString(), clock.UtcNow), ct).ConfigureAwait(false);
    }

    public async Task<bool> DeleteAsync(TenantId tenantId, string userId, CancellationToken ct = default)
    {
        using var scope = tenantContext.BeginScope(tenantId);
        var row = await db.UserProcurementMemories.SingleOrDefaultAsync(x => x.TenantId == tenantId && x.UserId == userId, ct).ConfigureAwait(false);
        var existed = row is not null;
        var now = clock.UtcNow;
        if (row is null)
        {
            row = new UserProcurementMemory
            {
                TenantId = tenantId,
                UserId = userId,
                ProfileJson = (UserMemoryProfile.Empty with { Enabled = false }).Serialize(),
                UpdatedAt = now,
                ExpiresAt = now + Retention,
            };
            db.UserProcurementMemories.Add(row);
        }
        row.ProfileJson = (UserMemoryProfile.Empty with { Enabled = false }).Serialize();
        row.UpdatedAt = now;
        row.ExpiresAt = now + Retention;
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        await audit.WriteAsync(new AuditEntry(tenantId, userId, "chat.memory.deleted", "user_procurement_memory", row.Id.Value.ToString(), clock.UtcNow), ct).ConfigureAwait(false);
        return existed;
    }

    private static IEnumerable<string> Top(IReadOnlyDictionary<string, int>? values) =>
        (values ?? new Dictionary<string, int>()).Where(x => x.Value >= SuggestionThreshold).OrderByDescending(x => x.Value).ThenBy(x => x.Key).Select(x => x.Key);
    private static void Increment(IDictionary<string, int> values, string? key)
    {
        if (key is not null) values[key] = values.TryGetValue(key, out var count) ? count + 1 : 1;
    }
    private static string? ClassifyWorkflow(string q) => q.ToLowerInvariant() switch
    {
        var x when Regex.IsMatch(x, @"\b(mail|email|messaggio\s+al\s+fornitore|supplier\s+(?:email|message)|vendor\s+(?:email|message))\b") => "supplier_email",
        var x when Regex.IsMatch(x, @"\b(kpi|dashboard|piano\s+operativo|action\s+plan|artifact|document)\b") => "artifact",
        var x when Regex.IsMatch(x, @"\b(rfp|rfq|rfi|gara|tender)\b") => "rfx",
        var x when Regex.IsMatch(x, @"\b(benchmark|mercato|market|confront|compar)\b") => "benchmark",
        var x when Regex.IsMatch(x, @"\b(risparm|saving|savings|save|reduce\s+costs|cost\s+reduction|ridurre\s+i\s+costi)\b") => "savings",
        _ => null,
    };
    private static string? ExplicitOutputPreference(string q) => q.ToLowerInvariant() switch
    {
        var x when Regex.IsMatch(x, @"\b(sempre|di\s+solito|preferisco|always|usually|i\s+prefer)\b.*\b(sintetic|breve|conciso|concise|brief|short)\b") => "concise",
        var x when Regex.IsMatch(x, @"\b(sempre|di\s+solito|preferisco|always|usually|i\s+prefer)\b.*\b(dettagliat|approfondit|detailed|in-depth)\b") => "detailed",
        var x when Regex.IsMatch(x, @"\b(sempre|di\s+solito|preferisco|always|usually|i\s+prefer)\b.*\b(piano\s+operativo|azioni|action\s+plan|next\s+actions)\b") => "action_plan",
        _ => null,
    };
}
