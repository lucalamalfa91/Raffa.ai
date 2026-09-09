using System.Xml.Linq;

namespace Contigo.AiGateway.Tests;

/// <summary>
/// AC-3 / task E13/F01/US01/T02's own Definition of Done: "SDK allow-list test green (only
/// Contigo.AiGateway.csproj references Azure.AI.* / Azure.Identity)". Scans every <c>*.csproj</c>
/// under the solution root by recursive file search rather than a hand-maintained project list —
/// deliberately, since T01 (this same phase, running in parallel) adds new projects
/// (<c>Contigo.Market</c>, <c>Contigo.Insights</c>, <c>Contigo.AiEval</c>, ...) this test must
/// still cover without anyone remembering to update a list here.
///
/// Narrower than <c>Contigo.ArchitectureTests.DependencyDirectionTests
/// .Domain_module_must_not_reference_provider_sdks</c> (that test's own <c>ForbiddenSdkPrefixes</c>
/// blocks the broad <c>"Azure."</c> prefix, but only across the fixed ADR-002 domain-module list —
/// it does not, and should not, forbid <c>Contigo.Api</c>'s legitimate <c>Azure.Storage.Blobs</c>
/// reference for ADR-005 object storage). This test is scoped to exactly the two SDK-family
/// prefixes this task's own Foundry auth needs (<c>Azure.AI.*</c>, <c>Azure.Identity</c>), checked
/// across every project in the solution, hosts and tests included.
/// </summary>
public class SdkAllowListTests
{
    private static readonly string[] ForbiddenPrefixes = ["Azure.AI.", "Azure.Identity"];

    private const string AllowedProjectName = "Contigo.AiGateway";

    [Fact]
    public void No_project_other_than_Contigo_AiGateway_references_an_Azure_AI_SDK_or_Azure_Identity()
    {
        var solutionRoot = FindSolutionRoot();
        var csprojFiles = Directory.GetFiles(solutionRoot, "*.csproj", SearchOption.AllDirectories);

        Assert.NotEmpty(csprojFiles);

        var violations = new List<string>();

        foreach (var csprojFile in csprojFiles)
        {
            var projectName = Path.GetFileNameWithoutExtension(csprojFile);
            if (string.Equals(projectName, AllowedProjectName, StringComparison.Ordinal))
            {
                continue;
            }

            var forbidden = GetPackageReferenceNames(csprojFile)
                .Where(pkg => ForbiddenPrefixes.Any(prefix => pkg.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))
                .ToList();

            if (forbidden.Count > 0)
            {
                violations.Add($"{projectName}: [{string.Join(", ", forbidden)}]");
            }
        }

        Assert.True(
            violations.Count == 0,
            "[AC-3 / task E13/F01/US01/T02] Projects outside Contigo.AiGateway reference an Azure " +
            $"AI SDK or Azure.Identity directly: {string.Join("; ", violations)}. Domain code and " +
            "hosts must call Contigo.AiGateway.IAiGateway instead of a provider SDK.");
    }

    [Fact]
    public void Contigo_AiGateway_itself_references_Azure_Identity_for_managed_identity_auth()
    {
        var solutionRoot = FindSolutionRoot();
        var csprojFile = Path.Combine(solutionRoot, "src", "Contigo.AiGateway", "Contigo.AiGateway.csproj");

        Assert.True(File.Exists(csprojFile), $"Project file not found: {csprojFile}");

        var packageReferences = GetPackageReferenceNames(csprojFile);

        // A sanity check that this allow-list test is not vacuously true — Contigo.AiGateway is
        // supposed to be the one place DefaultAzureCredential (task's own "auth via
        // DefaultAzureCredential..., never a key") is reachable from.
        Assert.Contains(
            packageReferences, pkg => pkg.StartsWith("Azure.Identity", StringComparison.OrdinalIgnoreCase));
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

        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Contigo.slnx")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName
            ?? throw new InvalidOperationException(
                "Could not find solution root (looking for Contigo.slnx). " +
                $"Started from: {assemblyLocation}");
    }
}
