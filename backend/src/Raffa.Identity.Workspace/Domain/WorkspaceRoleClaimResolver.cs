namespace Raffa.Identity.Workspace.Domain;

/// <summary>
/// Resolves a raw OIDC role-claim value to this catalog's <see cref="WorkspaceRoleName"/>
/// (ADR-010; story us-01-workspace-roles AC-3: "Role assignment resolves from OIDC claims
/// (Admin/Procurement/Legal/Finance/Read-only)").
///
/// Neither ADR-010 nor the product spec fixes the exact claim type or value string an Entra ID
/// token carries for a workspace role — ADR-010 only names <c>Raffa.Read</c>/<c>Raffa.Write</c>
/// as placeholder *scopes* (API permissions, a different concept from a workspace role) and says
/// the final shape is adopted "when the API surface... is fixed, without changing the registration
/// shape". Until that follow-up exists, this resolver accepts the reasonable set of shapes an
/// Entra ID App Role claim is actually emitted in: the bare enum name (<c>"Admin"</c>), the
/// product spec §3.1 label (<c>"Workspace Admin"</c>, <c>"Read-only / Business"</c>), and an
/// app-role value namespaced to this API (<c>"Raffa.Admin"</c>), each case- and
/// punctuation-insensitive. This is this task's own decision, not a re-litigation of ADR-010 — the
/// five role *names* themselves are locked (spec §3.1); only the claim string spelling was left
/// open, and every alias below still maps to exactly the same <see cref="WorkspaceRoleName"/>
/// five-value catalog ADR-010/<see cref="WorkspaceFactory"/> already fixed.
/// </summary>
public static class WorkspaceRoleClaimResolver
{
    /// <summary>
    /// Precedence used when more than one recognized role claim is present on the same identity:
    /// most-privileged first (spec §3.1's own row order — Workspace Admin manages "users, roles...
    /// all contracts, audit logs", the broadest grant of the five).
    /// </summary>
    private static readonly WorkspaceRoleName[] Precedence =
    [
        WorkspaceRoleName.Admin,
        WorkspaceRoleName.Procurement,
        WorkspaceRoleName.Legal,
        WorkspaceRoleName.Finance,
        WorkspaceRoleName.ReadOnly,
    ];

    private static readonly IReadOnlyDictionary<string, WorkspaceRoleName> AliasesByNormalizedValue =
        BuildAliasLookup();

    /// <summary>Resolves a single raw claim value (e.g. one Entra App Role claim).</summary>
    public static bool TryResolve(string? claimValue, out WorkspaceRoleName role)
    {
        if (!string.IsNullOrWhiteSpace(claimValue) &&
            AliasesByNormalizedValue.TryGetValue(Normalize(claimValue), out role))
        {
            return true;
        }

        role = default;
        return false;
    }

    /// <summary>
    /// Resolves the highest-precedence recognized role across every claim value an identity
    /// carries (an Entra ID token repeats the <c>roles</c> claim once per assigned app role).
    /// Returns <see langword="false"/> when none of the supplied values is recognized (including
    /// an empty collection).
    /// </summary>
    public static bool TryResolve(IEnumerable<string> claimValues, out WorkspaceRoleName role)
    {
        var resolved = new HashSet<WorkspaceRoleName>();
        foreach (var claimValue in claimValues)
        {
            if (TryResolve(claimValue, out var candidate))
            {
                resolved.Add(candidate);
            }
        }

        foreach (var candidate in Precedence)
        {
            if (resolved.Contains(candidate))
            {
                role = candidate;
                return true;
            }
        }

        role = default;
        return false;
    }

    private static string Normalize(string value)
    {
        // Strip an app-role namespace prefix (e.g. "Raffa.Admin" -> "Admin"), then fold to
        // lowercase letters only so "Workspace Admin", "workspace-admin" and "WORKSPACE_ADMIN"
        // all collapse to the same key as "Admin".
        var withoutNamespace = value.Contains('.') ? value[(value.LastIndexOf('.') + 1)..] : value;
        var letters = withoutNamespace.Where(char.IsLetter).Select(char.ToLowerInvariant).ToArray();
        return new string(letters);
    }

    private static Dictionary<string, WorkspaceRoleName> BuildAliasLookup()
    {
        var aliasesByRole = new Dictionary<WorkspaceRoleName, string[]>
        {
            [WorkspaceRoleName.Admin] = ["Admin", "Workspace Admin"],
            [WorkspaceRoleName.Procurement] = ["Procurement"],
            [WorkspaceRoleName.Legal] = ["Legal"],
            [WorkspaceRoleName.Finance] = ["Finance"],
            [WorkspaceRoleName.ReadOnly] = ["ReadOnly", "Read-only", "Read-only / Business", "Business"],
        };

        // Indexer assignment (not Dictionary.Add): two aliases for the same role can normalize to
        // the same key (e.g. "ReadOnly" and "Read-only" both fold to "readonly") — that is a
        // harmless overwrite with the same value, not a real collision, so it must not throw.
        var lookup = new Dictionary<string, WorkspaceRoleName>();
        foreach (var (role, aliases) in aliasesByRole)
        {
            foreach (var alias in aliases)
            {
                lookup[Normalize(alias)] = role;
            }
        }

        return lookup;
    }
}
