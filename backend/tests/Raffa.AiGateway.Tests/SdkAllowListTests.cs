using System.Xml.Linq;

namespace Raffa.AiGateway.Tests;

/// <summary>
/// AC-3 / task E13/F01/US01/T02's own Definition of Done: "SDK allow-list test green (only
/// Raffa.AiGateway.csproj references Azure.AI.* / Azure.Identity)". Scans every <c>*.csproj</c>
/// under the solution root by recursive file search rather than a hand-maintained project list —
/// deliberately, since T01 (this same phase, running in parallel) adds new projects
/// (<c>Raffa.Market</c>, <c>Raffa.Insights</c>, <c>Raffa.AiEval</c>, ...) this test must
/// still cover without anyone remembering to update a list here.
///
/// Narrower than <c>Raffa.ArchitectureTests.DependencyDirectionTests
/// .Domain_module_must_not_reference_provider_sdks</c> (that test's own <c>ForbiddenSdkPrefixes</c>
/// blocks the broad <c>"Azure."</c> prefix, but only across the fixed ADR-002 domain-module list —
/// it does not, and should not, forbid <c>Raffa.Api</c>'s legitimate storage/transport references
/// for ADR-005). This test is checked across every project in the solution, hosts and tests
/// included, and is therefore the one guard that reaches a <b>host</b> adapter.
///
/// <para>
/// Task E17/F01/US01/T01 (wave w15; ADR-025 §J.1d, ADR-026 w15 footer §2/§10): the allow-list is a
/// <b>package-scoped (prefix → allowed projects) map</b>, never a wider project skip. Widening the
/// old single <c>AllowedProjectName</c> — the one-word edit a task reaches for — would have made
/// <c>Azure.AI.*</c> legal in <c>Raffa.Api</c>, silently un-guarding the Foundry boundary ADR-004/
/// ADR-017 rest on. So: <c>Azure.AI.*</c> stays <c>Raffa.AiGateway</c>-only; <c>Azure.Identity</c>
/// (DefaultAzureCredential) is additionally permitted in <c>Raffa.Api</c> (the Graph guest
/// provisioner, ADR-025 §J.1a) and <c>Raffa.Worker</c> (the Service Bus consumer's managed-identity
/// auth, ADR-027 §D10) and, since the Service Bus transport lives in its own project,
/// <c>Raffa.Messaging</c>; <c>Microsoft.Graph</c> — the directory-write capability — is legal in
/// <c>Raffa.Api</c> and nowhere else, so a second call site is a build failure (ADR-025 §J.1c.1).
/// </para>
/// </summary>
public class SdkAllowListTests
{
    /// <summary>Prefix → the only projects allowed to reference a package with that prefix.</summary>
    private static readonly IReadOnlyDictionary<string, string[]> AllowedProjectsByPrefix = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
    {
        ["Azure.AI."] = ["Raffa.AiGateway"],
        ["Azure.Identity"] = ["Raffa.AiGateway", "Raffa.Api", "Raffa.Worker", "Raffa.Messaging"],
        ["Microsoft.Graph"] = ["Raffa.Api"],
    };

    [Fact]
    public void Provider_sdks_are_referenced_only_by_the_projects_the_allow_list_names()
    {
        var solutionRoot = FindSolutionRoot();
        var csprojFiles = Directory.GetFiles(solutionRoot, "*.csproj", SearchOption.AllDirectories);

        Assert.NotEmpty(csprojFiles);

        var violations = new List<string>();

        foreach (var csprojFile in csprojFiles)
        {
            var projectName = Path.GetFileNameWithoutExtension(csprojFile);

            foreach (var package in GetPackageReferenceNames(csprojFile))
            {
                foreach (var (prefix, allowedProjects) in AllowedProjectsByPrefix)
                {
                    if (package.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
                        && !allowedProjects.Contains(projectName, StringComparer.Ordinal))
                    {
                        violations.Add($"{projectName}: {package}");
                    }
                }
            }
        }

        Assert.True(
            violations.Count == 0,
            "[AC-3 / task E13/F01/US01/T02; ADR-025 §J.1d] Projects reference a provider SDK the allow-list " +
            $"does not grant them: {string.Join("; ", violations)}. Azure.AI.* is Raffa.AiGateway's alone " +
            "(domain code and hosts call Raffa.AiGateway.IAiGateway instead), Azure.Identity is confined to " +
            "the hosts that authenticate as the workload identity, and Microsoft.Graph to Raffa.Api's one " +
            "GraphGuestProvisioner call site.");
    }

    [Fact]
    public void Azure_AI_stays_illegal_in_Raffa_Api_even_though_Azure_Identity_is_allowed_there()
    {
        // The non-vacuity check for the map's whole point: the two prefixes share a project only
        // where the map says so. If a later edit collapses the map back into one project skip, this
        // is the assertion that notices.
        Assert.DoesNotContain("Raffa.Api", AllowedProjectsByPrefix["Azure.AI."]);
        Assert.Contains("Raffa.Api", AllowedProjectsByPrefix["Azure.Identity"]);
        Assert.Equal(["Raffa.Api"], AllowedProjectsByPrefix["Microsoft.Graph"]);
    }

    [Fact]
    public void Raffa_AiGateway_itself_references_Azure_Identity_for_managed_identity_auth()
    {
        var solutionRoot = FindSolutionRoot();
        var csprojFile = Path.Combine(solutionRoot, "src", "Raffa.AiGateway", "Raffa.AiGateway.csproj");

        Assert.True(File.Exists(csprojFile), $"Project file not found: {csprojFile}");

        var packageReferences = GetPackageReferenceNames(csprojFile);

        // A sanity check that this allow-list test is not vacuously true — Raffa.AiGateway is
        // supposed to be the one place DefaultAzureCredential (task's own "auth via
        // DefaultAzureCredential..., never a key") is reachable from for Foundry.
        Assert.Contains(
            packageReferences, pkg => pkg.StartsWith("Azure.Identity", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Raffa_Api_is_the_one_Microsoft_Graph_call_site()
    {
        var solutionRoot = FindSolutionRoot();
        var csprojFile = Path.Combine(solutionRoot, "src", "Raffa.Api", "Raffa.Api.csproj");

        Assert.True(File.Exists(csprojFile), $"Project file not found: {csprojFile}");
        Assert.Contains(
            GetPackageReferenceNames(csprojFile), pkg => pkg.StartsWith("Microsoft.Graph", StringComparison.OrdinalIgnoreCase));
    }

    private static List<string> GetPackageReferenceNames(string csprojPath)
    {
        var doc = XDocument.Load(csprojPath);
        return doc.Descendants("PackageReference")
            .Select(e => e.Attribute("Include")?.Value)
            .Where(v => v is not null)
            .Select(v => v!)
            .ToList();
    }

    private static string FindSolutionRoot()
    {
        var assemblyLocation = typeof(SdkAllowListTests).Assembly.Location;
        var dir = new DirectoryInfo(Path.GetDirectoryName(assemblyLocation)!);

        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Raffa.slnx")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName
            ?? throw new InvalidOperationException(
                "Could not find solution root (looking for Raffa.slnx). " +
                $"Started from: {assemblyLocation}");
    }
}
