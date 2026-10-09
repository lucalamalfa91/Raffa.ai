using System.Text.Json;

namespace Raffa.AiEval.Investigator;

/// <summary>One labelled phrase of the INV-04 set (<c>investigator/investigator-eval.v1.json</c>).</summary>
/// <param name="Id">"it-01" ... "de-30": the report row key and the xunit case name.</param>
/// <param name="Language">"it", "en", "fr", "es" or "de".</param>
/// <param name="Text">The user's phrase as typed.</param>
/// <param name="Category">gap-request, gap-implicit, ordinary, hard-negative or unanswerable.</param>
/// <param name="GapWorthy">A person would say the phrase asks for something Raffa cannot do today.</param>
/// <param name="Hard">A hard negative: a contract question carrying an operational verb or a
/// deliverable noun as a third party's duty, the user's own obligation or the text of a clause.</param>
/// <param name="Outcome">"answer" or "abstain": the simulated reply outcome trigger T2 reads.</param>
/// <param name="Expect">What the trigger must say for this phrase.</param>
internal sealed record InvestigatorEvalCase(
    string Id,
    string Language,
    string Text,
    string Category,
    bool GapWorthy,
    bool Hard,
    string Outcome,
    InvestigatorEvalExpectation Expect);

/// <summary>The expected trigger verdict. T1 is not asserted: it depends on the planner, which
/// reads Italian and English only; the report shows it as observed.</summary>
internal sealed record InvestigatorEvalExpectation(bool T3, bool T2);

/// <summary>The INV-04 evaluation set: ~150 phrases, 30 per language, loaded from the checked-in
/// JSON (source tree first, the copy next to the assembly as a fallback).</summary>
internal static class InvestigatorEvalSet
{
    public static readonly IReadOnlyList<string> Languages = ["it", "en", "fr", "es", "de"];

    public const int PhrasesPerLanguage = 30;

    /// <summary>The share of hard negatives the set must keep, overall and per language.</summary>
    public const double MinimumHardNegativeShare = 0.30;

    public const string FileName = "investigator-eval.v1.json";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    private static readonly Lazy<(string Version, IReadOnlyList<InvestigatorEvalCase> Cases)> Loaded = new(Load);

    public static string Version => Loaded.Value.Version;

    public static IReadOnlyList<InvestigatorEvalCase> Cases => Loaded.Value.Cases;

    private static (string, IReadOnlyList<InvestigatorEvalCase>) Load()
    {
        var path = new[] { AiEvalOptions.InvestigatorDirectory, AiEvalOptions.InvestigatorOutputDirectory }
            .Select(directory => Path.Combine(directory, FileName))
            .FirstOrDefault(File.Exists)
            ?? throw new FileNotFoundException(
                $"The investigator evaluation set '{FileName}' was found in neither '{AiEvalOptions.InvestigatorDirectory}' nor " +
                $"'{AiEvalOptions.InvestigatorOutputDirectory}'.");

        var document = JsonSerializer.Deserialize<EvalDocument>(File.ReadAllText(path), JsonOptions)
            ?? throw new InvalidOperationException($"'{path}' deserialized to null.");

        return (document.Version ?? "unversioned", document.Cases ?? []);
    }

    private sealed record EvalDocument(string? Version, List<InvestigatorEvalCase>? Cases);
}
