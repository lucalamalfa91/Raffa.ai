using System.Text;
using Raffa.Chat.Application.Guards;
using Raffa.Chat.Application.Language;
using Raffa.Chat.Application.Pack;

namespace Raffa.Chat.Application.Drafting;

/// <summary>
/// The deterministic fallback of the drafting workflow (ADR-030 D3): an email written from the
/// pack's own values with no model call — the renewal date, the notice deadline, the annual
/// spend, the top asks — every clause conditional on its fact being present, every number
/// formatted exactly the way <see cref="NumericGuard"/> reads it back, so the result passes
/// <see cref="DraftGuard"/> by construction and the user always leaves with a usable draft
/// (never an abstain). Digit-free wording everywhere else, like the playbook.
/// </summary>
public static class NegotiationEmailTemplate
{
    private static readonly string[] IsoCurrencies = ["CHF", "EUR", "USD", "GBP"];

    public static DraftOutcome Build(string language, string supplierName, IReadOnlyList<PackItem> pack, DraftPlan? plan)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(supplierName);
        ArgumentNullException.ThrowIfNull(pack);

        plan ??= DraftPlan.FromPack(pack);
        var italian = QuestionLanguage.IsItalian(language);
        var used = new List<string>();

        var (endDate, endDateKey) = FindDate(pack, "endDate", "renewalDate");
        var (deadline, deadlineKey) = FindDate(pack, "cancellationDeadline", "noticeDeadline");
        var (spend, spendKey) = FindSpend(pack);

        Track(used, endDateKey);
        Track(used, deadlineKey);
        Track(used, spendKey);

        var body = new StringBuilder();
        body.AppendLine(italian ? $"Gentile team {supplierName}," : $"Dear {supplierName} team,");
        body.AppendLine();

        var opening = new StringBuilder(italian
            ? "vi scrivo in merito al rinnovo del nostro contratto"
            : "I am writing about the renewal of our contract");
        if (endDate is not null)
        {
            opening.Append(italian ? $", in scadenza il {endDate}" : $", which ends on {endDate}");
        }

        if (deadline is not null)
        {
            opening.Append(italian ? $" (termine per la disdetta: {deadline})" : $" (notice deadline: {deadline})");
        }

        opening.Append('.');
        if (spend is not null)
        {
            opening.Append(italian ? $" Il valore annuo attuale è di {spend}." : $" The current annual value is {spend}.");
        }

        body.AppendLine(opening.ToString());
        body.AppendLine();

        var asks = plan.Asks.Where(a => !string.IsNullOrWhiteSpace(a.Sentence)).Take(3).ToList();
        if (!string.IsNullOrWhiteSpace(plan.Trade))
        {
            asks.Add(new DraftAsk("playbook", plan.Trade, PlaybookKeys(pack)));
        }

        body.AppendLine(italian
            ? "Prima di confermare il rinnovo vorremmo rivedere insieme le condizioni economiche:"
            : "Before we confirm the renewal we would like to review the commercial terms together:");
        if (asks.Count == 0)
        {
            body.AppendLine(italian
                ? "- una revisione del prezzo alla luce di quanto pagano oggi clienti comparabili;"
                : "- a price revision in line with what comparable customers pay today;");
            body.AppendLine(italian
                ? "- un tetto agli aumenti annui e la flessibilità sui volumi per la durata del contratto."
                : "- a cap on annual increases and volume flexibility for the whole term.");
        }
        else
        {
            foreach (var ask in asks)
            {
                body.AppendLine("- " + ask.Sentence.Trim());
                foreach (var key in ask.CitationKeys)
                {
                    Track(used, key);
                }
            }
        }

        body.AppendLine();
        body.AppendLine(italian
            ? "Restiamo disponibili a un confronto nei prossimi giorni e vi chiediamo di inviarci una proposta aggiornata prima della scadenza indicata, così da poter decidere il rinnovo con serenità."
            : "We remain available to discuss this in the coming days and ask you to send us a revised proposal before the deadline above, so that we can decide on the renewal in good time.");
        body.AppendLine();
        body.AppendLine(italian ? "Cordiali saluti," : "Kind regards,");
        body.AppendLine(italian ? "[Nome e cognome]" : "[Name and surname]");
        body.Append(italian ? "[Azienda]" : "[Company]");

        var subject = italian
            ? $"Rinnovo {supplierName} – richiesta di revisione delle condizioni"
            : $"{supplierName} renewal – request to revise the commercial terms";

        if (used.Count == 0 && pack.Count > 0)
        {
            used.Add(pack[0].CitationKey);
        }

        return new DraftOutcome(subject, body.ToString(), used, DraftSource.Template, 0, null, []);
    }

    private static void Track(List<string> used, string? key)
    {
        if (key is not null && !used.Contains(key, StringComparer.Ordinal))
        {
            used.Add(key);
        }
    }

    private static (string? Value, string? Key) FindDate(IReadOnlyList<PackItem> pack, params string[] keys)
    {
        foreach (var key in keys)
        {
            foreach (var item in pack)
            {
                var value = item.Values.FirstOrDefault(v => v.Kind == PackValueKind.Date && string.Equals(v.Key, key, StringComparison.OrdinalIgnoreCase));
                if (value is not null && !string.IsNullOrWhiteSpace(value.Value))
                {
                    return (value.Value, item.CitationKey);
                }
            }
        }

        return (null, null);
    }

    /// <summary>"{CUR} {value}" only when the currency is one <see cref="NumericGuard"/> checks —
    /// a contract whose currency never resolved ("n/a") gets no spend sentence rather than an
    /// unguardable figure.</summary>
    private static (string? Value, string? Key) FindSpend(IReadOnlyList<PackItem> pack)
    {
        foreach (var item in pack)
        {
            var value = item.Values.FirstOrDefault(v =>
                v.Kind == PackValueKind.Amount &&
                string.Equals(v.Key, "annualSpend", StringComparison.OrdinalIgnoreCase) &&
                v.Currency is not null &&
                IsoCurrencies.Contains(v.Currency, StringComparer.OrdinalIgnoreCase));
            if (value is not null)
            {
                return ($"{value.Currency!.ToUpperInvariant()} {value.Value}", item.CitationKey);
            }
        }

        return (null, null);
    }

    private static IReadOnlyList<string> PlaybookKeys(IReadOnlyList<PackItem> pack) =>
        pack.Where(i => i.CitationKey.StartsWith("raffa:playbook:", StringComparison.Ordinal))
            .Select(i => i.CitationKey)
            .Take(1)
            .ToList();
}
