
using Raffa.AiFlows.CapabilityGaps.Investigation;

namespace Raffa.AiEval.Investigator;

/// <summary>
/// INV-04 / F4-D08: the offline regression of the investigator's deterministic trigger, run before
/// the release of <c>Triggered</c> mode and again whenever the lexicon changes. Asserts what is
/// deterministic and owned here — the set is well formed (150 phrases, 30 per language, at least 30
/// percent hard negatives), every phrase reaches the investigator, and T3 and T2 say exactly what
/// each phrase is labelled with — and writes the precision/recall report
/// (<c>reports/investigator-last-run.md</c>). The 90/60/70 percent targets are monitored in that
/// report with an alarm line; they are deliberately not assertions (plan 5.4).
/// </summary>
[Trait("Category", "AiEval")]
public sealed class InvestigatorEvalTests
{
    private static readonly Lazy<IReadOnlyList<InvestigatorObservation>> Observed =
        new(() => InvestigatorTriggerEvaluator.Evaluate(InvestigatorEvalSet.Cases));

    public static TheoryData<string> CaseIds
    {
        get
        {
            var data = new TheoryData<string>();
            foreach (var evalCase in InvestigatorEvalSet.Cases)
            {
                data.Add(evalCase.Id);
            }

            return data;
        }
    }

    [Fact]
    public void The_set_has_thirty_phrases_for_each_of_the_five_languages_and_unique_ids()
    {
        Assert.Equal(InvestigatorEvalSet.Languages.Count * InvestigatorEvalSet.PhrasesPerLanguage, InvestigatorEvalSet.Cases.Count);

        foreach (var language in InvestigatorEvalSet.Languages)
        {
            Assert.Equal(
                InvestigatorEvalSet.PhrasesPerLanguage,
                InvestigatorEvalSet.Cases.Count(c => c.Language == language));
        }

        Assert.Equal(InvestigatorEvalSet.Cases.Count, InvestigatorEvalSet.Cases.Select(c => c.Id).Distinct(StringComparer.Ordinal).Count());
        Assert.All(InvestigatorEvalSet.Cases, c => Assert.StartsWith(c.Language + "-", c.Id, StringComparison.Ordinal));
        Assert.Equal(
            InvestigatorEvalSet.Cases.Count,
            InvestigatorEvalSet.Cases.Select(c => c.Text.Trim().ToLowerInvariant()).Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void At_least_thirty_percent_of_the_phrases_are_hard_negatives_overall_and_in_every_language()
    {
        Assert.All(InvestigatorEvalSet.Cases.Where(c => c.Hard), c =>
        {
            Assert.False(c.GapWorthy, $"{c.Id}: a hard negative is never gap-worthy.");
            Assert.Equal("hard-negative", c.Category);
        });

        var overall = InvestigatorEvalSet.Cases.Count(c => c.Hard) / (double)InvestigatorEvalSet.Cases.Count;
        Assert.True(overall >= InvestigatorEvalSet.MinimumHardNegativeShare, $"Hard negatives are {overall:P0} of the set.");

        foreach (var language in InvestigatorEvalSet.Languages)
        {
            var inLanguage = InvestigatorEvalSet.Cases.Where(c => c.Language == language).ToList();
            var share = inLanguage.Count(c => c.Hard) / (double)inLanguage.Count;
            Assert.True(share >= InvestigatorEvalSet.MinimumHardNegativeShare, $"{language}: hard negatives are {share:P0} of the phrases.");
        }
    }

    [Fact]
    public void The_set_carries_real_gaps_and_every_category_in_every_language()
    {
        foreach (var language in InvestigatorEvalSet.Languages)
        {
            var inLanguage = InvestigatorEvalSet.Cases.Where(c => c.Language == language).ToList();
            Assert.Contains(inLanguage, c => c.GapWorthy && c.Category == "gap-request");
            Assert.Contains(inLanguage, c => c.GapWorthy && c.Category == "gap-implicit");
            Assert.Contains(inLanguage, c => c.Category == "ordinary");
            Assert.Contains(inLanguage, c => c.Category == "unanswerable" && c.Outcome == "abstain");
        }
    }

    [Theory]
    [MemberData(nameof(CaseIds))]
    public void T3_and_T2_say_what_the_label_says_and_an_Italian_or_English_phrase_reaches_the_investigator(string caseId)
    {
        var observation = Observed.Value.Single(o => o.Case.Id == caseId);

        // The gate reads Italian and English: there it must let the phrase through (otherwise the
        // label would describe a phrase the investigator never sees). For the other languages the
        // gate's own coverage is reported, not asserted.
        if (observation.Case.Language is "it" or "en")
        {
            Assert.True(
                observation.ReachesInvestigator,
                $"{caseId}: the gate labels this phrase {observation.Gate}; it would never reach the investigator ({observation.Case.Text}).");
        }

        Assert.True(
            observation.Verdict.T3 == observation.Case.Expect.T3,
            $"{caseId}: expected T3={observation.Case.Expect.T3}, observed {observation.Verdict.T3} ({observation.Verdict.Reason}) — {observation.Case.Text}");

        Assert.True(
            observation.Verdict.T2 == observation.Case.Expect.T2,
            $"{caseId}: expected T2={observation.Case.Expect.T2}, observed {observation.Verdict.T2} — {observation.Case.Text}");
    }

    [Fact]
    public void The_lexicon_is_versioned_and_covers_the_five_languages()
    {
        var lexicon = InvestigatorTriggerLexicon.Default;

        Assert.False(string.IsNullOrWhiteSpace(lexicon.Version));
        Assert.Equal(InvestigatorEvalSet.Languages, lexicon.Languages.Select(l => l.Language).ToList());
    }

    [Fact]
    public void The_run_writes_the_precision_and_recall_report_per_trigger_per_language_and_overall()
    {
        var report = InvestigatorTriggerEvaluator.RenderReport(Observed.Value);

        Directory.CreateDirectory(Path.GetDirectoryName(AiEvalOptions.InvestigatorReportPath)!);
        File.WriteAllText(AiEvalOptions.InvestigatorReportPath, report);

        Assert.Contains("## Overall", report, StringComparison.Ordinal);
        foreach (var language in InvestigatorEvalSet.Languages)
        {
            Assert.Contains($"## Language: {language}", report, StringComparison.Ordinal);
        }

        Assert.Contains("T3 operational request (lexicon)", report, StringComparison.Ordinal);
        Assert.Contains("Precision", report, StringComparison.Ordinal);
        Assert.Contains("Recall", report, StringComparison.Ordinal);
        Assert.Contains("## Targets", report, StringComparison.Ordinal);
    }

    [Fact]
    public void T3_alone_keeps_the_recall_and_the_precision_of_an_operational_request_trigger()
    {
        // Not the 90/60/70 targets of plan 5.4 (those are monitored after release, with an alarm):
        // a floor under the lexicon itself, so a careless edit cannot silently gut it. Every
        // gap-request phrase is detected (recall 1.0 on that category) and no hard negative is.
        var requests = Observed.Value.Where(o => o.Case.Category == "gap-request").ToList();
        Assert.NotEmpty(requests);
        Assert.All(requests, o => Assert.True(o.Verdict.T3, $"{o.Case.Id}: {o.Case.Text}"));

        var hardNegatives = Observed.Value.Where(o => o.Case.Hard).ToList();
        Assert.NotEmpty(hardNegatives);
        Assert.All(hardNegatives, o => Assert.False(o.Verdict.T3, $"{o.Case.Id}: {o.Case.Text}"));
    }
}
