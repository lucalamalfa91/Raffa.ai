using System.Diagnostics.Metrics;
using System.Globalization;
using Raffa.AiFlows.DocumentExtraction.Schemas;
using Raffa.AiFlows.DocumentExtraction.Stages;
using Raffa.AiGateway.Contracts;
using Raffa.Documents.Contracts.Application.Extraction;
using Raffa.Documents.Contracts.Domain;

namespace Raffa.AiFlows.DocumentExtraction.Orchestration;

/// <summary>How the second reading of a high-impact field compares with the one the run applied.</summary>
internal enum SecondOpinionOutcome
{
    Agree,
    Disagree,
    PrimaryOnly,
    SecondOnly,
    BothAbsent,
}

/// <summary>
/// A-07: the stage a second opinion is taken from. Same stage (so the same prompt, by name) but a schema
/// narrowed to the high-impact fields, so the pass is a focused re-read rather than a repeat of the whole
/// stage. Never applied to the store: the orchestrator only compares and counts.
/// </summary>
internal sealed class SecondOpinionStage : IExtractionStage
{
    private readonly IExtractionStage _inner;

    public SecondOpinionStage(IExtractionStage inner, IReadOnlyList<string> fields)
    {
        _inner = inner;
        Fields = fields;
        JsonSchema = StagedExtractionJsonSchemas.Facts(fields);
    }

    public IReadOnlyList<string> Fields { get; }

    public ExtractionStage Stage => _inner.Stage;

    public string StageName => _inner.StageName;

    public IReadOnlyList<ExtractionStage> DependsOn => [];

    public string JsonSchema { get; }

    public AiExtractionRequest BuildRequest(ExtractionStageInput input) => new(StageName, input.DocumentText, JsonSchema);

    public StageFacts Parse(string payloadJson) => _inner.Parse(payloadJson);
}

/// <summary>
/// A-07: second opinion on the fields whose error costs most (end date, notice period, annual fee), in
/// Shadow only. The comparison is counted (<c>raffa.extraction.second_opinion</c> by field and outcome) and
/// logged by the orchestrator; no value, status, evidence or row is ever changed or written.
/// </summary>
internal static class SecondOpinionShadow
{
    public static readonly IReadOnlyList<string> HighImpactFields = ["endDate", "noticePeriodDays", "annualSpend"];

    private static readonly Meter Meter = new("Raffa.AiFlows.Extraction", "1.0.0");

    private static readonly Counter<long> Opinions = Meter.CreateCounter<long>(
        "raffa.extraction.second_opinion",
        description: "Second-opinion (shadow) readings of high-impact fields compared with the applied reading, by field and outcome.");

    /// <summary>The high-impact fields the given stage owns (empty for most stages).</summary>
    public static IReadOnlyList<string> FieldsOf(ExtractionStage stage) =>
        stage is ExtractionStage.Metadata or ExtractionStage.CommercialTerms or ExtractionStage.DatesAndRenewalTerms
            ? ExtractionStageFields.For(stage).Where(f => HighImpactFields.Contains(f, StringComparer.Ordinal)).ToList()
            : [];

    public static IReadOnlyList<(string Field, SecondOpinionOutcome Outcome)> Compare(
        StageFacts.Scalars primary, StageFacts.Scalars second, IReadOnlyList<string> fields)
    {
        var results = new List<(string, SecondOpinionOutcome)>(fields.Count);
        foreach (var field in fields)
        {
            var a = ValueOf(primary, field);
            var b = ValueOf(second, field);
            var outcome = (a, b) switch
            {
                (null, null) => SecondOpinionOutcome.BothAbsent,
                (not null, null) => SecondOpinionOutcome.PrimaryOnly,
                (null, not null) => SecondOpinionOutcome.SecondOnly,
                _ => Equivalent(field, a!, b!) ? SecondOpinionOutcome.Agree : SecondOpinionOutcome.Disagree,
            };
            results.Add((field, outcome));
        }

        return results;
    }

    public static void Record(IEnumerable<(string Field, SecondOpinionOutcome Outcome)> results)
    {
        foreach (var (field, outcome) in results)
        {
            Opinions.Add(1, new KeyValuePair<string, object?>("field", field), new KeyValuePair<string, object?>("outcome", outcome.ToString()));
        }
    }

    private static string? ValueOf(StageFacts.Scalars facts, string field) =>
        facts.Facts
            .Where(f => string.Equals(f.Field, field, StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(f.Value))
            .Select(f => f.Value!.Trim())
            .FirstOrDefault();

    private static bool Equivalent(string field, string a, string b)
    {
        switch (field)
        {
            case "endDate":
                if (DateOnly.TryParse(a, CultureInfo.InvariantCulture, DateTimeStyles.None, out var da)
                    && DateOnly.TryParse(b, CultureInfo.InvariantCulture, DateTimeStyles.None, out var db))
                {
                    return da == db;
                }

                break;
            case "noticePeriodDays":
            case "annualSpend":
                if (decimal.TryParse(a, NumberStyles.Number, CultureInfo.InvariantCulture, out var na)
                    && decimal.TryParse(b, NumberStyles.Number, CultureInfo.InvariantCulture, out var nb))
                {
                    return na == nb;
                }

                break;
        }

        return string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
    }
}
