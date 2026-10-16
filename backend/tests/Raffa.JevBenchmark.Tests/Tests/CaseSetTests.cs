using Raffa.AiGateway.Contracts;
using Raffa.JevBenchmark.Tests.Harness;

namespace Raffa.JevBenchmark.Tests.Tests;

public sealed class CaseSetTests
{
    [Fact]
    public void The_classify_set_is_well_formed_labelled_and_marked_synthetic()
    {
        var set = CaseSets.LoadClassify();

        Assert.Equal(30, set.Cases.Count);
        Assert.Equal(set.Cases.Count, set.Cases.Select(c => c.Id).Distinct(StringComparer.Ordinal).Count());
        Assert.Contains("SYNTHETIC", set.Provenance, StringComparison.Ordinal);
        Assert.Contains("pending", set.Provenance, StringComparison.OrdinalIgnoreCase);
        Assert.All(set.Cases, c =>
        {
            Assert.True(Enum.TryParse<AiDocumentType>(c.ExpectedLabel, out _), $"{c.Id}: '{c.ExpectedLabel}' is not an AiDocumentType.");
            Assert.False(string.IsNullOrWhiteSpace(c.Text), c.Id);
            Assert.Contains(c.Language, new[] { "it", "en", "de" });
        });

        foreach (var type in Enum.GetNames<AiDocumentType>())
        {
            Assert.True(set.Cases.Count(c => c.ExpectedLabel == type) >= 3, $"{type} needs at least three cases.");
        }

        foreach (var language in new[] { "it", "en", "de" })
        {
            Assert.Contains(set.Cases, c => c.Language == language);
        }
    }

    [Fact]
    public void The_gap_set_reuses_the_AiEval_investigator_set_with_a_binary_label()
    {
        var set = CaseSets.LoadGaps();

        Assert.Equal(150, set.Cases.Count);
        Assert.Equal(set.Cases.Count, set.Cases.Select(c => c.Id).Distinct(StringComparer.Ordinal).Count());
        Assert.Equal([CaseSets.GapLabel, CaseSets.NoGapLabel], set.Labels);
        Assert.Contains(set.Cases, c => c.ExpectedLabel == CaseSets.GapLabel);
        Assert.Contains(set.Cases, c => c.ExpectedLabel == CaseSets.NoGapLabel);
        Assert.All(set.Cases, c => Assert.Contains(c.ExpectedLabel, set.Labels));
        Assert.Contains("Raffa.AiEval", set.Provenance, StringComparison.Ordinal);
        // Hard negatives are the point of the set: none of them may be labelled as a gap.
        Assert.All(set.Cases.Where(c => c.Category == "hard-negative"), c => Assert.Equal(CaseSets.NoGapLabel, c.ExpectedLabel));
    }

    [Fact]
    public void The_content_hash_is_stable_and_changes_with_the_data()
    {
        var first = CaseSets.LoadClassify();
        var second = CaseSets.LoadClassify();
        var edited = first with { Cases = first.Cases.Select((c, i) => i == 0 ? c with { Text = c.Text + " " } : c).ToList() };

        Assert.Equal(first.ContentHash, second.ContentHash);
        Assert.NotEqual(first.ContentHash, edited.ContentHash);
        Assert.Equal(64, first.ContentHash.Length);
    }

    [Fact]
    public void Take_limits_deterministically_and_zero_means_all()
    {
        var set = CaseSets.LoadClassify();

        Assert.Equal(5, set.Take(5).Cases.Count);
        Assert.Equal(set.Cases.Take(5).Select(c => c.Id), set.Take(5).Cases.Select(c => c.Id));
        Assert.Equal(30, set.Take(0).Cases.Count);
        Assert.Equal(30, set.Take(1000).Cases.Count);
    }

    [Fact]
    public void A_missing_set_fails_with_a_clear_message()
    {
        var ex = Assert.Throws<FileNotFoundException>(() => CaseSets.LoadClassify(Path.Combine(Path.GetTempPath(), "does-not-exist-" + Guid.NewGuid().ToString("N"))));

        Assert.Contains("classify-cases.v1.json", ex.Message, StringComparison.Ordinal);
    }
}
