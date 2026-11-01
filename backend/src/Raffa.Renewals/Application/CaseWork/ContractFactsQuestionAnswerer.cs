using System.Globalization;
using System.Text.RegularExpressions;
using Raffa.SharedKernel;
using Raffa.SharedKernel.Cases;
using Raffa.SharedKernel.Contracts;
using Raffa.SharedKernel.Text;

namespace Raffa.Renewals.Application.CaseWork;

/// <summary>
/// Default <see cref="ICaseQuestionAnswerer"/>: answers a question about one contract only from its
/// validated facts (dates, notice, prices, clauses, obligations, liability evidence) and always names
/// the source — or says it has no reliable basis. It never answers market questions (no benchmark in
/// 4.0). Same rules as the prototype's <c>answer()</c>; the Chat module may register a sourced-answer
/// implementation over the full document instead.
/// </summary>
public sealed partial class ContractFactsQuestionAnswerer(IContractFactsReader factsReader, IClock clock) : ICaseQuestionAnswerer
{
    private static CaseText M(string id, params (string, object?)[] vars) => CaseText.Message(id, vars);

    public async Task<CaseAnswer> AnswerAsync(TenantId tenantId, EntityId contractId, string text, CancellationToken cancellationToken = default)
    {
        var facts = await factsReader.GetAsync(tenantId, contractId, cancellationToken).ConfigureAwait(false);
        if (facts is null)
        {
            return new CaseAnswer(M("I cannot find this contract among the validated ones."), M("no evidence above threshold"));
        }

        return Answer(facts, text, DateOnly.FromDateTime(clock.UtcNow.UtcDateTime));
    }

    public static CaseAnswer Answer(ContractFacts c, string question, DateOnly today)
    {
        var t = (question ?? string.Empty).ToLowerInvariant();
        var d = c.Deep;
        var cancel = c.NoticeDeadline is { } nd ? Date(nd) : c.Cancel;
        var end = c.EndDate is { } ed ? Date(ed) : c.End;
        var days = c.NoticeDeadline is { } nd2 ? nd2.DayNumber - today.DayNumber : c.CancelDays;
        var spend = c.Spend.ToString("#,##0", CultureInfo.InvariantCulture);

        if (Market().IsMatch(t))
        {
            return new CaseAnswer(
                M("I have no reliable market benchmark in this release and I do not invent one. I compare only with your contract, your target and the offers you receive."),
                M("declared limit"));
        }

        if (Liability().IsMatch(t))
        {
            var r = d.Raise.FirstOrDefault(x => x.Topic.Contains("liab", StringComparison.OrdinalIgnoreCase));
            if (r is null)
            {
                return None();
            }

            var ev = r.EvidenceKey is { } k && c.Evidence.TryGetValue(k, out var e) ? e : null;
            var quote = ev is null ? string.Empty : " «" + ev.Quote + "»";
            var src = ev is null ? r.Source : ev.Doc + " p." + ev.Page.ToString(CultureInfo.InvariantCulture) + " §" + ev.Sec;
            return new CaseAnswer(CaseText.Of(c.Supplier + ": " + r.Says + quote), CaseText.Of(src));
        }

        if (Notice().IsMatch(t))
        {
            var renew = d.Push.FirstOrDefault(x => x.Topic.Contains("renew", StringComparison.OrdinalIgnoreCase));
            var auto = string.Equals(c.Auto, "Yes", StringComparison.OrdinalIgnoreCase);
            return new CaseAnswer(
                M(auto
                        ? "Written notice by {cancel} — {notice} days before the end on {end}. {days} days left. Without notice it renews for 12 months."
                        : "Written notice by {cancel} — {notice} days before the end on {end}. {days} days left. No automatic renewal.",
                    ("cancel", cancel), ("notice", c.Notice), ("end", end), ("days", days)),
                renew is null ? M("contract") : CaseText.Of(renew.Source));
        }

        if (Price().IsMatch(t))
        {
            var products = string.Join(" · ", d.Products.Select(p => p.Name + " CHF " + p.UnitPrice.ToString(CultureInfo.InvariantCulture) + " / " + p.Unit));
            return new CaseAnswer(
                M("{supplier}: annual spend CHF {spend} · uplift at renewal {uplift}. Prices from the contract, no external data: {products}.",
                    ("supplier", c.Supplier), ("spend", spend), ("uplift", c.Uplift), ("products", products)),
                M("contract price table"));
        }

        if (End().IsMatch(t))
        {
            return new CaseAnswer(
                M("{name} ends on {end}. Notice by {cancel} ({days} days).", ("name", c.Name), ("end", end), ("cancel", cancel), ("days", days)),
                CaseText.Of("§ 2.1 · § 8.4"));
        }

        if (Clauses().IsMatch(t))
        {
            return new CaseAnswer(
                M("To discuss: {push}. To raise: {raise}.",
                    ("push", string.Join(" · ", d.Push.Select(x => x.Topic + " — " + x.Proposal))),
                    ("raise", string.Join(", ", d.Raise.Select(x => x.Topic)))),
                CaseText.Of("Contract 360 · " + string.Join(", ", d.Push.Select(x => x.Source))));
        }

        if (Obligations().IsMatch(t))
        {
            return new CaseAnswer(
                M("Us: {you}. {supplier}: {sup}.",
                    ("you", string.Join(" · ", d.OblYou.Select(o => o.What + " (" + o.When + ")"))),
                    ("supplier", c.Supplier),
                    ("sup", string.Join(" · ", d.OblSup.Select(o => o.What)))),
                M("obligations from the contract"));
        }

        if (Summary().IsMatch(t))
        {
            return new CaseAnswer(
                M("{supplier} — {name}: CHF {spend} a year, ends on {end}, notice by {cancel} ({days} days), uplift {uplift}. {n} clauses to discuss.",
                    ("supplier", c.Supplier), ("name", c.Name), ("spend", spend), ("end", end), ("cancel", cancel), ("days", days), ("uplift", c.Uplift), ("n", d.Push.Count)),
                M("validated fields"));
        }

        return None();
    }

    private static CaseAnswer None() => new(
        M("I cannot find a reliable basis in the validated contracts to answer. Try deadlines, notice, prices, clauses, liability or obligations."),
        M("no evidence above threshold"));

    private static string Date(DateOnly d) => d.ToString("dd MMM yyyy", CultureInfo.InvariantCulture);

    [GeneratedRegex("mercato|benchmark|market|concorren")]
    private static partial Regex Market();

    [GeneratedRegex("responsabil|liabil|massimale")]
    private static partial Regex Liability();

    [GeneratedRegex("disdett|preavviso|notice")]
    private static partial Regex Notice();

    [GeneratedRegex("aumento|uplift|rincaro|prezzo|price|pag|spes|cost")]
    private static partial Regex Price();

    [GeneratedRegex("scad|fine|termina|expire|end")]
    private static partial Regex End();

    [GeneratedRegex("clausol|clause|cambiare|negozi")]
    private static partial Regex Clauses();

    [GeneratedRegex("obblig|dobbiamo|must")]
    private static partial Regex Obligations();

    [GeneratedRegex("riassum|summar|panoram")]
    private static partial Regex Summary();
}
