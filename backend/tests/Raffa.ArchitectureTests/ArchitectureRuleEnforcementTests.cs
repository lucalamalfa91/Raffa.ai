namespace Raffa.ArchitectureTests;

/// <summary>
/// Proves the ADR-002 dependency-direction rule actually blocks a violation, not just that
/// today's already-clean `.csproj` files happen to carry none.
/// <see cref="DependencyDirectionTests"/> only asserts the current, real project files have zero
/// violations. That assertion would stay green even if the detection logic itself were broken
/// (for example an empty forbidden-prefix list trivially "passes"). These tests exercise the same
/// detection helpers and fixtures — <see cref="DependencyDirectionTests.GetProjectReferenceNames"/>,
/// <see cref="DependencyDirectionTests.GetPackageReferenceNames"/>,
/// <see cref="DependencyDirectionTests.AllowedReferences"/> and
/// <see cref="DependencyDirectionTests.ForbiddenSdkPrefixes"/> — against synthetic, deliberately
/// bad and deliberately clean csproj content, so the rule is proven to fail the build on a real
/// violation (task-02 objective) and to stay quiet on a compliant module (no false positive).
/// </summary>
public class ArchitectureRuleEnforcementTests
{
    [Fact]
    public void Detects_domain_module_referencing_another_domain_modules_internals()
    {
        // Raffa.Identity.Workspace may only reference Raffa.SharedKernel (ADR-002).
        // A reference to Raffa.Renewals — another domain module's internals — must be flagged.
        var csprojPath = WriteTempCsproj(
            projectReferences: ["Raffa.SharedKernel", "Raffa.Renewals"],
            packageReferences: []);

        try
        {
            var refs = DependencyDirectionTests.GetProjectReferenceNames(csprojPath);
            var allowed = DependencyDirectionTests.AllowedReferences["Raffa.Identity.Workspace"];

            var violations = refs
                .Where(r => DependencyDirectionTests.AllRaffaProjects.Contains(r) && !allowed.Contains(r))
                .ToList();

            Assert.Equal(["Raffa.Renewals"], violations);
        }
        finally
        {
            File.Delete(csprojPath);
        }
    }

    [Fact]
    public void Detects_domain_module_referencing_a_provider_sdk_package()
    {
        var csprojPath = WriteTempCsproj(
            projectReferences: ["Raffa.SharedKernel"],
            packageReferences: ["Azure.Storage.Blobs"]);

        try
        {
            var packageRefs = DependencyDirectionTests.GetPackageReferenceNames(csprojPath);

            var forbidden = packageRefs
                .Where(pkg => DependencyDirectionTests.ForbiddenSdkPrefixes.Any(prefix =>
                    pkg.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))
                .ToList();

            Assert.Equal(["Azure.Storage.Blobs"], forbidden);
        }
        finally
        {
            File.Delete(csprojPath);
        }
    }

    [Fact]
    public void Allows_a_clean_reference_set_with_no_violations()
    {
        // Control case: SharedKernel + the module's own allowed gateway interface, plus an
        // unrelated non-provider package. The detector must report zero violations, proving the
        // rule does not over-trigger on a compliant module.
        var csprojPath = WriteTempCsproj(
            projectReferences: ["Raffa.SharedKernel", "Raffa.AiGateway"],
            packageReferences: ["Microsoft.Extensions.Logging.Abstractions"]);

        try
        {
            var refs = DependencyDirectionTests.GetProjectReferenceNames(csprojPath);
            var allowed = DependencyDirectionTests.AllowedReferences["Raffa.Documents.Contracts"];
            var projectViolations = refs
                .Where(r => DependencyDirectionTests.AllRaffaProjects.Contains(r) && !allowed.Contains(r))
                .ToList();

            var packageRefs = DependencyDirectionTests.GetPackageReferenceNames(csprojPath);
            var packageViolations = packageRefs
                .Where(pkg => DependencyDirectionTests.ForbiddenSdkPrefixes.Any(prefix =>
                    pkg.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))
                .ToList();

            Assert.Empty(projectViolations);
            Assert.Empty(packageViolations);
        }
        finally
        {
            File.Delete(csprojPath);
        }
    }

    // ------- AI flows layer (ADR-002 amendment) -------

    [Fact]
    public void Detects_domain_module_referencing_AiFlows()
    {
        // Raffa.Chat may reference only SharedKernel and AiGateway. The AI flows layer sits above
        // the modules, so a module that takes a reference to it (a dependency cycle in waiting)
        // must be flagged by the same filter Domain_module_project_references_follow_allowed_direction
        // uses. This also proves Raffa.AiFlows is in AllRaffaProjects: had it been left out, the
        // filter would drop the reference and the violation would go unseen.
        var csprojPath = WriteTempCsproj(
            projectReferences: ["Raffa.SharedKernel", "Raffa.AiGateway", "Raffa.AiFlows"],
            packageReferences: []);

        try
        {
            var refs = DependencyDirectionTests.GetProjectReferenceNames(csprojPath);
            var allowed = DependencyDirectionTests.AllowedReferences["Raffa.Chat"];

            var violations = refs
                .Where(r => DependencyDirectionTests.AllRaffaProjects.Contains(r) && !allowed.Contains(r))
                .ToList();

            Assert.Equal(["Raffa.AiFlows"], violations);
        }
        finally
        {
            File.Delete(csprojPath);
        }
    }

    [Fact]
    public void No_domain_module_is_allowed_to_reference_AiFlows()
    {
        // The allow-list itself must never grow a Raffa.AiFlows entry: it is the rule's source of
        // truth, so a well-meaning edit there would silently legalise the cycle.
        foreach (var (module, allowed) in DependencyDirectionTests.AllowedReferences)
        {
            Assert.DoesNotContain(DependencyDirectionTests.AiFlows, allowed);
            Assert.NotEqual(DependencyDirectionTests.AiFlows, module);
        }

        Assert.Contains(DependencyDirectionTests.AiFlows, DependencyDirectionTests.AllRaffaProjects);
    }

    [Fact]
    public void Detects_a_non_host_project_referencing_AiFlows()
    {
        // Only_hosts_reference_AiFlows scans every src/*/*.csproj, including projects outside the
        // domain-module set (Messaging here). Build a fake src tree to prove the scan flags the
        // non-host and lets the two hosts through.
        var srcRoot = Path.Combine(Path.GetTempPath(), $"synthetic-src-{Guid.NewGuid():N}");

        try
        {
            foreach (var (name, referencesAiFlows) in new[]
            {
                ("Raffa.Api", true),
                ("Raffa.Worker", true),
                ("Raffa.Messaging", true),
                ("Raffa.Storage", false),
            })
            {
                var dir = Path.Combine(srcRoot, name);
                Directory.CreateDirectory(dir);
                File.WriteAllText(
                    Path.Combine(dir, $"{name}.csproj"),
                    TempCsprojXml(referencesAiFlows ? ["Raffa.SharedKernel", "Raffa.AiFlows"] : ["Raffa.SharedKernel"], []));
            }

            Assert.Equal(["Raffa.Messaging"], DependencyDirectionTests.FindAiFlowsConsumerViolations(srcRoot));
        }
        finally
        {
            Directory.Delete(srcRoot, recursive: true);
        }
    }

    [Fact]
    public void Detects_AiFlows_referencing_a_host_or_a_project_outside_the_allow_list()
    {
        // Messaging and Storage are not in AllRaffaProjects, so the check must not rely on that set.
        var csprojPath = WriteTempCsproj(
            projectReferences:
            [
                "Raffa.SharedKernel", "Raffa.AiGateway", "Raffa.Chat", "Raffa.Market",
                "Raffa.Api", "Raffa.Worker", "Raffa.Tools", "Raffa.Messaging", "Raffa.Storage",
            ],
            packageReferences: []);

        try
        {
            Assert.Equal(
                ["Raffa.Api", "Raffa.Worker", "Raffa.Tools", "Raffa.Messaging", "Raffa.Storage"],
                DependencyDirectionTests.FindAiFlowsProjectReferenceViolations(csprojPath));
        }
        finally
        {
            File.Delete(csprojPath);
        }
    }

    [Fact]
    public void Detects_AiFlows_referencing_provider_sdk_or_persistence_packages()
    {
        var csprojPath = WriteTempCsproj(
            projectReferences: ["Raffa.SharedKernel"],
            packageReferences:
            [
                "Azure.AI.OpenAI",
                "Microsoft.EntityFrameworkCore.Design",
                "Npgsql.EntityFrameworkCore.PostgreSQL",
                "Pgvector",
                "EFCore.NamingConventions",
                "Microsoft.Extensions.Options",
            ]);

        try
        {
            Assert.Equal(
                [
                    "Azure.AI.OpenAI",
                    "Microsoft.EntityFrameworkCore.Design",
                    "Npgsql.EntityFrameworkCore.PostgreSQL",
                    "Pgvector",
                    "EFCore.NamingConventions",
                ],
                DependencyDirectionTests.FindForbiddenAiFlowsPackages(csprojPath));
        }
        finally
        {
            File.Delete(csprojPath);
        }
    }

    [Fact]
    public void Allows_a_clean_AiFlows_reference_set_with_no_violations()
    {
        var csprojPath = WriteTempCsproj(
            projectReferences: [.. DependencyDirectionTests.AiFlowsAllowedReferences],
            packageReferences:
            [
                "Microsoft.Extensions.DependencyInjection.Abstractions",
                "Microsoft.Extensions.Options",
                "Microsoft.Extensions.Configuration.Binder",
                "Microsoft.Extensions.Logging.Abstractions",
            ]);

        try
        {
            Assert.Empty(DependencyDirectionTests.FindAiFlowsProjectReferenceViolations(csprojPath));
            Assert.Empty(DependencyDirectionTests.FindForbiddenAiFlowsPackages(csprojPath));
        }
        finally
        {
            File.Delete(csprojPath);
        }
    }

    // ------- helpers -------

    private static string WriteTempCsproj(IEnumerable<string> projectReferences, IEnumerable<string> packageReferences)
    {
        var path = Path.Combine(Path.GetTempPath(), $"synthetic-{Guid.NewGuid():N}.csproj");
        File.WriteAllText(path, TempCsprojXml(projectReferences, packageReferences));
        return path;
    }

    private static string TempCsprojXml(IEnumerable<string> projectReferences, IEnumerable<string> packageReferences)
    {
        var projectRefXml = string.Concat(projectReferences.Select(name =>
            $"<ProjectReference Include=\"..\\..\\src\\{name}\\{name}.csproj\" />"));
        var packageRefXml = string.Concat(packageReferences.Select(name =>
            $"<PackageReference Include=\"{name}\" Version=\"1.0.0\" />"));

        return
            "<Project Sdk=\"Microsoft.NET.Sdk\">" +
            "<PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup>" +
            "<ItemGroup>" + projectRefXml + "</ItemGroup>" +
            "<ItemGroup>" + packageRefXml + "</ItemGroup>" +
            "</Project>";
    }
}
