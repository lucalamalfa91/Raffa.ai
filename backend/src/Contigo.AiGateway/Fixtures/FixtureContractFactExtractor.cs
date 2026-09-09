using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace Contigo.AiGateway.Fixtures;

/// <summary>
/// Deterministic, rule-based stand-in for the <c>extract</c> role's structured-output model
/// (<see cref="FixtureAiGateway.ExtractAsync"/>). Until task E13/F01 landed a live Foundry
/// gateway, the fixture answered every extraction stage with an empty <c>{}</c>: honest, but it
/// meant no document processed on a fixture-backed host (local, CI, and the deployed <c>dev</c>
/// while ADR-008's AI services account does not exist) ever carried a single evidenced fact —
/// every stage reported "nothing extracted", every document parked in <c>needs_review</c> with
/// zero fields to review, and the review screen had no page, span or confidence to show.
///
/// <para>
/// This extractor keeps the fixture provider-free and deterministic while producing facts that
/// are <em>true of the document</em>: every value is read from the text by a regular expression,
/// every <c>sourceSpan</c> is the literal matched text, every <c>sourcePage</c> is resolved from
/// the <c>[[PAGE n]]</c> markers the pipeline puts in front of each page, and the confidence is not
/// a guess about the model but a statement about the rule that fired — see the three constants
/// below. It never fabricates a fact the text does not contain: a document without an annual-fee
/// sentence gets no <c>annualSpend</c>, a preamble that names two parties without saying which is
/// the supplier gets a <em>low-confidence</em> supplier proposal, and two conflicting amounts for
/// the same field produce one low-confidence fact rather than a silently picked winner. That is
/// exactly the shape a human-in-the-loop review needs (product spec §7.3), and what lets a clean
/// sample contract complete while an ambiguous one is routed to review — for real, not scripted.
/// </para>
///
/// <para>
/// Stage names are matched on the <c>ExtractionStage</c> enum's own <c>ToString()</c> values
/// (<c>Metadata</c>, <c>CommercialTerms</c>, <c>DatesAndRenewalTerms</c>, ...) by string, because
/// this project sits below <c>Contigo.Documents.Contracts</c> in the ADR-002 dependency direction
/// and cannot reference the enum. The "one row = one fact" list stages (line items, clauses,
/// obligations, risks) return an empty list: nothing in a rule-based fixture can honestly attribute
/// a clause type or a risk severity, and an empty list is a legitimate outcome for a master
/// agreement (an MSA typically has no priced line items at all).
/// </para>
/// </summary>
public static class FixtureContractFactExtractor
{
    /// <summary>An explicit, unambiguous cue fired exactly once (a role-labelled party name, one
    /// currency, one annual amount). Above spec §7.3's &gt;95% "auto-accept" line.</summary>
    public const double StrongConfidence = 0.96;

    /// <summary>A clear cue that still involves a reading (an "effective" clause read as status
    /// "active", a preamble "dated" date read as the effective date). Flagged, never blocking.</summary>
    public const double GoodConfidence = 0.9;

    /// <summary>A value computed from other explicit facts rather than read directly (the end date
    /// from start + initial term, the cancellation deadline from end − notice period).</summary>
    public const double DerivedConfidence = 0.86;

    /// <summary>An ambiguous or conflicting reading: two different amounts for the same field, a
    /// renewal clause that both affirms and denies auto-renewal, a party named without a role.
    /// Below every review bar in the pipeline (0.6 ordinary, 0.8 critical) and the screen (80%),
    /// so the document lands in <c>needs_review</c> with this fact in the list.</summary>
    public const double WeakConfidence = 0.52;

    private const RegexOptions Options = RegexOptions.CultureInvariant | RegexOptions.IgnoreCase;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
    };

    private static readonly Regex PageMarkerRegex = new(@"\[\[PAGE (?<n>\d+)\]\]", RegexOptions.CultureInvariant);

    /// <summary>A count written as digits ("36"), as a number word ("thirty-six", "forty five"),
    /// optionally followed by the digits in parentheses ("thirty-six (36)"). Only genuine number
    /// words qualify — an open <c>[a-z]+</c> would swallow the verb before "ninety" in "gives ninety
    /// (90) days" and lose the fact.</summary>
    private const string NumberWordPattern =
        @"(?:(?:twenty|thirty|forty|fifty|sixty|seventy|eighty|ninety)(?:[-\s](?:one|two|three|four|five|six|seven|eight|nine))?" +
        @"|ten|eleven|twelve|thirteen|fourteen|fifteen|sixteen|seventeen|eighteen|nineteen|one|two|three|four|five|six|seven|eight|nine)";

    private const string NumberToken = @"\b(?<n>\d+|" + NumberWordPattern + @")\b\s*(?:\(\s*\d+\s*\))?";

    private const string DateToken =
        @"(?<date>\d{4}-\d{2}-\d{2}|\d{1,2}(?:st|nd|rd|th)?\s+[A-Z][a-z]+\s+\d{4}|[A-Z][a-z]+\s+\d{1,2}(?:st|nd|rd|th)?,?\s+\d{4})";

    private const string PartyToken = @"[A-Z][A-Za-z0-9&'’.\-]*(?:\s+(?!and\b|effective\b|dated\b|as\b|for\b|whereby\b|on\b)[A-Za-z0-9&'’.\-]+){0,5}";

    private static readonly Regex LabelledSupplierRegex = new(
        @"(?<![A-Za-z0-9])(?<name>[A-Z][A-Za-z0-9&.,'’\- ]{2,80}?)\s*,?\s*\(\s*(?:the\s+|hereinafter\s+)?[""“”']?(?:Supplier|Provider|Service Provider|Vendor|Contractor|Licensor)[""“”']?\s*\)",
        RegexOptions.CultureInvariant);

    private static readonly Regex SupplierLineRegex = new(@"\bSupplier\s*:\s*(?<name>[^\n.;]{2,80})", Options);

    private static readonly Regex PreambleRegex = new(
        @"\bbetween\s+(?<a>" + PartyToken + @")\s+and\s+(?<b>" + PartyToken + ")",
        RegexOptions.CultureInvariant);

    private static readonly Regex CurrencyRegex = new(@"(?<![A-Za-z])(?<cur>EUR|USD|GBP|CHF|€|\$|£)\s?(?<amt>\d[\d.,]*)", RegexOptions.CultureInvariant);

    private static readonly Regex GoverningLawRegex = new(
        @"governed by(?: and construed in accordance with)? the laws? of (?<law>[^.,;\n]{2,60})",
        Options);

    /// <summary>Legal-form suffixes that legitimately follow a comma inside a party name
    /// ("Salesforce, Inc.") — see <see cref="NormalizePartyName"/>.</summary>
    private static readonly HashSet<string> LegalSuffixes = new(StringComparer.OrdinalIgnoreCase)
    {
        "Inc", "Inc.", "LLC", "L.L.C.", "Ltd", "Ltd.", "Limited", "GmbH", "AG", "SA", "S.A.", "SpA", "S.p.A.",
        "BV", "B.V.", "NV", "N.V.", "LLP", "Corp", "Corp.", "Co", "Co.", "PLC", "plc", "Srl", "S.r.l.", "Oy", "AB",
    };

    private static readonly Regex DraftRegex = new(@"\bdraft\b", Options);
    private static readonly Regex EffectiveRegex = new(@"\beffective(?:\s+(?:as of|on|from|date))?\s*:?\s*" + DateToken, Options);
    private static readonly Regex DatedRegex = new(@"\b(?:dated|as of)\s+" + DateToken, Options);
    private static readonly Regex EffectiveCueRegex = new(@"\beffective\b[^.;\n]{0,40}", Options);

    private static readonly Regex AnnualCueRegex = new(@"\b(?:annual|annually|per year|per annum|yearly|a year|each year|p\.a\.)\b", Options);
    private static readonly Regex TotalCueRegex = new(@"\b(?:total contract value|TCV|total fees|total value|aggregate fees|total amount)\b", Options);

    private static readonly Regex PaymentWithinRegex = new(
        @"\b(?:payable|paid|due|payment)\b[^.\n]{0,40}?\bwithin\s+" + NumberToken + @"\s*(?:calendar\s+|business\s+)?days",
        Options);
    private static readonly Regex NetTermsRegex = new(@"\bnet\s?(?<n>\d{1,3})\b", Options);

    private static readonly Regex StartDateRegex = new(
        @"\b(?:commenc(?:es|ing|ement date)|start(?:s|ing)?\s+(?:on|date)|term\s+(?:begins|starts)\s+on)\b[^.\n]{0,20}?" + DateToken,
        Options);
    private static readonly Regex EndDateRegex = new(
        @"\b(?:expires?|expiring|expiry|until|through|ends?\s+on|end date|terminates on)\b[^.\n]{0,15}?" + DateToken,
        Options);
    private static readonly Regex InitialTermRegex = new(
        @"\binitial term\b[^.\n]{0,30}?" + NumberToken + @"\s*(?<unit>months?|years?)",
        Options);
    private static readonly Regex RenewalTermRegex = new(
        @"\b(?:successive|renewal|further|additional)\s+(?:periods?\s+of\s+|terms?\s+of\s+)?" + NumberToken + @"[-\s]?(?<unit>months?|years?)",
        Options);
    private static readonly Regex AnnualRenewalTermRegex = new(
        @"\b(?:successive|renewal|further|additional)\s+(?:one-year|annual|twelve-month|12-month)\s+(?:periods?|terms?)",
        Options);
    private static readonly Regex AutoRenewPositiveRegex = new(
        @"(?<!\b(?:not|no|never|without)\s)(?:\b(?:renews?|renewed|renewal|extends?|extended)\s+automatically|\bautomatically\s+(?:renews?|renewed|extends?|extended)|\bauto-?renew(?:s|al|ing)?\b)",
        Options);
    private static readonly Regex AutoRenewNegativeRegex = new(
        @"\b(?:shall|will|does|do|may)\s+not\s+(?:automatically\s+)?(?:renew|be renewed|auto-?renew|extend)\b|\bno automatic renewal\b|\bwithout automatic renewal\b|\bnot\s+renew\s+automatically\b",
        Options);
    private static readonly Regex NoticeRegex = new(
        NumberToken + @"\s*(?:calendar\s+)?days['’]?\s*(?:prior\s+|advance\s+)?(?:written\s+)?notice",
        Options);

    private static readonly Dictionary<string, int> NumberWords = new(StringComparer.OrdinalIgnoreCase)
    {
        ["one"] = 1, ["two"] = 2, ["three"] = 3, ["four"] = 4, ["five"] = 5, ["six"] = 6, ["seven"] = 7,
        ["eight"] = 8, ["nine"] = 9, ["ten"] = 10, ["eleven"] = 11, ["twelve"] = 12, ["thirteen"] = 13,
        ["fourteen"] = 14, ["fifteen"] = 15, ["sixteen"] = 16, ["seventeen"] = 17, ["eighteen"] = 18,
        ["nineteen"] = 19, ["twenty"] = 20, ["thirty"] = 30, ["forty"] = 40, ["fifty"] = 50, ["sixty"] = 60,
        ["seventy"] = 70, ["eighty"] = 80, ["ninety"] = 90,
    };

    /// <summary>
    /// Returns the payload JSON for <paramref name="stageName"/> over <paramref name="documentText"/>
    /// (page-marked or not). Always well-formed JSON: <c>{"facts":[...]}</c> for the three scalar
    /// stages, <c>{"items":[]}</c> for the four list stages, <c>{}</c> for a stage this fixture does
    /// not know.
    /// </summary>
    public static string Extract(string stageName, string documentText)
    {
        var pages = new PageMap(documentText);

        var facts = NormalizeStage(stageName) switch
        {
            "metadata" => ExtractMetadata(documentText, pages),
            "commercialterms" => ExtractCommercialTerms(documentText, pages),
            "datesandrenewalterms" => ExtractDatesAndRenewalTerms(documentText, pages),
            "lineitems" or "legalclauses" or "obligations" or "risk" => null,
            _ => (List<Fact>?)[],
        };

        if (facts is null)
        {
            return """{"items":[]}""";
        }

        return JsonSerializer.Serialize(new { facts }, JsonOptions);
    }

    private static string NormalizeStage(string stageName) =>
        stageName.Replace("-", string.Empty, StringComparison.Ordinal)
            .Replace("_", string.Empty, StringComparison.Ordinal)
            .Replace(" ", string.Empty, StringComparison.Ordinal)
            .ToLowerInvariant() switch
        {
            "dates" or "datesandrenewal" => "datesandrenewalterms",
            "commercial" => "commercialterms",
            "clauses" => "legalclauses",
            "risks" => "risk",
            var other => other,
        };

    // ----- metadata --------------------------------------------------------------------------

    private static List<Fact> ExtractMetadata(string text, PageMap pages)
    {
        var facts = new List<Fact>();

        var supplier = ExtractSupplier(text, pages);
        if (supplier is not null)
        {
            facts.Add(supplier);
        }

        var currency = ExtractCurrency(text, pages);
        if (currency is not null)
        {
            facts.Add(currency);
        }

        var law = GoverningLawRegex.Match(text);
        if (law.Success)
        {
            var value = TrimName(law.Groups["law"].Value);
            if (value.Length > 0)
            {
                facts.Add(Fact.Create("governingLaw", value, law, pages, StrongConfidence));
            }
        }

        var status = ExtractStatus(text, pages);
        if (status is not null)
        {
            facts.Add(status);
        }

        return facts;
    }

    private static Fact? ExtractSupplier(string text, PageMap pages)
    {
        var labelled = LabelledSupplierRegex.Match(text);
        if (labelled.Success)
        {
            var name = NormalizePartyName(labelled.Groups["name"].Value);
            if (name.Length > 0)
            {
                return Fact.Create("supplier", name, labelled, pages, StrongConfidence);
            }
        }

        var line = SupplierLineRegex.Match(text);
        if (line.Success)
        {
            var name = NormalizePartyName(line.Groups["name"].Value);
            if (name.Length > 0)
            {
                return Fact.Create("supplier", name, line, pages, GoodConfidence);
            }
        }

        // A preamble names two parties but says nothing about which one supplies. The second
        // party is the conventional reading — proposed, never trusted: a reviewer confirms it.
        var preamble = PreambleRegex.Match(text);
        if (preamble.Success)
        {
            var name = NormalizePartyName(preamble.Groups["b"].Value);
            if (name.Length > 0)
            {
                return Fact.Create("supplier", name, preamble, pages, WeakConfidence);
            }
        }

        return null;
    }

    private static Fact? ExtractCurrency(string text, PageMap pages)
    {
        var matches = CurrencyRegex.Matches(text);
        if (matches.Count == 0)
        {
            return null;
        }

        var codes = matches.Select(m => NormalizeCurrency(m.Groups["cur"].Value)).Distinct(StringComparer.Ordinal).ToList();
        var first = matches[0];
        return Fact.Create(
            "currency",
            NormalizeCurrency(first.Groups["cur"].Value),
            first,
            pages,
            codes.Count == 1 ? StrongConfidence : WeakConfidence);
    }

    private static Fact? ExtractStatus(string text, PageMap pages)
    {
        var draft = DraftRegex.Match(text);
        if (draft.Success)
        {
            return Fact.Create("status", "draft", draft, pages, GoodConfidence);
        }

        var effective = EffectiveCueRegex.Match(text);
        if (effective.Success)
        {
            return Fact.Create("status", "active", effective, pages, GoodConfidence);
        }

        return null;
    }

    // ----- commercial terms --------------------------------------------------------------------

    private static List<Fact> ExtractCommercialTerms(string text, PageMap pages)
    {
        var facts = new List<Fact>();

        var annual = ExtractCuedAmount(text, pages, "annualSpend", AnnualCueRegex);
        if (annual is not null)
        {
            facts.Add(annual);
        }

        var total = ExtractCuedAmount(text, pages, "totalContractValue", TotalCueRegex);
        if (total is not null)
        {
            facts.Add(total);
        }

        var payment = ExtractPaymentTerms(text, pages);
        if (payment is not null)
        {
            facts.Add(payment);
        }

        return facts;
    }

    /// <summary>An amount whose surrounding sentence carries <paramref name="cue"/>. One distinct
    /// amount is a strong fact; two or more different amounts for the same field are a conflict
    /// the text itself does not resolve, so the first is proposed at <see cref="WeakConfidence"/>.</summary>
    private static Fact? ExtractCuedAmount(string text, PageMap pages, string field, Regex cue)
    {
        var candidates = new List<(Match Match, string Amount)>();
        foreach (Match match in CurrencyRegex.Matches(text))
        {
            var sentence = SentenceAround(text, match.Index, match.Length);
            if (!cue.IsMatch(sentence))
            {
                continue;
            }

            var amount = NormalizeAmount(match.Groups["amt"].Value);
            if (amount is not null)
            {
                candidates.Add((match, amount));
            }
        }

        if (candidates.Count == 0)
        {
            return null;
        }

        var distinct = candidates.Select(c => c.Amount).Distinct(StringComparer.Ordinal).Count();
        var (first, value) = candidates[0];
        return Fact.Create(field, value, first, pages, distinct == 1 ? StrongConfidence : WeakConfidence);
    }

    private static Fact? ExtractPaymentTerms(string text, PageMap pages)
    {
        var candidates = new List<(Match Match, int Days)>();
        foreach (Match match in PaymentWithinRegex.Matches(text))
        {
            if (TryParseCount(match.Groups["n"].Value, out var days))
            {
                candidates.Add((match, days));
            }
        }

        foreach (Match match in NetTermsRegex.Matches(text))
        {
            if (int.TryParse(match.Groups["n"].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var days))
            {
                candidates.Add((match, days));
            }
        }

        if (candidates.Count == 0)
        {
            return null;
        }

        var distinct = candidates.Select(c => c.Days).Distinct().Count();
        var (first, value) = candidates.OrderBy(c => c.Match.Index).First();
        return Fact.Create(
            "paymentTerms",
            "Net " + value.ToString(CultureInfo.InvariantCulture),
            first,
            pages,
            distinct == 1 ? StrongConfidence : WeakConfidence);
    }

    // ----- dates and renewal terms -----------------------------------------------------------

    private static List<Fact> ExtractDatesAndRenewalTerms(string text, PageMap pages)
    {
        var facts = new List<Fact>();

        var effective = ExtractEffectiveDate(text, pages);
        if (effective is not null)
        {
            facts.Add(effective.Value.Fact);
        }

        DateOnly? startDate = null;
        var start = StartDateRegex.Match(text);
        if (start.Success && TryParseDate(start.Groups["date"].Value, out var parsedStart))
        {
            startDate = parsedStart;
            facts.Add(Fact.Create("startDate", Iso(parsedStart), start, pages, StrongConfidence));
        }
        else if (effective is not null)
        {
            startDate = effective.Value.Date;
            facts.Add(Fact.Create("startDate", Iso(effective.Value.Date), effective.Value.Match, pages, DerivedConfidence));
        }

        int? initialTermMonths = null;
        var initialTerm = InitialTermRegex.Match(text);
        if (initialTerm.Success && TryParseCount(initialTerm.Groups["n"].Value, out var termCount))
        {
            initialTermMonths = ToMonths(termCount, initialTerm.Groups["unit"].Value);
        }

        DateOnly? endDate = null;
        var end = EndDateRegex.Match(text);
        if (end.Success && TryParseDate(end.Groups["date"].Value, out var parsedEnd))
        {
            endDate = parsedEnd;
            facts.Add(Fact.Create("endDate", Iso(parsedEnd), end, pages, StrongConfidence));
        }
        else if (startDate is { } s && initialTermMonths is { } months)
        {
            endDate = s.AddMonths(months).AddDays(-1);
            facts.Add(Fact.Create("endDate", Iso(endDate.Value), initialTerm, pages, DerivedConfidence));
        }

        var autoRenewal = ExtractAutoRenewal(text, pages);
        if (autoRenewal is not null)
        {
            facts.Add(autoRenewal);
        }

        var renewalTerm = ExtractRenewalTerm(text, pages);
        if (renewalTerm is not null)
        {
            facts.Add(renewalTerm);
        }

        var notice = NoticeRegex.Match(text);
        if (notice.Success && endDate is { } e && TryParseCount(notice.Groups["n"].Value, out var noticeDays))
        {
            facts.Add(Fact.Create("cancellationDeadline", Iso(e.AddDays(-noticeDays)), notice, pages, DerivedConfidence));
        }

        return facts;
    }

    private static (Fact Fact, DateOnly Date, Match Match)? ExtractEffectiveDate(string text, PageMap pages)
    {
        var candidates = new List<(Match Match, DateOnly Date, double Confidence)>();
        foreach (Match match in EffectiveRegex.Matches(text))
        {
            if (TryParseDate(match.Groups["date"].Value, out var date))
            {
                candidates.Add((match, date, StrongConfidence));
            }
        }

        if (candidates.Count == 0)
        {
            foreach (Match match in DatedRegex.Matches(text))
            {
                if (TryParseDate(match.Groups["date"].Value, out var date))
                {
                    candidates.Add((match, date, GoodConfidence));
                }
            }
        }

        if (candidates.Count == 0)
        {
            return null;
        }

        var distinct = candidates.Select(c => c.Date).Distinct().Count();
        var (first, firstDate, confidence) = candidates[0];
        var fact = Fact.Create("effectiveDate", Iso(firstDate), first, pages, distinct == 1 ? confidence : WeakConfidence);
        return (fact, firstDate, first);
    }

    private static Fact? ExtractAutoRenewal(string text, PageMap pages)
    {
        var positive = AutoRenewPositiveRegex.Match(text);
        var negative = AutoRenewNegativeRegex.Match(text);

        if (positive.Success && !negative.Success)
        {
            return Fact.Create("autoRenewal", "true", positive, pages, StrongConfidence);
        }

        if (negative.Success && !positive.Success)
        {
            return Fact.Create("autoRenewal", "false", negative, pages, StrongConfidence);
        }

        if (positive.Success && negative.Success)
        {
            // The document both affirms and denies automatic renewal — a reviewer has to read it.
            return Fact.Create("autoRenewal", "true", positive, pages, WeakConfidence);
        }

        return null;
    }

    private static Fact? ExtractRenewalTerm(string text, PageMap pages)
    {
        var term = RenewalTermRegex.Match(text);
        if (term.Success && TryParseCount(term.Groups["n"].Value, out var count))
        {
            var months = ToMonths(count, term.Groups["unit"].Value);
            return Fact.Create("renewalTermMonths", months.ToString(CultureInfo.InvariantCulture), term, pages, StrongConfidence);
        }

        var annual = AnnualRenewalTermRegex.Match(text);
        if (annual.Success)
        {
            return Fact.Create("renewalTermMonths", "12", annual, pages, GoodConfidence);
        }

        return null;
    }

    // ----- helpers ----------------------------------------------------------------------------

    private static string TrimName(string raw)
    {
        var trimmed = raw.Trim().TrimEnd(',', ';', ':', '-', ' ').Trim();
        return trimmed.StartsWith("the ", StringComparison.OrdinalIgnoreCase) ? trimmed[4..].Trim() : trimmed;
    }

    /// <summary>
    /// A party name as a signature block writes it, minus a trailing place ("Northwind Traders SA,
    /// Lisbon" → "Northwind Traders SA") but keeping a comma-separated legal form ("Salesforce,
    /// Inc." stays whole). Only single-word trailing segments are treated as a place; anything
    /// longer is left alone rather than guessed at.
    /// </summary>
    private static string NormalizePartyName(string raw)
    {
        var segments = TrimName(raw).Split(',').Select(s => s.Trim()).Where(s => s.Length > 0).ToList();
        while (segments.Count > 1)
        {
            var last = segments[^1];
            var isSingleWord = !last.Contains(' ');
            if (isSingleWord && !LegalSuffixes.Contains(last))
            {
                segments.RemoveAt(segments.Count - 1);
                continue;
            }

            break;
        }

        return string.Join(", ", segments);
    }

    private static string NormalizeCurrency(string raw) => raw switch
    {
        "€" => "EUR",
        "$" => "USD",
        "£" => "GBP",
        var code => code.ToUpperInvariant(),
    };

    /// <summary>"48,000" → "48000"; "48.000,50" → "48000.50"; "1,250.75" → "1250.75"; "36000" →
    /// "36000". Returns <see langword="null"/> for something that only looked like a number.</summary>
    private static string? NormalizeAmount(string raw)
    {
        var trimmed = raw.Trim().TrimEnd('.', ',');
        if (trimmed.Length == 0)
        {
            return null;
        }

        if (Regex.IsMatch(trimmed, @"^\d+$", RegexOptions.CultureInvariant))
        {
            return trimmed;
        }

        // Thousands groups with an optional two-digit decimal tail, either separator convention.
        var grouped = Regex.Match(trimmed, @"^(?<int>\d{1,3}(?:[.,]\d{3})+)(?:(?<sep>[.,])(?<dec>\d{1,2}))?$", RegexOptions.CultureInvariant);
        if (grouped.Success)
        {
            var integer = grouped.Groups["int"].Value.Replace(".", string.Empty, StringComparison.Ordinal)
                .Replace(",", string.Empty, StringComparison.Ordinal);
            return grouped.Groups["dec"].Success ? integer + "." + grouped.Groups["dec"].Value : integer;
        }

        var plainDecimal = Regex.Match(trimmed, @"^(?<int>\d+)[.,](?<dec>\d{1,2})$", RegexOptions.CultureInvariant);
        if (plainDecimal.Success)
        {
            return plainDecimal.Groups["int"].Value + "." + plainDecimal.Groups["dec"].Value;
        }

        return null;
    }

    private static bool TryParseCount(string token, out int value)
    {
        var cleaned = token.Trim();
        if (int.TryParse(cleaned, NumberStyles.Integer, CultureInfo.InvariantCulture, out value))
        {
            return value > 0;
        }

        value = 0;
        foreach (var part in cleaned.Split([' ', '-'], StringSplitOptions.RemoveEmptyEntries))
        {
            if (!NumberWords.TryGetValue(part, out var partValue))
            {
                value = 0;
                return false;
            }

            value += partValue;
        }

        return value > 0;
    }

    private static int ToMonths(int count, string unit) =>
        unit.StartsWith("year", StringComparison.OrdinalIgnoreCase) ? count * 12 : count;

    private static readonly string[] DateFormats =
    [
        "yyyy-MM-dd", "d MMMM yyyy", "dd MMMM yyyy", "MMMM d, yyyy", "MMMM dd, yyyy", "MMMM d yyyy",
        "d MMM yyyy", "MMM d, yyyy",
    ];

    private static bool TryParseDate(string raw, out DateOnly date)
    {
        var cleaned = Regex.Replace(raw.Trim(), @"(?<=\d)(st|nd|rd|th)", string.Empty, RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
        return DateOnly.TryParseExact(cleaned, DateFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out date);
    }

    private static string Iso(DateOnly date) => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    /// <summary>The sentence containing a match: from the previous sentence terminator (or page
    /// marker / start of text) to the next one.</summary>
    private static string SentenceAround(string text, int index, int length)
    {
        var start = index;
        while (start > 0 && text[start - 1] is not ('.' or ';' or '\n'))
        {
            start--;
        }

        var end = index + length;
        while (end < text.Length && text[end] is not ('.' or ';' or '\n'))
        {
            end++;
        }

        return text[start..end];
    }

    /// <summary>Resolves a character offset to the 1-based page whose <c>[[PAGE n]]</c> marker
    /// precedes it. <see langword="null"/> when the text carries no markers at all — a page is
    /// reported only when it is really known.</summary>
    private sealed class PageMap
    {
        private readonly List<(int Offset, int Page)> _markers = [];

        public PageMap(string text)
        {
            foreach (Match marker in PageMarkerRegex.Matches(text))
            {
                if (int.TryParse(marker.Groups["n"].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var page))
                {
                    _markers.Add((marker.Index, page));
                }
            }
        }

        public int? PageAt(int offset)
        {
            if (_markers.Count == 0)
            {
                return null;
            }

            int? page = null;
            foreach (var (markerOffset, markerPage) in _markers)
            {
                if (markerOffset <= offset)
                {
                    page = markerPage;
                }
                else
                {
                    break;
                }
            }

            return page ?? _markers[0].Page;
        }
    }

    private sealed record Fact(string Field, string Value, int? SourcePage, string SourceSpan, double Confidence)
    {
        /// <summary>The span is the literal matched text, single-spaced and capped well under the
        /// <c>extraction_evidence.source_span</c> column's 500 characters.</summary>
        public static Fact Create(string field, string value, Match match, PageMap pages, double confidence)
        {
            var span = Regex.Replace(match.Value.Trim(), @"\s+", " ", RegexOptions.CultureInvariant);
            if (span.Length > 300)
            {
                span = span[..300];
            }

            return new Fact(field, value, pages.PageAt(match.Index), span, confidence);
        }
    }
}
