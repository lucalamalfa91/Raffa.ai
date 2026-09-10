using System.Xml.Linq;

namespace Raffa.ArchitectureTests;

/// <summary>
/// Architecture tests that enforce the dependency direction rules from ADR-002:
///   - Domain modules reference only SharedKernel and (where needed) AI Gateway / Benchmark interfaces.
///   - Domain modules never reference other domain modules, provider SDKs, or host projects.
///   - Host projects (Api, Worker) are thin composition roots with no business-logic types.
/// These tests inspect .csproj project/package references (structural, source-of-truth)
/// and assembly metadata (runtime). A violation fails the build.
/// </summary>
public class DependencyDirectionTests
{
    private static readonly string SolutionRoot = FindSolutionRoot();

    /// <summary>Domain modules per ADR-002 module-map.</summary>
    private static readonly string[] DomainModules =
    [
        "Raffa.Identity.Workspace",
        "Raffa.Documents.Contracts",
        "Raffa.Suppliers.Products",
        "Raffa.Renewals",
        "Raffa.Savings",
        "Raffa.Quotes",
        "Raffa.Chat",
        "Raffa.Audit",
        // ADR-024 module-map delta (task E13/F01/US01/T01, v2-scaffold): Market and Insights
        // join the domain-module set so this theory's dependency-direction / provider-SDK
        // checks cover them too.
        "Raffa.Market",
        "Raffa.Insights",
    ];

    /// <summary>All Raffa project names (used to filter Raffa-internal references).</summary>
    internal static readonly HashSet<string> AllRaffaProjects =
    [
        "Raffa.SharedKernel",
        "Raffa.Identity.Workspace",
        "Raffa.Documents.Contracts",
        "Raffa.Suppliers.Products",
        "Raffa.Renewals",
        "Raffa.Savings",
        "Raffa.Quotes",
        "Raffa.Chat",
        "Raffa.Benchmark",
        "Raffa.AiGateway",
        "Raffa.Audit",
        "Raffa.Api",
        "Raffa.Worker",
        "Raffa.Market",
        "Raffa.Insights",
    ];

    /// <summary>
    /// Allowed Raffa project references per domain module.
    /// SharedKernel is universal; AI Gateway allowed for Documents/Contracts + Chat;
    /// Benchmark allowed for Renewals + Savings + Quotes.
    /// </summary>
    internal static readonly Dictionary<string, HashSet<string>> AllowedReferences = new()
    {
        ["Raffa.Identity.Workspace"]   = ["Raffa.SharedKernel"],
        ["Raffa.Documents.Contracts"]  = ["Raffa.SharedKernel", "Raffa.AiGateway"],
        ["Raffa.Suppliers.Products"]   = ["Raffa.SharedKernel"],
        ["Raffa.Renewals"]             = ["Raffa.SharedKernel", "Raffa.Benchmark"],
        ["Raffa.Savings"]              = ["Raffa.SharedKernel", "Raffa.Benchmark"],
        ["Raffa.Quotes"]               = ["Raffa.SharedKernel", "Raffa.Benchmark"],
        ["Raffa.Chat"]                 = ["Raffa.SharedKernel", "Raffa.AiGateway"],
        ["Raffa.Audit"]                = ["Raffa.SharedKernel"],
        // ADR-024 module-map delta (task E13/F01/US01/T01, v2-scaffold).
        ["Raffa.Market"]               = ["Raffa.SharedKernel", "Raffa.AiGateway", "Raffa.Benchmark"],
        ["Raffa.Insights"]             = ["Raffa.SharedKernel", "Raffa.Benchmark"],
    };

    /// <summary>Provider SDK prefixes that domain modules must never reference directly.</summary>
    internal static readonly string[] ForbiddenSdkPrefixes =
    [
        "Azure.",
        "Microsoft.Azure.",
        "Microsoft.AI.",
        "OpenAI",
        "Google.Cloud.",
        "Amazon.",
    ];

    [Theory]
    [MemberData(nameof(GetDomainModules))]
    public void Domain_module_project_references_follow_allowed_direction(string moduleName)
    {
        var csprojPath = Path.Combine(SolutionRoot, "src", moduleName, $"{moduleName}.csproj");
        Assert.True(File.Exists(csprojPath), $"Project file not found: {csprojPath}");

        var projectRefs = GetProjectReferenceNames(csprojPath);
        var allowed = AllowedReferences[moduleName];

        var violations = projectRefs
            .Where(r => AllRaffaProjects.Contains(r) && !allowed.Contains(r))
            .ToList();

        Assert.True(
            violations.Count == 0,
            $"[ADR-002] {moduleName} has forbidden project references: " +
            $"[{string.Join(", ", violations)}]. " +
            $"Allowed Raffa references: [{string.Join(", ", allowed)}]");
    }

    [Theory]
    [MemberData(nameof(GetDomainModules))]
    public void Domain_module_must_not_reference_provider_sdks(string moduleName)
    {
        var csprojPath = Path.Combine(SolutionRoot, "src", moduleName, $"{moduleName}.csproj");
        Assert.True(File.Exists(csprojPath), $"Project file not found: {csprojPath}");

        var packageRefs = GetPackageReferenceNames(csprojPath);

        var forbidden = packageRefs
            .Where(pkg => ForbiddenSdkPrefixes.Any(prefix =>
                pkg.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))
            .ToList();

        Assert.True(
            forbidden.Count == 0,
            $"[ADR-002] {moduleName} directly references provider SDKs: " +
            $"[{string.Join(", ", forbidden)}]. " +
            "Domain modules must use AI Gateway / Benchmark Service interfaces instead.");
    }

    /// <summary>
    /// Task E04/F01/US01/T02 ("adapter registry + provider SDK isolation"): the registry and
    /// <c>IBenchmarkService</c> itself must never reference a provider SDK directly — only a
    /// concrete <c>IBenchmarkProviderAdapter</c> implementation may (module-map.md: "Benchmark
    /// Service (impl) --references--> provider adapters (isolated)"). This task adds no concrete
    /// adapter, so <c>Raffa.Benchmark.csproj</c> itself must carry zero forbidden SDK package
    /// references today.
    /// </summary>
    [Fact]
    public void Benchmark_module_must_not_reference_provider_sdks()
    {
        var csprojPath = Path.Combine(SolutionRoot, "src", "Raffa.Benchmark", "Raffa.Benchmark.csproj");
        Assert.True(File.Exists(csprojPath), $"Project file not found: {csprojPath}");

        var packageRefs = GetPackageReferenceNames(csprojPath);

        var forbidden = packageRefs
            .Where(pkg => ForbiddenSdkPrefixes.Any(prefix =>
                pkg.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))
            .ToList();

        Assert.True(
            forbidden.Count == 0,
            $"[ADR-002/E04-F01-US01-T02] Raffa.Benchmark directly references provider SDKs: " +
            $"[{string.Join(", ", forbidden)}]. Provider SDK references belong on a concrete " +
            "IBenchmarkProviderAdapter implementation, isolated from the registry.");
    }

    [Fact]
    public void All_domain_modules_exist_in_solution()
    {
        foreach (var moduleName in DomainModules)
        {
            var csprojPath = Path.Combine(SolutionRoot, "src", moduleName, $"{moduleName}.csproj");
            Assert.True(File.Exists(csprojPath),
                $"[ADR-002] Domain module project missing: {moduleName}");
        }

        // Also verify SharedKernel, AiGateway, Benchmark, hosts
        foreach (var name in new[]
        {
            "Raffa.SharedKernel", "Raffa.AiGateway", "Raffa.Benchmark",
            "Raffa.Api", "Raffa.Worker"
        })
        {
            var csprojPath = Path.Combine(SolutionRoot, "src", name, $"{name}.csproj");
            Assert.True(File.Exists(csprojPath),
                $"[ADR-002] Required project missing: {name}");
        }
    }

    [Theory]
    [InlineData("Raffa.Api")]
    [InlineData("Raffa.Worker")]
    public void Host_must_not_contain_domain_types(string hostName)
    {
        var assembly = System.Reflection.Assembly.Load(
            new System.Reflection.AssemblyName(hostName));

        // Hosts are thin composition roots. They should contain only:
        //   - Program (top-level statements / entry point)
        //   - DI/startup wiring helpers
        //   - Compiler-generated types
        // NOT domain entities, aggregates, value objects, or services.
        var publicDomainTypes = assembly.GetTypes()
            .Where(t => t.IsPublic && t.Namespace is not null)
            .Where(t =>
                !t.Name.StartsWith('<') &&                     // compiler-generated
                !t.Name.Contains("Program", StringComparison.Ordinal) &&
                !t.Name.Contains("Startup", StringComparison.Ordinal) &&
                !t.Name.Contains("Extensions", StringComparison.Ordinal))
            .ToList();

        Assert.True(
            publicDomainTypes.Count == 0,
            $"[ADR-002] Host {hostName} exposes public types that look like business logic: " +
            $"[{string.Join(", ", publicDomainTypes.Select(t => t.FullName))}]. " +
            "Hosts must be thin composition roots — move domain types to their module project.");
    }

    // ------- helpers -------

    public static TheoryData<string> GetDomainModules()
    {
        var data = new TheoryData<string>();
        foreach (var name in DomainModules)
            data.Add(name);
        return data;
    }

    internal static List<string> GetProjectReferenceNames(string csprojPath)
    {
        var doc = XDocument.Load(csprojPath);
        return doc.Descendants("ProjectReference")
            .Select(e => e.Attribute("Include")?.Value)
            .Where(v => v is not null)
            // csproj files are authored with Windows separators; Linux CI
            // Path.GetFileName* does not treat '\' as a directory separator.
            .Select(v => Path.GetFileNameWithoutExtension(v!.Replace('\\', '/')))
            .ToList();
    }

    internal static List<string> GetPackageReferenceNames(string csprojPath)
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
        var assemblyLocation = typeof(DependencyDirectionTests).Assembly.Location;
        var dir = new DirectoryInfo(Path.GetDirectoryName(assemblyLocation)!);

        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Raffa.slnx")))
            dir = dir.Parent;

        return dir?.FullName
            ?? throw new InvalidOperationException(
                "Could not find solution root (looking for Raffa.slnx). " +
                $"Started from: {assemblyLocation}");
    }
}
