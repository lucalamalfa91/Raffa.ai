using Raffa.Benchmark.Contracts;
using Raffa.Insights.Application;
using Raffa.Insights.Contracts;
using Raffa.SharedKernel;

namespace Raffa.Insights.Tests;

/// <summary>
/// Proves task E31/F02/US01/T01's <see cref="NegotiationPointRanker"/> — parent story
/// us-01-point-ranker AC-1 ("each point carries the full field set, and is emitted only if
/// grounded"), AC-2 ("order: above-band price -&gt; uncapped/high liability -&gt;
/// auto-renew+short notice -&gt; SLA/credits -&gt; term/volume -&gt; payment terms") and the
/// epic-31 "Out of scope: no ungrounded '7 lever' dump" rule. Mirrors
/// <c>PricedLineNegotiationCalculatorTests</c>/<c>StrategyPackBuilderTests</c>'s own shape/style.
/// </summary>
public sealed class NegotiationPointRankerTests
{
    private static readonly EntityId ContractId = EntityId.New();

    private static PricedLine Line(
        string description = "Sales Cloud Enterprise",
        decimal? quantity = null,
        decimal? unitPrice = null,
        int? termMonths = null,
        BenchmarkDistribution? benchmark = null,
        string currency = "USD") =>
        new(null, description, quantity, unitPrice, currency, termMonths, benchmark, SampleSize: null);

    private static NegotiationPointInputs Inputs(
        IReadOnlyList<PricedLine>? pricedLines = null,
        bool autoRenewal = false,
        DateOnly? renewalDate = null,
        DateOnly? cancellationDeadline = null,
        IReadOnlyList<NegotiationClauseSnapshot>? clauses = null,
        IReadOnlyList<NegotiationRiskSnapshot>? risks = null,
        string? paymentTerms = null) =>
        new(
            ContractId,
            pricedLines ?? [],
            autoRenewal,
            renewalDate,
            cancellationDeadline,
            clauses ?? [],
            risks ?? [],
            paymentTerms);

    // ----- AC-1 / grounded-only, never a generic dump -----

    [Fact]
    public void Rejects_a_null_inputs_argument()
    {
        Assert.Throws<ArgumentNullException>(() => NegotiationPointRanker.Rank(null!));
    }

    [Fact]
    public void No_grounded_facts_at_all_returns_an_empty_list_never_a_generic_dump()
    {
        var points = NegotiationPointRanker.Rank(Inputs());

        Assert.Empty(points);
    }

    [Fact]
    public void Above_band_price_outranks_ungrounded_payment_terms()
    {
        // The task's own Definition of Done line, verbatim: "above-band price outranks ungrounded
        // payment-terms and the generic dump is not an answer" -- payment terms has no recorded
        // value at all here, so it must never appear, while the above-band line must.
        var line = Line(unitPrice: 2400m, benchmark: new BenchmarkDistribution(1500m, 1800m, 2100m));

        var points = NegotiationPointRanker.Rank(Inputs(pricedLines: [line], paymentTerms: null));

        var point = Assert.Single(points);
        Assert.Equal(NegotiationPointTopic.AboveBandPrice, point.Topic);
        Assert.Equal(1, point.Rank);
        Assert.DoesNotContain(points, p => p.Topic == NegotiationPointTopic.PaymentTerms);
    }

    [Fact]
    public void Every_canonical_topic_grounds_in_the_councils_priority_order_when_all_are_grounded()
    {
        var pricedLines = new[]
        {
            Line("Above-band line", unitPrice: 2400m, benchmark: new BenchmarkDistribution(1500m, 1800m, 2100m)),
            Line("Committed line", quantity: 250m, termMonths: 36),
        };
        var risks = new[]
        {
            new NegotiationRiskSnapshot("Uncapped liability", "No cap on total liability.", CriticalityRiskSeverity.Critical),
            new NegotiationRiskSnapshot("Missing SLA", "No SLA or credits are defined.", CriticalityRiskSeverity.Medium),
        };

        var inputs = Inputs(
            pricedLines: pricedLines,
            autoRenewal: true,
            renewalDate: new DateOnly(2027, 1, 1),
            cancellationDeadline: new DateOnly(2026, 12, 15),
            risks: risks,
            paymentTerms: "Net 30");

        var points = NegotiationPointRanker.Rank(inputs);

        Assert.Equal(
            [
                NegotiationPointTopic.AboveBandPrice,
                NegotiationPointTopic.UncappedOrHighLiability,
                NegotiationPointTopic.AutoRenewShortNotice,
                NegotiationPointTopic.SlaCredits,
                NegotiationPointTopic.TermVolume,
                NegotiationPointTopic.PaymentTerms,
            ],
            points.Select(p => p.Topic));
        Assert.Equal([1, 2, 3, 4, 5, 6], points.Select(p => p.Rank));
    }

    [Fact]
    public void Each_grounded_point_carries_the_full_field_set()
    {
        var line = Line(unitPrice: 2400m, benchmark: new BenchmarkDistribution(1500m, 1800m, 2100m));

        var points = NegotiationPointRanker.Rank(Inputs(pricedLines: [line]));

        var point = Assert.Single(points);
        Assert.False(string.IsNullOrWhiteSpace(point.Current));
        Assert.False(string.IsNullOrWhiteSpace(point.Target));
        Assert.False(string.IsNullOrWhiteSpace(point.WhyItMatters));
        Assert.NotEmpty(point.CitationKeys);
    }

    [Fact]
    public void Same_inputs_produce_the_same_ranked_list_every_time()
    {
        // Compared field-by-field, not as whole records (same reasoning
        // StrategyPackBuilderTests's own determinism test already follows): a record's
        // auto-generated Equals compares an IReadOnlyList<string> property by reference, not by
        // content, so two independently-built-but-identical CitationKeys lists would otherwise
        // read as "different" even though nothing about the ranking is actually nondeterministic.
        var line = Line(unitPrice: 2400m, benchmark: new BenchmarkDistribution(1500m, 1800m, 2100m));
        var inputs = Inputs(pricedLines: [line], paymentTerms: "Net 30");

        var first = NegotiationPointRanker.Rank(inputs);
        var second = NegotiationPointRanker.Rank(inputs);

        Assert.Equal(first.Select(p => p.Topic), second.Select(p => p.Topic));
        Assert.Equal(first.Select(p => p.Rank), second.Select(p => p.Rank));
        Assert.Equal(first.Select(p => p.Current), second.Select(p => p.Current));
        Assert.Equal(first.Select(p => p.Target), second.Select(p => p.Target));
        Assert.Equal(first.Select(p => p.WhyItMatters), second.Select(p => p.WhyItMatters));
        Assert.Equal(first.Select(p => p.Strength), second.Select(p => p.Strength));
        Assert.Equal(first.SelectMany(p => p.CitationKeys), second.SelectMany(p => p.CitationKeys));
    }

    // ----- Topic 1: above-band price -----

    [Fact]
    public void Above_band_price_is_ungrounded_when_no_line_exceeds_p75()
    {
        var atBand = Line(unitPrice: 2100m, benchmark: new BenchmarkDistribution(1500m, 1800m, 2100m));
        var noBenchmark = Line(unitPrice: 5000m, benchmark: null);

        var points = NegotiationPointRanker.Rank(Inputs(pricedLines: [atBand, noBenchmark]));

        Assert.DoesNotContain(points, p => p.Topic == NegotiationPointTopic.AboveBandPrice);
    }

    [Fact]
    public void Above_band_price_picks_the_worst_line_and_cites_it()
    {
        var contractId = ContractId;
        var mild = Line("Mild overage", unitPrice: 2200m, benchmark: new BenchmarkDistribution(1500m, 1800m, 2100m));
        var worst = Line("Worst overage", unitPrice: 3000m, benchmark: new BenchmarkDistribution(1500m, 1800m, 2100m));

        var points = NegotiationPointRanker.Rank(Inputs(pricedLines: [mild, worst]));

        var point = Assert.Single(points, p => p.Topic == NegotiationPointTopic.AboveBandPrice);
        Assert.Contains("Worst overage", point.Current);
        Assert.Contains("2 priced lines", point.Current);
        Assert.Contains($"fact:{contractId}:priced-line[1].unitPrice", point.CitationKeys);
        Assert.Contains($"calc:priced-line[1].above-band", point.CitationKeys);
    }

    [Theory]
    [InlineData(2400, NegotiationPointStrength.Strong)] // (2400-2100)/2100 = 14.3% >= 10%
    [InlineData(2150, NegotiationPointStrength.Moderate)] // (2150-2100)/2100 = 2.4% < 10%
    public void Above_band_price_strength_reflects_the_overage_margin(decimal unitPrice, NegotiationPointStrength expected)
    {
        var line = Line(unitPrice: unitPrice, benchmark: new BenchmarkDistribution(1500m, 1800m, 2100m));

        var points = NegotiationPointRanker.Rank(Inputs(pricedLines: [line]));

        var point = Assert.Single(points, p => p.Topic == NegotiationPointTopic.AboveBandPrice);
        Assert.Equal(expected, point.Strength);
    }

    // ----- Topic 2: uncapped / high liability -----

    [Fact]
    public void Liability_is_ungrounded_with_no_matching_risk_or_clause()
    {
        var risks = new[] { new NegotiationRiskSnapshot("Price escalation", "Prices may rise annually.", CriticalityRiskSeverity.Low) };
        var clauses = new[] { new NegotiationClauseSnapshot("Confidentiality", "Each party shall keep the other's information confidential.") };

        var points = NegotiationPointRanker.Rank(Inputs(risks: risks, clauses: clauses));

        Assert.DoesNotContain(points, p => p.Topic == NegotiationPointTopic.UncappedOrHighLiability);
    }

    [Fact]
    public void Liability_grounds_from_a_risk_row_and_cites_it()
    {
        var risks = new[]
        {
            new NegotiationRiskSnapshot("Uncapped liability", "No cap on total liability was found.", CriticalityRiskSeverity.Critical),
        };

        var points = NegotiationPointRanker.Rank(Inputs(risks: risks));

        var point = Assert.Single(points, p => p.Topic == NegotiationPointTopic.UncappedOrHighLiability);
        Assert.Equal(NegotiationPointStrength.Strong, point.Strength);
        Assert.Equal([$"fact:{ContractId}:risk[0]"], point.CitationKeys);
    }

    [Fact]
    public void Liability_falls_back_to_a_clause_when_no_risk_matches()
    {
        var clauses = new[]
        {
            new NegotiationClauseSnapshot(
                "Limitation of liability",
                "Each party's liability shall not exceed the fees paid in the preceding 12 months."),
        };

        var points = NegotiationPointRanker.Rank(Inputs(clauses: clauses));

        var point = Assert.Single(points, p => p.Topic == NegotiationPointTopic.UncappedOrHighLiability);
        Assert.Equal(NegotiationPointStrength.Moderate, point.Strength);
        Assert.Contains("Limitation of liability", point.Current);
        Assert.Equal([$"fact:{ContractId}:clause[0]"], point.CitationKeys);
    }

    [Fact]
    public void Liability_prefers_a_risk_over_a_clause_when_both_match()
    {
        var risks = new[] { new NegotiationRiskSnapshot("Uncapped liability", "Unlimited exposure.", CriticalityRiskSeverity.High) };
        var clauses = new[] { new NegotiationClauseSnapshot("Limitation of liability", "Capped at fees paid.") };

        var points = NegotiationPointRanker.Rank(Inputs(risks: risks, clauses: clauses));

        var point = Assert.Single(points, p => p.Topic == NegotiationPointTopic.UncappedOrHighLiability);
        Assert.Equal($"fact:{ContractId}:risk[0]", Assert.Single(point.CitationKeys));
    }

    [Fact]
    public void Liability_from_a_low_severity_risk_without_uncapped_wording_is_moderate()
    {
        var risks = new[] { new NegotiationRiskSnapshot("Liability review", "Liability terms are non-standard.", CriticalityRiskSeverity.Low) };

        var points = NegotiationPointRanker.Rank(Inputs(risks: risks));

        var point = Assert.Single(points, p => p.Topic == NegotiationPointTopic.UncappedOrHighLiability);
        Assert.Equal(NegotiationPointStrength.Moderate, point.Strength);
    }

    // ----- Topic 3: auto-renew + short notice -----

    [Fact]
    public void Auto_renew_short_notice_is_ungrounded_without_auto_renewal()
    {
        var points = NegotiationPointRanker.Rank(Inputs(
            autoRenewal: false,
            renewalDate: new DateOnly(2027, 1, 1),
            cancellationDeadline: new DateOnly(2026, 12, 15)));

        Assert.DoesNotContain(points, p => p.Topic == NegotiationPointTopic.AutoRenewShortNotice);
    }

    [Theory]
    [MemberData(nameof(MissingDateCases))]
    public void Auto_renew_short_notice_is_ungrounded_when_a_date_is_missing(DateOnly? renewalDate, DateOnly? cancellationDeadline)
    {
        var points = NegotiationPointRanker.Rank(Inputs(
            autoRenewal: true, renewalDate: renewalDate, cancellationDeadline: cancellationDeadline));

        Assert.DoesNotContain(points, p => p.Topic == NegotiationPointTopic.AutoRenewShortNotice);
    }

    public static TheoryData<DateOnly?, DateOnly?> MissingDateCases() => new()
    {
        { null, new DateOnly(2026, 12, 15) },
        { new DateOnly(2027, 1, 1), null },
        { null, null },
    };

    [Fact]
    public void Auto_renew_short_notice_is_ungrounded_when_the_window_is_not_short()
    {
        var points = NegotiationPointRanker.Rank(Inputs(
            autoRenewal: true,
            renewalDate: new DateOnly(2027, 1, 1),
            cancellationDeadline: new DateOnly(2026, 9, 1))); // 122-day window

        Assert.DoesNotContain(points, p => p.Topic == NegotiationPointTopic.AutoRenewShortNotice);
    }

    [Fact]
    public void Auto_renew_short_notice_is_ungrounded_when_the_deadline_is_after_the_renewal_date()
    {
        // An anomalous/inconsistent pair -- never narrate a negative notice window as a fact.
        var points = NegotiationPointRanker.Rank(Inputs(
            autoRenewal: true,
            renewalDate: new DateOnly(2026, 1, 1),
            cancellationDeadline: new DateOnly(2026, 2, 1)));

        Assert.DoesNotContain(points, p => p.Topic == NegotiationPointTopic.AutoRenewShortNotice);
    }

    [Theory]
    [InlineData(17, NegotiationPointStrength.Strong)]
    [InlineData(45, NegotiationPointStrength.Moderate)]
    public void Auto_renew_short_notice_strength_reflects_the_notice_window(int noticeDays, NegotiationPointStrength expected)
    {
        var renewalDate = new DateOnly(2027, 1, 1);
        var cancellationDeadline = renewalDate.AddDays(-noticeDays);

        var points = NegotiationPointRanker.Rank(Inputs(
            autoRenewal: true, renewalDate: renewalDate, cancellationDeadline: cancellationDeadline));

        var point = Assert.Single(points, p => p.Topic == NegotiationPointTopic.AutoRenewShortNotice);
        Assert.Equal(expected, point.Strength);
        Assert.Contains(noticeDays.ToString(), point.Current);
    }

    // ----- Topic 4: SLA / credits -----

    [Fact]
    public void Sla_credits_is_ungrounded_with_no_matching_risk_or_clause()
    {
        var points = NegotiationPointRanker.Rank(Inputs(
            risks: [new NegotiationRiskSnapshot("Price escalation", "Prices may rise.", CriticalityRiskSeverity.Low)]));

        Assert.DoesNotContain(points, p => p.Topic == NegotiationPointTopic.SlaCredits);
    }

    [Fact]
    public void Sla_credits_grounds_from_a_risk_row()
    {
        var risks = new[] { new NegotiationRiskSnapshot("Missing SLA", "No SLA or credits are defined.", CriticalityRiskSeverity.High) };

        var points = NegotiationPointRanker.Rank(Inputs(risks: risks));

        var point = Assert.Single(points, p => p.Topic == NegotiationPointTopic.SlaCredits);
        Assert.Equal(NegotiationPointStrength.Strong, point.Strength);
        Assert.Equal([$"fact:{ContractId}:risk[0]"], point.CitationKeys);
    }

    [Fact]
    public void Sla_credits_falls_back_to_a_clause_when_no_risk_matches()
    {
        var clauses = new[]
        {
            new NegotiationClauseSnapshot("Service Level Agreement", "Supplier targets 99.9% uptime with no defined credits."),
        };

        var points = NegotiationPointRanker.Rank(Inputs(clauses: clauses));

        var point = Assert.Single(points, p => p.Topic == NegotiationPointTopic.SlaCredits);
        Assert.Equal(NegotiationPointStrength.Moderate, point.Strength);
        Assert.Equal([$"fact:{ContractId}:clause[0]"], point.CitationKeys);
    }

    [Fact]
    public void An_empty_risk_and_clause_list_never_fabricates_a_missing_sla_point()
    {
        // Appendix C rule 10: absence of evidence is never evidence of absence -- zero risks and
        // zero clauses must not be read as "confirmed no SLA", only as "no data either way".
        var points = NegotiationPointRanker.Rank(Inputs());

        Assert.DoesNotContain(points, p => p.Topic == NegotiationPointTopic.SlaCredits);
        Assert.DoesNotContain(points, p => p.Topic == NegotiationPointTopic.UncappedOrHighLiability);
    }

    // ----- Topic 5: term / volume -----

    [Fact]
    public void Term_volume_is_ungrounded_with_no_term_or_quantity_recorded()
    {
        var line = Line(quantity: null, termMonths: null);

        var points = NegotiationPointRanker.Rank(Inputs(pricedLines: [line]));

        Assert.DoesNotContain(points, p => p.Topic == NegotiationPointTopic.TermVolume);
    }

    [Fact]
    public void Term_volume_grounds_from_a_recorded_term_alone()
    {
        var line = Line(termMonths: 36);

        var points = NegotiationPointRanker.Rank(Inputs(pricedLines: [line]));

        var point = Assert.Single(points, p => p.Topic == NegotiationPointTopic.TermVolume);
        Assert.Contains("36-month term", point.Current);
        Assert.Equal([$"fact:{ContractId}:priced-line[0].termMonths"], point.CitationKeys);
    }

    [Fact]
    public void Term_volume_grounds_from_a_recorded_quantity_alone()
    {
        var line = Line(quantity: 250m);

        var points = NegotiationPointRanker.Rank(Inputs(pricedLines: [line]));

        var point = Assert.Single(points, p => p.Topic == NegotiationPointTopic.TermVolume);
        Assert.Contains("250", point.Current);
        Assert.Equal([$"fact:{ContractId}:priced-line[0].quantity"], point.CitationKeys);
    }

    [Fact]
    public void Term_volume_combines_term_and_quantity_when_both_are_recorded_across_lines()
    {
        var termLine = Line("Term line", termMonths: 12);
        var volumeLine = Line("Volume line", quantity: 500m);

        var points = NegotiationPointRanker.Rank(Inputs(pricedLines: [termLine, volumeLine]));

        var point = Assert.Single(points, p => p.Topic == NegotiationPointTopic.TermVolume);
        Assert.Contains("Term line", point.Current);
        Assert.Contains("Volume line", point.Current);
        Assert.Equal(2, point.CitationKeys.Count);
    }

    [Theory]
    [InlineData(24, NegotiationPointStrength.Strong)]
    [InlineData(12, NegotiationPointStrength.Moderate)]
    public void Term_volume_strength_reflects_the_committed_term_length(int termMonths, NegotiationPointStrength expected)
    {
        var line = Line(termMonths: termMonths);

        var points = NegotiationPointRanker.Rank(Inputs(pricedLines: [line]));

        var point = Assert.Single(points, p => p.Topic == NegotiationPointTopic.TermVolume);
        Assert.Equal(expected, point.Strength);
    }

    // ----- Topic 6: payment terms -----

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Payment_terms_is_ungrounded_when_blank(string? paymentTerms)
    {
        var points = NegotiationPointRanker.Rank(Inputs(paymentTerms: paymentTerms));

        Assert.DoesNotContain(points, p => p.Topic == NegotiationPointTopic.PaymentTerms);
    }

    [Fact]
    public void Payment_terms_grounds_when_present_and_is_always_moderate()
    {
        var points = NegotiationPointRanker.Rank(Inputs(paymentTerms: "Net 30"));

        var point = Assert.Single(points, p => p.Topic == NegotiationPointTopic.PaymentTerms);
        Assert.Contains("Net 30", point.Current);
        Assert.Equal(NegotiationPointStrength.Moderate, point.Strength);
        Assert.Equal([$"fact:{ContractId}:paymentTerms"], point.CitationKeys);
    }

    // ----- Topic key / label mapping (host consumption surface) -----

    [Theory]
    [InlineData(NegotiationPointTopic.AboveBandPrice, "above-band-price")]
    [InlineData(NegotiationPointTopic.UncappedOrHighLiability, "uncapped-or-high-liability")]
    [InlineData(NegotiationPointTopic.AutoRenewShortNotice, "auto-renew-short-notice")]
    [InlineData(NegotiationPointTopic.SlaCredits, "sla-credits")]
    [InlineData(NegotiationPointTopic.TermVolume, "term-volume")]
    [InlineData(NegotiationPointTopic.PaymentTerms, "payment-terms")]
    public void Every_topic_has_a_stable_kebab_case_point_key(NegotiationPointTopic topic, string expectedKey)
    {
        Assert.Equal(expectedKey, topic.ToPointKey());
    }

    [Fact]
    public void Every_topic_has_a_non_blank_display_label()
    {
        foreach (var topic in Enum.GetValues<NegotiationPointTopic>())
        {
            Assert.False(string.IsNullOrWhiteSpace(topic.ToDisplayLabel()));
        }
    }
}
