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

    // ------- helpers -------

    private static string WriteTempCsproj(IEnumerable<string> projectReferences, IEnumerable<string> packageReferences)
    {
        var projectRefXml = string.Concat(projectReferences.Select(name =>
            $"<ProjectReference Include=\"..\\..\\src\\{name}\\{name}.csproj\" />"));
        var packageRefXml = string.Concat(packageReferences.Select(name =>
            $"<PackageReference Include=\"{name}\" Version=\"1.0.0\" />"));

        var xml =
            "<Project Sdk=\"Microsoft.NET.Sdk\">" +
            "<PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup>" +
            "<ItemGroup>" + projectRefXml + "</ItemGroup>" +
            "<ItemGroup>" + packageRefXml + "</ItemGroup>" +
            "</Project>";

        var path = Path.Combine(Path.GetTempPath(), $"synthetic-{Guid.NewGuid():N}.csproj");
        File.WriteAllText(path, xml);
        return path;
    }
}
