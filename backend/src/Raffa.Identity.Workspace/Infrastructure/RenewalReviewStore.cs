using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;

namespace Raffa.Identity.Workspace.Infrastructure;

/// <summary>One "Free renewal review" lead from the marketing landing page (unmapped, snake_case aliases).</summary>
public sealed class RenewalReviewRequestRow
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Company { get; set; } = string.Empty;
    public string RenewalTiming { get; set; } = string.Empty;
    public bool Consent { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public string Source { get; set; } = RenewalReviewStore.LandingSource;
}

/// <summary>
/// Platform-level storage of the landing page's renewal-review leads (<c>renewal_review_request</c>, see
/// <c>AddRenewalReviewRequests</c>). No tenant content, no RLS, no IP or user agent. On a non-Postgres provider
/// (the in-memory test host, where the table does not exist) a process-local copy stands in so the endpoints
/// stay testable, as in <see cref="PlaygroundStore"/>.
/// </summary>
public sealed class RenewalReviewStore(IdentityWorkspaceDbContext db)
{
    public const string LandingSource = "landing";
    public const int MaxListed = 500;

    private static readonly ConcurrentDictionary<Guid, RenewalReviewRequestRow> LocalRequests = new();

    private bool IsPostgres => db.Database.ProviderName?.Contains("Npgsql", StringComparison.OrdinalIgnoreCase) == true;

    public async Task SaveAsync(RenewalReviewRequestRow request, CancellationToken ct = default)
    {
        if (!IsPostgres)
        {
            LocalRequests[request.Id] = request;
            return;
        }

        await db.Database.ExecuteSqlInterpolatedAsync(
            $"""
            INSERT INTO renewal_review_request (id, name, email, company, renewal_timing, consent, created_at, source)
            VALUES ({request.Id}, {request.Name}, {request.Email}, {request.Company}, {request.RenewalTiming},
                    {request.Consent}, {request.CreatedAt.ToUniversalTime()}, {request.Source})
            """, ct).ConfigureAwait(false);
    }

    /// <summary>Newest first, at most <see cref="MaxListed"/>.</summary>
    public async Task<List<RenewalReviewRequestRow>> ListAsync(CancellationToken ct = default)
    {
        if (!IsPostgres)
        {
            return [.. LocalRequests.Values.OrderByDescending(r => r.CreatedAt).ThenByDescending(r => r.Id).Take(MaxListed)];
        }

        return await db.Database.SqlQuery<RenewalReviewRequestRow>(
            $"""
            SELECT id AS "id", name AS "name", email AS "email", company AS "company",
                   renewal_timing AS "renewal_timing", consent AS "consent", created_at AS "created_at", source AS "source"
            FROM renewal_review_request ORDER BY created_at DESC, id DESC LIMIT {MaxListed}
            """).ToListAsync(ct).ConfigureAwait(false);
    }
}
