using Raffa.Documents.Contracts.Application;
using Raffa.Documents.Contracts.Application.Extraction;
using Raffa.Documents.Contracts.Domain;

namespace Raffa.AiFlows.DocumentExtraction.Stages;

/// <summary>
/// F5-T05: the short, deterministic block a dependent stage's request text starts with when context
/// dependencies are on: <c>[[CONTEXT]]\ncurrency: EUR\nstartDate: 2026-01-01\n[[/CONTEXT]]\n</c>. Built from
/// auto-accept-grade scalar facts only (confidence at or above <see cref="ExtractionConfidencePolicy.AutoAcceptThreshold"/>),
/// one line per field in the dependency's schema order, dependencies in <c>DependsOn</c> order, the
/// lifecycle <c>status</c> never included. No usable fact = no block (the request is then the plain text).
/// </summary>
internal static class ExtractionContextBlock
{
    public const string Open = "[[CONTEXT]]";
    public const string Close = "[[/CONTEXT]]";

    public static string Prefix(
        IReadOnlyDictionary<ExtractionStage, StageFacts> context, IReadOnlyList<ExtractionStage> dependsOn)
    {
        if (context.Count == 0 || dependsOn.Count == 0)
        {
            return string.Empty;
        }

        var lines = new List<string>();
        foreach (var dependency in dependsOn)
        {
            if (!context.TryGetValue(dependency, out var facts) || facts is not StageFacts.Scalars scalars)
            {
                continue;
            }

            foreach (var field in ExtractionStageFields.For(dependency))
            {
                if (ExtractionConfidencePolicy.IsStatusField(field))
                {
                    continue;
                }

                var fact = scalars.Facts.FirstOrDefault(f =>
                    string.Equals(f.Field, field, StringComparison.OrdinalIgnoreCase)
                    && !string.IsNullOrWhiteSpace(f.Value)
                    && f.Confidence is { } confidence
                    && confidence >= ExtractionConfidencePolicy.AutoAcceptThreshold);
                if (fact is not null)
                {
                    lines.Add($"{field}: {SingleLine(fact.Value!)}");
                }
            }
        }

        return lines.Count == 0 ? string.Empty : Open + "\n" + string.Join("\n", lines) + "\n" + Close + "\n";
    }

    private static string SingleLine(string value) =>
        string.Join(' ', value.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
}
