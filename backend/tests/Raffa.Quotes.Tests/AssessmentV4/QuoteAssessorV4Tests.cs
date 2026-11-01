using Raffa.Quotes.Application.AssessmentV4;
using Raffa.Quotes.Domain;

namespace Raffa.Quotes.Tests.AssessmentV4;

/// <summary><see cref="QuoteAssessorV4"/> equals <c>SQ.assess</c> on the prototype's quotes; never a benchmark.</summary>
public sealed class QuoteAssessorV4Tests
{
    private static readonly OfferInput Databricks = new(
        "Databricks", "Databricks_Proposal_Q-88213.pdf", "12 months", "30 Sep 2026",
        [
            new("Premium DBU — committed", "DBX-PREM-DBU", 600000, "DBU", 0.55m, null, null),
            new("SQL Serverless DBU", "DBX-SQL-SRVLESS", 200000, "DBU", 0.70m, null, null),
            new("Enterprise Support", "DBX-ENT-SUPPORT", 1, "year", 50000, null, null),
        ],
        [new("payment", "Annual in advance"), new("uplift", null), new("renewal", "Auto-renews 12 months unless 60 days notice"), new("liability", null), new("sla", "99.9% uptime, credits not stated")],
        [
            new("Annual uplift not stated", "Aumento annuo non indicato"),
            new("Limitation of liability not stated", "Limite di responsabilità non indicato"),
            new("Service credits for SLA breaches not stated", "Crediti per violazioni SLA non indicati"),
            new("Data egress fees not stated", "Costi di uscita dati non indicati"),
        ],
        null);

    private static AssessOptions It(OfferTarget? target = null, string? notice = null, int? policy = null, string? planTerm = null) =>
        new("it", target, planTerm, [], notice, policy);

    [Fact]
    public void Nothing_comparable_gives_nocompare_with_what_is_missing()
    {
        var a = QuoteAssessorV4.Assess(Databricks, It());

        Assert.Equal(QuoteVerdict.NoCompare, a.Verdict);
        Assert.Equal("none", a.Basis);
        Assert.Equal(520000m, a.Total);
        Assert.Equal("CHF 520k", QuoteFormat.K(a.Total));
        Assert.Null(a.CurTotal);
        Assert.Equal(
            [
                "il prezzo del tuo contratto attuale per queste righe",
                "un obiettivo indicato da te (raccolto nella chat del rinnovo)",
                "altre offerte per la stessa esigenza",
            ],
            a.Missing);
        var gap = Assert.Single(a.Points);
        Assert.Equal(
            "Non indicato: Aumento annuo non indicato; Limite di responsabilità non indicato; Crediti per violazioni SLA non indicati; Costi di uscita dati non indicati",
            gap.Text);
        Assert.Equal("il preventivo stesso", gap.Src);
        Assert.DoesNotContain(a.Points, p => p.Text.Contains("mercato", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Own_contract_price_and_target_drive_the_verdict()
    {
        var snowflake = new OfferInput(
            "Snowflake", "Snowflake_Renewal_Offer_2026.pdf", "24 months", "15 Mar 2026",
            [new("Compute credits — Enterprise", "SNOW-ENT-CREDIT", 160000, "credit", 2.34m, 2.60m, "2024 contract price")],
            [new("liability", "12 months of fees")],
            [],
            new OfferTarget("p10", "−10% on unit price", null));

        var a = QuoteAssessorV4.Assess(snowflake, new AssessOptions("en", null, "36", [], "01 Mar 2026", 24));

        Assert.Equal(QuoteVerdict.MeetsTarget, a.Verdict);
        Assert.Equal("-10.0%", QuoteFormat.Pct(a.Own!.Value));
        Assert.Equal("Compute credits — Enterprise: CHF 2.34 per credit vs CHF 2.6 today (-10.0%)", a.Points[0].Text);
        Assert.Equal("2024 contract price", a.Points[0].Src);
        Assert.Contains(a.Points, p => p.Kind == "target" && p.Src == "your objective, not a benchmark");
        Assert.Contains(a.Points, p => p is { Kind: "term", Text: "Term 24 months — your plan asks for 36 months" });
        Assert.Contains(a.Points, p => p is { Kind: "policy", Src: "Procurement Policy v3.2" });
        Assert.Contains(a.Points, p => p.Kind == "validity");
        Assert.True(a.Points.Count <= QuoteAssessorV4.MaxPoints);
    }

    [Fact]
    public void Above_current_without_target_and_the_counter_email_asks_to_hold_the_price()
    {
        var q = Databricks with
        {
            Lines = [new("Seat", "S", 10, "user / mo", 12m, 10m, "Your current price")],
            Gaps = [new("Annual uplift not stated", "Aumento annuo non indicato")],
        };
        var a = QuoteAssessorV4.Assess(q, new AssessOptions("en", null, null, [], null, null));

        Assert.Equal(QuoteVerdict.AboveCurrent, a.Verdict);
        Assert.Equal(1440m, a.Total);
        var mail = QuoteAssessorV4.CounterEmail(q, a, "Marta Keller", "Acme AG");
        Assert.Contains("• Seat: you quoted CHF 12 per user / mo, above our current CHF 10. We ask you to hold the current price.", mail, StringComparison.Ordinal);
        Assert.Contains("• Please state in writing: Annual uplift.", mail, StringComparison.Ordinal);
        Assert.EndsWith("Marta Keller\nProcurement, Acme AG", mail, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("checked", "negotiating", true)]
    [InlineData("checked", "signed", false)]
    [InlineData("signing", "signed", true)]
    [InlineData("declined", "checked", true)]
    [InlineData("declined", "signing", false)]
    public void Decision_transitions(string from, string to, bool allowed) =>
        Assert.Equal(allowed, QuoteDecisionStatus.CanMove(from, to));
}
