using System.Globalization;
using System.Runtime.CompilerServices;

namespace Contigo.AiEval;

/// <summary>
/// Run-time switches for the golden set (task E13/F06/US01/T02, ask-golden-set; R-EVD-03 "run in
/// CI against the fixture gateway and on demand against Foundry"). Everything here has a
/// deterministic default so an unconfigured <c>dotnet test</c> — the CI path — always runs the
/// fixture-gateway evaluation with a pinned clock and no network.
///
/// <para>
/// <b>The manual Foundry run (OQ-askv2-009: "Foundry run is manual")</b>: set
/// <c>AiEval__UseFoundry=true</c> and the harness leaves the host's own <c>IAiGateway</c>
/// registration in place instead of substituting the fixture — <c>Contigo.AiGateway
/// .ServiceCollectionExtensions.AddAiGatewayModule</c> then resolves <c>FoundryAiGateway</c> when
/// <c>AiGateway:Endpoint</c> is configured, and the fixture otherwise (R-AI-01). A live model is
/// not deterministic, so that mode reports every case's verdict but asserts only the invariants a
/// real model must still honour (kind, guard interventions, no engineer chrome); the verbatim
/// -number assertions become advisory. See <c>backend/README.md</c> § "Ask V2 AI evaluation set".
/// </para>
/// </summary>
internal static class AiEvalOptions
{
    /// <summary>Environment variable name — double underscore matches the
    /// <c>ConnectionStrings__X</c> convention every deployed Contigo setting already uses.</summary>
    public const string UseFoundryVariable = "AiEval__UseFoundry";

    /// <summary>
    /// The instant every fixture host's <c>IClock</c> is pinned to. Every relative-date expectation
    /// in <c>golden/*.json</c> (a renewal window, a notice deadline, "days left") is computed from
    /// this exact instant, so the set never races a wall clock or a midnight rollover — the same
    /// determinism convention <c>Contigo.Chat.Application.DeterministicQueryHandler</c>'s own
    /// <c>IClock</c> dependency exists for (Appendix C rule 6).
    /// </summary>
    public static readonly DateTimeOffset EvaluationInstant =
        new(2026, 9, 9, 12, 0, 0, TimeSpan.Zero);

    /// <summary><see cref="EvaluationInstant"/> as the calendar day the engine's own
    /// <c>DateOnly.FromDateTime(clock.UtcNow.UtcDateTime)</c> derives.</summary>
    public static DateOnly Today => DateOnly.FromDateTime(EvaluationInstant.UtcDateTime);

    /// <summary><see langword="true"/> only when <see cref="UseFoundryVariable"/> is set to a
    /// value <see cref="bool.TryParse(string, out bool)"/> reads as true — anything else (unset,
    /// blank, "0", garbage) keeps the deterministic fixture run, so a typo can never silently turn
    /// CI into a billed, non-deterministic model run.</summary>
    public static bool UseFoundry =>
        bool.TryParse(Environment.GetEnvironmentVariable(UseFoundryVariable), out var value) && value;

    /// <summary>Where <c>GoldenSetReport</c> writes the per-case markdown artefact the task asks
    /// for (<c>backend/tests/Contigo.AiEval/reports/last-run.md</c>, git-ignored).</summary>
    public static string ReportPath => Path.Combine(ProjectDirectory, "reports", "last-run.md");

    /// <summary>The checked-in <c>golden/</c> directory in the source tree.</summary>
    public static string GoldenDirectory => Path.Combine(ProjectDirectory, "golden");

    /// <summary>Fallback <c>golden/</c> directory next to the compiled test assembly — used when
    /// the source tree is not present (a packaged run), populated by the csproj's own
    /// <c>Content Include="golden\*.json"</c> copy step.</summary>
    public static string GoldenOutputDirectory => Path.Combine(AppContext.BaseDirectory, "golden");

    /// <summary>
    /// This project's own directory, resolved from the compiler-supplied path of this source file
    /// rather than from <see cref="AppContext.BaseDirectory"/> + a hard-coded <c>../../..</c> hop.
    /// The relative hop breaks the moment the build's output layout changes (a different
    /// TargetFramework folder, a custom <c>OutputPath</c>); <see cref="CallerFilePathAttribute"/>
    /// is fixed at compile time and cannot drift.
    /// </summary>
    private static string ProjectDirectory { get; } = ResolveProjectDirectory();

    private static string ResolveProjectDirectory([CallerFilePath] string? thisFile = null) =>
        Path.GetDirectoryName(thisFile)
            ?? throw new InvalidOperationException(
                "Could not resolve the Contigo.AiEval project directory from the compiler-supplied " +
                "source path — the golden set cannot locate golden/ or reports/.");

    /// <summary>Human-readable one-liner naming which mode this run used, printed at the top of
    /// the report so a reader never has to guess whether the numbers came from the fixture or from
    /// a live model.</summary>
    public static string DescribeMode() => UseFoundry
        ? "Foundry (manual run — " + UseFoundryVariable + "=true; verbatim-number checks are advisory)"
        : "fixture gateway (deterministic; no network, no Foundry endpoint), clock pinned to " +
          EvaluationInstant.ToString("u", CultureInfo.InvariantCulture);
}
