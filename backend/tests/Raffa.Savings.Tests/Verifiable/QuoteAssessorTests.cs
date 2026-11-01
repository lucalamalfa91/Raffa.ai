using System.Text.Json.Nodes;
using Raffa.Savings.Application.Verifiable;
using Raffa.SharedKernel.Text;

namespace Raffa.Savings.Tests.Verifiable;

/// <summary>
/// The AskRaffa 4.0 quote check (<see cref="QuoteAssessor"/>, port of <c>SQ.assess</c>) and savings
/// report (<see cref="SavingsReport"/>, port of <c>SQ.report</c>): every case must reproduce the
/// prototype's own output (<c>sq-golden.json</c>) in English and — through the generated catalogue
/// <c>web/src/i18n/it/quote4.ts</c> — in Italian. No market benchmark, no estimate: a quote is compared
/// only with the customer's own contract, their target, other offers received and the policy; quantity
/// reductions never count as negotiation.
/// </summary>
public sealed class QuoteAssessorTests
{
    public static TheoryData<int> CaseIndexes()
    {
        var data = new TheoryData<int>();
        for (var i = 0; i < SqFixtures.Golden["cases"]!.AsArray().Count; i++)
        {
            data.Add(i);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(CaseIndexes))]
    public void Assess_reproduces_the_prototype(int index)
    {
        var g = SqFixtures.Golden["cases"]![index]!;
        var lang = g["lang"]!.GetValue<string>();
        var it = lang == "it" ? SqFixtures.Quote4Catalogue().Concat(SqFixtures.GapsItalian()).ToDictionary(kv => kv.Key, kv => kv.Value) : null;
        string R(CaseText t) => t.Render(it is null ? null : id => it.TryGetValue(id, out var x) ? x : null);

        var q = SqFixtures.Quote(g["quote"]!.GetValue<string>());
        var a = QuoteAssessor.Assess(q, SqFixtures.Options(g["opts"]));
        var name = g["name"]!.GetValue<string>() + "/" + lang;

        Assert.True(g["verdict"]!.GetValue<string>() == a.Verdict, name + ": verdict " + a.Verdict);
        Assert.Equal(g["basis"]!.GetValue<string>(), a.Basis);
        Assert.Equal(g["total"]!.GetValue<double>(), a.Total, 6);
        AssertNullable(g["curTotal"], a.CurTotal);
        AssertNullable(g["own"], a.Own);
        AssertNullable(g["targetTotal"], a.TargetTotal);
        Assert.Equal(g["targetMet"] is JsonValue tm ? tm.GetValue<bool>() : null, a.TargetMet);
        Assert.Equal(g["partial"]!.GetValue<bool>(), a.Partial);
        Assert.Equal(
            g["points"]!.AsArray().Select(p => string.Join(" | ", p!["kind"], p["tone"], p["text"], p["src"])),
            a.Points.Select(p => string.Join(" | ", p.Kind, p.Tone, R(p.Text), R(p.Src))));
        Assert.Equal(g["missing"]!.AsArray().Select(x => x!.GetValue<string>()), a.Missing.Select(R));
        Assert.Equal(g["gaps"]!.AsArray().Select(x => x!.GetValue<string>()), a.Gaps.Select(R));
        Assert.Equal(g["evaluable"]!.AsArray().Select(x => x!.GetValue<string>()), a.Evaluable.Select(R));
        var lines = g["lines"]!.AsArray();
        Assert.Equal(lines.Count, a.Lines.Count);
        for (var i = 0; i < lines.Count; i++)
        {
            Assert.Equal(lines[i]!["annual"]!.GetValue<double>(), a.Lines[i].Annual, 6);
            AssertNullable(lines[i]!["currentAnnual"], a.Lines[i].CurrentAnnual);
            AssertNullable(lines[i]!["delta"], a.Lines[i].Delta);
            Assert.Equal(lines[i]!["pos"]!.GetValue<string>(), a.Lines[i].Pos);
        }

        if (g["counter"] is JsonValue counter)
        {
            Assert.Equal(counter.GetValue<string>(), CounterEmail.Build(q, a, "Marta Keller"));
        }
    }

    [Fact]
    public void Every_verdict_is_covered()
    {
        var verdicts = SqFixtures.Golden["cases"]!.AsArray().Select(c => c!["verdict"]!.GetValue<string>()).ToHashSet();
        Assert.Equal(
            new[] { QuoteVerdicts.NoCompare, QuoteVerdicts.MissesTarget, QuoteVerdicts.MeetsTarget, QuoteVerdicts.BelowCurrent, QuoteVerdicts.AboveCurrent, QuoteVerdicts.Same }.ToHashSet(),
            verdicts);
    }

    [Fact]
    public void Nothing_comparable_gives_nocompare_with_what_is_missing_never_a_guess()
    {
        var a = QuoteAssessor.Assess(SqFixtures.Quote("q1"));
        Assert.Equal(QuoteVerdicts.NoCompare, a.Verdict);
        Assert.Null(a.CurTotal);
        Assert.Null(a.Own);
        Assert.Empty(a.Comparisons);
        Assert.Equal(
            ["your current contract price for these lines", "a target set by you (collected in the renewal chat)", "other offers for the same need"],
            a.Missing.Select(m => m.Render()));
        Assert.DoesNotContain(a.Points, p => p.Text.Render().Contains("market", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Comparisons_are_against_the_customers_own_contract_price()
    {
        var a = QuoteAssessor.Assess(SqFixtures.Quote("qc1"));
        var c = Assert.Single(a.Comparisons);
        Assert.Equal((148, 156), (c.Quoted, c.Current));
        Assert.Equal("Your current price", c.Source.Render());
        Assert.Equal("contratto attuale", c.Source.Render(id => id == "{label}⟦source⟧" ? "contratto attuale" : null));
        Assert.Equal(QuoteVerdicts.BelowCurrent, a.Verdict);
    }

    [Fact]
    public void Formatting_matches_the_prototype()
    {
        foreach (var f in SqFixtures.Golden["fmt"]!.AsArray())
        {
            if (f!["n"] is JsonValue n)
            {
                Assert.Equal(f["k"]!.GetValue<string>(), QuoteFormat.K(n.GetValue<double>()));
                Assert.Equal(f["up"]!.GetValue<string>(), QuoteFormat.Up(n.GetValue<double>()));
            }
            else
            {
                Assert.Equal(f["pct"]!.GetValue<string>(), QuoteFormat.Pct(f["d"]!.GetValue<double>()));
            }
        }

        Assert.Equal("CHF 156k", QuoteFormat.K(156000));
        Assert.Equal("CHF 2.4M", QuoteFormat.K(2.4e6));
        Assert.Equal(SqFixtures.Golden["kr"]!.GetValue<string>(), QuoteFormat.Kr(120000, 180000));
    }

    public static TheoryData<int> ReportIndexes()
    {
        var data = new TheoryData<int>();
        for (var i = 0; i < SqFixtures.Golden["reports"]!.AsArray().Count; i++)
        {
            data.Add(i);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(ReportIndexes))]
    public void Report_reproduces_the_prototype(int index)
    {
        var g = SqFixtures.Golden["reports"]![index]!;
        var f = g["f"]!;
        var t = g["targets"]!;
        var r = SavingsReport.Build(new SavingsReportInput
        {
            Filter = new SavingsFilter(
                f["sup"]?.GetValue<string>(), f["cat"]?.GetValue<string>(), f["cats"]?.AsArray().Select(x => x!.GetValue<string>()).ToList(),
                f["owner"]?.GetValue<string>(), f["period"]?.GetValue<string>()),
            Targets = new SavingsTargets(t["annual"]?.GetValue<double>(), t["noticeDays"]?.GetValue<int>()),
            Hist = SqFixtures.Hist(),
            Open = SqFixtures.Open(),
            Spend = g["spend"]!.GetValue<double>(),
            Today = SqFixtures.Today,
        });
        var o = g["out"]!;
        Assert.Equal(o["contractual"]!.GetValue<double>(), r.Contractual);
        Assert.Equal(o["realized"]!.GetValue<double>(), r.Realized);
        Assert.Equal(o["volumeAmt"]!.GetValue<double>(), r.VolumeAmt);
        Assert.Equal(o["count"]!.GetValue<int>(), r.Count);
        Assert.Equal(o["volumeN"]!.GetValue<int>(), r.VolumeN);
        Assert.Equal(o["objectives"]!.GetValue<double>(), r.Objectives);
        Assert.Equal(o["undefinedN"]!.GetValue<int>(), r.UndefinedN);
        Assert.Equal(o["target"]!.GetValue<double>(), r.Target);
        Assert.Equal(o["pctTarget"]!.GetValue<double>(), r.PctTarget, 12);
        Assert.Equal(o["pctSpend"]!.GetValue<double>(), r.PctSpend, 12);
        Assert.Equal(
            o["months"]!.AsArray().Select(m => $"{m!["label"]} {m["amount"]} {m["count"]} {m["future"]}"),
            r.Months.Select(m => $"{m.Label} {m.Amount} {m.Count} {(m.Future ? "true" : "false")}"));
        Assert.Equal(o["peak"]!.GetValue<double>(), r.Peak);
        Assert.Equal(
            o["by"]!.AsArray().Select(x => $"{x!["label"]} {x["contractual"]} {x["realized"]} {x["objectives"]} {x["n"]} {x["total"]}"),
            r.By.Select(x => $"{x.Label} {x.Contractual} {x.Realized} {x.Objectives} {x.N} {x.Total}"));
        Assert.Equal(o["maxBy"]!.GetValue<double>(), r.MaxBy);
        Assert.Equal(o["recent"]!.AsArray().Select(x => x!.GetValue<string>()), r.Recent.Select(x => x.Id));
        Assert.Equal(o["volumeItems"]!.AsArray().Select(x => x!.GetValue<string>()), r.VolumeItems.Select(x => x.Id));
        Assert.Equal(o["open"]!.AsArray().Select(x => x!.GetValue<string>()), r.Open.Select(x => x.Supplier));
        Assert.Equal(o["noticeSoon"]!.AsArray().Select(x => x!.GetValue<string>()), r.NoticeSoon.Select(x => x.Supplier));
        Assert.Equal(o["supplierOpts"]!.AsArray().Select(x => x!.GetValue<string>()), r.SupplierOpts);
        Assert.Equal(o["catOpts"]!.AsArray().Select(x => x!.GetValue<string>()), r.CatOpts);
        Assert.Equal(o["ownerOpts"]!.AsArray().Select(x => x!.GetValue<string>()), r.OwnerOpts);
    }

    [Fact]
    public void Quantity_reductions_never_count_as_negotiation()
    {
        var hist = SqFixtures.Hist();
        var r = SavingsReport.Build(new SavingsReportInput { Filter = new SavingsFilter(Period: "12m"), Hist = hist, Today = SqFixtures.Today });
        var volume = hist.Where(h => h.Kind == "volume" && SavingsReport.InPeriod(h.When, "12m", SqFixtures.Today)).ToList();
        Assert.NotEmpty(volume);
        Assert.All(r.Recent, h => Assert.NotEqual("volume", h.Kind));
        Assert.Equal(volume.Sum(h => h.Amount), r.VolumeAmt);
        Assert.Equal(hist.Where(h => h.Kind != "volume" && SavingsReport.InPeriod(h.When, "12m", SqFixtures.Today)).Sum(h => h.Amount), r.Contractual);
        Assert.DoesNotContain(r.Months.SelectMany(m => m.Items), h => h.Kind == "volume");
        Assert.DoesNotContain(r.By, g => g.Label is "Workday" or "Atlassian"); // volume-only suppliers are not negotiation results

        // Only-volume history: nothing negotiated, everything shown apart.
        var onlyVolume = SavingsReport.Build(new SavingsReportInput { Filter = new SavingsFilter(Period: "12m"), Hist = volume, Today = SqFixtures.Today });
        Assert.Equal(0, onlyVolume.Contractual);
        Assert.Equal(0, onlyVolume.Count);
        Assert.Equal(volume.Count, onlyVolume.VolumeN);

        // Realized is invoice-proven only.
        Assert.True(r.Realized <= r.Contractual);
        Assert.Equal(hist.Where(h => h.Kind != "volume" && h.Realized && SavingsReport.InPeriod(h.When, "12m", SqFixtures.Today)).Sum(h => h.RealizedAmount ?? 0), r.Realized);
    }

    [Fact]
    public void Contractual_formula_is_old_minus_new_unit_price_times_contracted_quantity()
    {
        Assert.Equal(41600, SavingsReport.ContractualAnnual(2.60, 2.34, 160000), 6);
        Assert.Equal(300 * 12 * 12, SavingsReport.ContractualAnnual(156, 144, 300, 12));
        Assert.Equal(9000, SavingsReport.AvoidedUplift(90000, 10), 6);
        Assert.Equal(150 * 200, SavingsReport.QuantityReduction(150, 200));
    }

    private static void AssertNullable(JsonNode? expected, double? actual)
    {
        if (expected is null)
        {
            Assert.Null(actual);
            return;
        }

        Assert.NotNull(actual);
        Assert.Equal(expected.GetValue<double>(), actual!.Value, 6);
    }
}
