using System.Text.RegularExpressions;
using Raffa.SharedKernel;
using Raffa.SharedKernel.Contracts;

namespace Raffa.Chat.Application.Ask4;

/// <summary>
/// The prototype's <c>answer(text, cid)</c> (AskRaffa V4.dc.html) over one validated contract's
/// <see cref="ContractFacts"/>: deterministic, keyword-routed, every answer with its source.
/// Used by the fixture answer service so fixture mode shows exactly the prototype's texts.
/// </summary>
public static partial class ContractFactsAnswerer
{
    public static AskAnswer Answer(string text, ContractFacts c, DateOnly today, string lang)
    {
        var t = text.ToLowerInvariant();
        var en = lang == "en";
        var d = c.Deep;
        var id = new EntityId(c.ContractId);
        AskAnswer A(string body, string? src, AskAnswerCta? cta = null) => new(false, body, src, cta, id, c.Supplier);

        if (AskRules.IsMarketQuestion(text))
        {
            return AskRules.MarketRefusal(c, lang);
        }

        if (Liability().IsMatch(t))
        {
            var r = d.Raise.FirstOrDefault(x => x.Topic.Contains("liab", StringComparison.OrdinalIgnoreCase));
            var ev = r?.EvidenceKey is { } key && c.Evidence.TryGetValue(key, out var e) ? e : null;
            var quote = ev is null ? "" : "«" + ev.Quote + "»";
            var src = ev is not null ? $"{ev.Doc} p.{ev.Page} §{ev.Sec}" : r?.Source;
            return A(c.Supplier + ": " + (r?.Says ?? "—") + " " + quote, src);
        }

        var cancelDays = AskRules.CancelDays(c, today);
        if (Notice().IsMatch(t))
        {
            var src = d.Push.FirstOrDefault(x => x.Topic.Contains("renew", StringComparison.OrdinalIgnoreCase))?.Source
                ?? (en ? "contract" : "contratto");
            if (c.NoticeDeadline is null && string.IsNullOrWhiteSpace(c.Cancel))
            {
                // No notice date was read from the contract: say so, never "0 days left" (CancelDays is 0 by default).
                var unknown = en
                    ? $"The validated fields of {c.Supplier} carry no notice deadline. Check the renewal clause in the contract before relying on a date."
                    : $"Nei campi validati di {c.Supplier} non c’è una scadenza di disdetta. Verifica la clausola di rinnovo nel contratto prima di contare su una data.";
                return A(unknown, en ? "validated fields" : "campi validati");
            }

            var body = en
                ? $"Written notice by {c.Cancel} — {c.Notice} days before the end on {c.End}. {cancelDays} days left. " +
                  (c.Auto == "Yes" ? "Without notice it renews for 12 months." : "No auto-renewal.")
                : $"Disdetta scritta entro il {c.Cancel} — {c.Notice} giorni prima della fine del {c.End}. Mancano {cancelDays} giorni. " +
                  (c.Auto == "Yes" ? "Senza disdetta si rinnova per 12 mesi." : "Nessun rinnovo automatico.");
            return A(body, src);
        }

        if (Price().IsMatch(t))
        {
            var products = string.Join(" · ", d.Products.Select(p => $"{p.Name} CHF {AskRules.JsNumber(p.UnitPrice)} / {p.Unit}"));
            var body = en
                ? $"{c.Supplier}: annual spend CHF {AskRules.Thousands(c.Spend)} · uplift at renewal {c.Uplift}. Prices from the contract, no external data: {products}."
                : $"{c.Supplier}: spesa annua CHF {AskRules.Thousands(c.Spend)} · aumento al rinnovo {c.Uplift}. Prezzi dal contratto, nessun dato esterno: {products}.";
            return A(body, en ? "the contract's price table" : "tabella prezzi del contratto");
        }

        if (End().IsMatch(t))
        {
            var body = en
                ? $"{c.Name} ends on {c.End}. Notice by {c.Cancel} ({cancelDays} days)."
                : $"{c.Name} termina il {c.End}. Disdetta entro il {c.Cancel} ({cancelDays} giorni).";
            return A(body, "§ 2.1 · § 8.4");
        }

        if (Clause().IsMatch(t))
        {
            var push = string.Join(" · ", d.Push.Select(x => x.Topic + " — " + x.Proposal));
            var raise = string.Join(", ", d.Raise.Select(x => x.Topic));
            var body = en ? $"To discuss: {push}. To raise: {raise}." : $"Da discutere: {push}. Da sollevare: {raise}.";
            return A(body, "Contract 360 · " + string.Join(", ", d.Push.Select(x => x.Source)));
        }

        if (Obligation().IsMatch(t))
        {
            var you = string.Join(" · ", d.OblYou.Select(o => $"{o.What} ({o.When})"));
            var sup = string.Join(" · ", d.OblSup.Select(o => o.What));
            var body = en ? $"Us: {you}. {c.Supplier}: {sup}." : $"Noi: {you}. {c.Supplier}: {sup}.";
            return A(body, en ? "obligations from the contract" : "obblighi dal contratto");
        }

        if (Summary().IsMatch(t))
        {
            var body = en
                ? $"{c.Supplier} — {c.Name}: CHF {AskRules.Thousands(c.Spend)} a year, ends on {c.End}, notice by {c.Cancel} ({cancelDays} days), uplift {c.Uplift}. {d.Push.Count} clauses to discuss."
                : $"{c.Supplier} — {c.Name}: CHF {AskRules.Thousands(c.Spend)} l’anno, termina il {c.End}, disdetta entro il {c.Cancel} ({cancelDays} giorni), aumento {c.Uplift}. {d.Push.Count} clausole da discutere.";
            return A(body, en ? "validated fields" : "campi validati", new AskAnswerCta("openContract", id));
        }

        return AskRules.NoEvidence(c, lang);
    }

    [GeneratedRegex("responsabil|liabil|massimale")]
    private static partial Regex Liability();

    [GeneratedRegex("disdett|preavviso|notice")]
    private static partial Regex Notice();

    [GeneratedRegex("aumento|uplift|rincaro|prezzo|price|pag|spes|cost")]
    private static partial Regex Price();

    [GeneratedRegex("scad|fine|termina|expire|end")]
    private static partial Regex End();

    [GeneratedRegex("clausol|clause|cambiare|negozi")]
    private static partial Regex Clause();

    [GeneratedRegex("obblig|dobbiamo|must")]
    private static partial Regex Obligation();

    [GeneratedRegex("riassum|summar|panoram")]
    private static partial Regex Summary();
}
