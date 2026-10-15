using System.Diagnostics.Metrics;
using Raffa.Documents.Contracts.Application.Extraction;

namespace Raffa.AiFlows.DocumentExtraction.Verification;

/// <summary>Verdict counts of one stage's facts.</summary>
public readonly record struct SpanCheckCounts(int Found, int FoundOnOtherPage, int NotFound, int NoSpan)
{
    public int Total => Found + FoundOnOtherPage + NotFound + NoSpan;
}

/// <summary>
/// F5-T07: runs a <see cref="SpanVerifier"/> over the typed facts of one stage. In Shadow it only counts and
/// returns the very same <see cref="StageFacts"/> instance; in Gate it returns copies whose
/// <c>SpanVerified</c> is set (false for not-found and for found-on-another-page, true for found, null when
/// there is no span). The model's page and values are never touched.
/// </summary>
internal static class StageSpanCheck
{
    public const string MeterName = "Raffa.AiFlows.Extraction";

    private static readonly Meter Meter = new(MeterName, "1.0.0");

    private static readonly Counter<long> Verdicts = Meter.CreateCounter<long>(
        "raffa.extraction.span_verifications",
        description: "Evidence spans checked against the document text, by stage, verdict and mode.");

    public static (StageFacts Facts, SpanCheckCounts Counts) Run(StageFacts facts, SpanVerifier verifier, SpanVerificationMode mode)
    {
        var gate = mode == SpanVerificationMode.Gate;
        int found = 0, otherPage = 0, notFound = 0, noSpan = 0;

        bool? Judge(string? span, int? page)
        {
            switch (verifier.Verify(span, page))
            {
                case SpanVerdict.Found:
                    found++;
                    return true;
                case SpanVerdict.FoundOnOtherPage:
                    otherPage++;
                    return false;
                case SpanVerdict.NotFound:
                    notFound++;
                    return false;
                default:
                    noSpan++;
                    return null;
            }
        }

        StageFacts result = facts switch
        {
            // Absent facts (no value) have no evidence row and nothing to verify.
            StageFacts.Scalars s => gate
                ? new StageFacts.Scalars(s.Stage, s.Facts.Select(f => string.IsNullOrWhiteSpace(f.Value)
                    ? f
                    : f with { SpanVerified = Judge(f.SourceSpan, f.SourcePage) }).ToList())
                : Observe(s, s.Facts.Where(f => !string.IsNullOrWhiteSpace(f.Value)), f => Judge(f.SourceSpan, f.SourcePage)),
            StageFacts.LineItems l => gate
                ? new StageFacts.LineItems(l.Items.Select(i => i with { SpanVerified = Judge(i.SourceSpan, i.SourcePage) }).ToList())
                : Observe(l, l.Items, i => Judge(i.SourceSpan, i.SourcePage)),
            StageFacts.Clauses c => gate
                ? new StageFacts.Clauses(c.Items.Select(i => i with { SpanVerified = Judge(i.SourceSpan, i.SourcePage) }).ToList())
                : Observe(c, c.Items, i => Judge(i.SourceSpan, i.SourcePage)),
            StageFacts.Obligations o => gate
                ? new StageFacts.Obligations(o.Items.Select(i => i with { SpanVerified = Judge(i.SourceSpan, i.SourcePage) }).ToList())
                : Observe(o, o.Items, i => Judge(i.SourceSpan, i.SourcePage)),
            StageFacts.Risks r => gate
                ? new StageFacts.Risks(r.Items.Select(i => i with { SpanVerified = Judge(i.SourceSpan, i.SourcePage) }).ToList())
                : Observe(r, r.Items, i => Judge(i.SourceSpan, i.SourcePage)),
            _ => facts,
        };

        var counts = new SpanCheckCounts(found, otherPage, notFound, noSpan);
        Record(facts.Stage.ToString(), mode, counts);
        return (result, counts);
    }

    private static StageFacts Observe<T>(StageFacts original, IEnumerable<T> items, Func<T, bool?> judge)
    {
        foreach (var item in items)
        {
            judge(item);
        }

        return original;
    }

    private static void Record(string stage, SpanVerificationMode mode, SpanCheckCounts counts)
    {
        void Add(int count, string verdict)
        {
            if (count > 0)
            {
                Verdicts.Add(
                    count,
                    new KeyValuePair<string, object?>("stage", stage),
                    new KeyValuePair<string, object?>("verdict", verdict),
                    new KeyValuePair<string, object?>("mode", mode.ToString()));
            }
        }

        Add(counts.Found, "found");
        Add(counts.FoundOnOtherPage, "found_on_other_page");
        Add(counts.NotFound, "not_found");
        Add(counts.NoSpan, "no_span");
    }
}
