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
        // Task E20/F02/US02/T01 (w17, NW-73): the operator console — host, not a domain module.
        // Listed here so the violation check covers it; absent from DomainModules and
        // AllowedReferences (a domain module referencing this host is a detectable violation).
        "Raffa.Tools",
        // ADR-002 amendment (AI flows layer): Raffa.AiFlows sits above the domain modules and is
        // referenced only by the hosts. Listed here so the violation check above sees a domain
        // module referencing it (without this entry the filter would drop the reference and the
        // test would stay blind); deliberately absent from DomainModules and AllowedReferences.
        "Raffa.AiFlows",
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

    /// <summary>Provider SDK prefixes that domain modules must never reference directly. Task
    /// E17/F01/US01/T01 (ADR-002 w15 footer clause 4, ADR-025 §J.1d): <c>Microsoft.Graph</c> joins
    /// the list -- nothing here matched it before, so a domain module could have taken a direct
    /// directory-write dependency with no test objecting. The adapter lives in the host
    /// (<c>Raffa.Api/Infrastructure/GraphGuestProvisioner</c>), which this domain-module scan never
    /// reaches; <c>Raffa.AiGateway.Tests.SdkAllowListTests</c> is the guard that does.</summary>
    internal static readonly string[] ForbiddenSdkPrefixes =
    [
        "Azure.",
        "Microsoft.Azure.",
        "Microsoft.AI.",
        "Microsoft.Graph",
        "OpenAI",
        "Google.Cloud.",
        "Amazon.",
    ];

    /// <summary>The project name of the AI flows layer (ADR-002 amendment).</summary>
    internal const string AiFlows = "Raffa.AiFlows";

    /// <summary>
    /// The only Raffa projects <c>Raffa.AiFlows</c> may reference: the kernel, the gateway and the
    /// domain modules it orchestrates. Never a host (Api, Worker) nor Tools, Messaging or Storage.
    /// </summary>
    internal static readonly HashSet<string> AiFlowsAllowedReferences =
    [
        "Raffa.SharedKernel",
        "Raffa.AiGateway",
        "Raffa.Chat",
        "Raffa.Documents.Contracts",
        "Raffa.Quotes",
        "Raffa.Market",
        "Raffa.Insights",
        "Raffa.Benchmark",
        "Raffa.Renewals",
        "Raffa.Savings",
        "Raffa.Suppliers.Products",
        "Raffa.Identity.Workspace",
    ];

    /// <summary>The only projects that may reference <c>Raffa.AiFlows</c>: the two composition roots.</summary>
    internal static readonly HashSet<string> AiFlowsConsumers = ["Raffa.Api", "Raffa.Worker"];

    /// <summary>
    /// Package prefixes <c>Raffa.AiFlows</c> must not reference directly: every provider SDK a
    /// domain module is barred from, plus the persistence stack (EF Core, Npgsql, Pgvector). Data
    /// and persistence stay in the domain modules; AiFlows reaches a DbContext only transitively.
    /// </summary>
    internal static readonly string[] ForbiddenAiFlowsPackagePrefixes =
    [
        .. ForbiddenSdkPrefixes,
        "Microsoft.EntityFrameworkCore",
        "EFCore.",
        "Npgsql",
        "Pgvector",
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

    /// <summary>
    /// ADR-002 amendment: <c>Raffa.AiFlows</c> may reference only the kernel, the gateway and the
    /// domain modules. In particular never a host (Api, Worker), Tools, Messaging or Storage.
    /// </summary>
    [Fact]
    public void AiFlows_references_only_modules_and_kernel()
    {
        var csprojPath = Path.Combine(SolutionRoot, "src", AiFlows, $"{AiFlows}.csproj");
        Assert.True(File.Exists(csprojPath), $"Project file not found: {csprojPath}");

        var violations = FindAiFlowsProjectReferenceViolations(csprojPath);

        Assert.True(
            violations.Count == 0,
            $"[ADR-002] {AiFlows} has forbidden project references: [{string.Join(", ", violations)}]. " +
            $"Allowed: [{string.Join(", ", AiFlowsAllowedReferences)}].");
    }

    /// <summary>
    /// ADR-002 amendment: nothing below the hosts may depend on the AI flows layer, otherwise a
    /// module that <c>Raffa.AiFlows</c> orchestrates would form a cycle with it. The scan covers
    /// every <c>src/*/*.csproj</c>, so it also catches projects outside <see cref="DomainModules"/>
    /// (Messaging, Storage, Tools, AiGateway, Benchmark, SharedKernel).
    /// </summary>
    [Fact]
    public void Only_hosts_reference_AiFlows()
    {
        var offenders = FindAiFlowsConsumerViolations(Path.Combine(SolutionRoot, "src"));

        Assert.True(
            offenders.Count == 0,
            $"[ADR-002] Only [{string.Join(", ", AiFlowsConsumers)}] may reference {AiFlows}, but " +
            $"[{string.Join(", ", offenders)}] do. Move the dependency into {AiFlows} or behind a port " +
            "defined in the module.");
    }

    [Fact]
    public void AiFlows_must_not_reference_provider_sdks_or_persistence_packages()
    {
        var csprojPath = Path.Combine(SolutionRoot, "src", AiFlows, $"{AiFlows}.csproj");
        Assert.True(File.Exists(csprojPath), $"Project file not found: {csprojPath}");

        var forbidden = FindForbiddenAiFlowsPackages(csprojPath);

        Assert.True(
            forbidden.Count == 0,
            $"[ADR-002] {AiFlows} directly references provider SDK or persistence packages: " +
            $"[{string.Join(", ", forbidden)}]. Provider SDKs belong behind Raffa.AiGateway; " +
            "data access belongs in the domain modules.");
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
            "Raffa.Api", "Raffa.Worker", AiFlows
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

    /// <summary>
    /// Raffa project references of <c>Raffa.AiFlows</c> outside <see cref="AiFlowsAllowedReferences"/>.
    /// Filters on the <c>Raffa.</c> prefix rather than <see cref="AllRaffaProjects"/>, so projects
    /// that are not listed there (Messaging, Storage) are caught as well.
    /// </summary>
    internal static List<string> FindAiFlowsProjectReferenceViolations(string csprojPath) =>
        GetProjectReferenceNames(csprojPath)
            .Where(r => r.StartsWith("Raffa.", StringComparison.Ordinal) && !AiFlowsAllowedReferences.Contains(r))
            .ToList();

    /// <summary>Packages of the csproj that <c>Raffa.AiFlows</c> must not reference directly.</summary>
    internal static List<string> FindForbiddenAiFlowsPackages(string csprojPath) =>
        GetPackageReferenceNames(csprojPath)
            .Where(pkg => ForbiddenAiFlowsPackagePrefixes.Any(prefix =>
                pkg.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))
            .ToList();

    /// <summary>True if the csproj has a <c>ProjectReference</c> to <c>Raffa.AiFlows</c>.</summary>
    internal static bool ReferencesAiFlows(string csprojPath) =>
        GetProjectReferenceNames(csprojPath).Contains(AiFlows);

    /// <summary>
    /// Names of the projects under <paramref name="srcRoot"/> (<c>src/*/*.csproj</c>) that
    /// reference <c>Raffa.AiFlows</c> without being one of <see cref="AiFlowsConsumers"/>.
    /// </summary>
    internal static List<string> FindAiFlowsConsumerViolations(string srcRoot) =>
        Directory.EnumerateDirectories(srcRoot)
            .SelectMany(dir => Directory.EnumerateFiles(dir, "*.csproj", SearchOption.TopDirectoryOnly))
            .Where(ReferencesAiFlows)
            .Select(path => Path.GetFileNameWithoutExtension(path))
            .Where(name => !AiFlowsConsumers.Contains(name))
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToList();

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
