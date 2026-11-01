using Raffa.SharedKernel.Text;
using static Raffa.Renewals.Application.Cases.Tx;

namespace Raffa.Renewals.Application.Cases;

/// <summary>
/// The prototype's scripted demo scenario (<c>renewal-case-v4.js</c>: <c>sampleReply</c>, <c>analyze</c>,
/// <c>verifyDoc</c>, <c>OFFERS</c>, <c>FEATS</c>, the example address, the extraction's weak fields,
/// <c>p1of / p2of</c>), reproduced exactly. For demos and tests only — never wire it in production.
/// </summary>
public sealed class FixtureCaseScriptServices : ICaseScriptServices
{
    public static FixtureCaseScriptServices Instance { get; } = new();

    private static readonly (string Name, double Price, int Term, string Cover, string Mig, string Gap)[] Offers =
    [
        ("Vendor A", 0.82, 24, "4 of 5", "8 weeks", "Support SLA not stated"),
        ("Vendor B", 0.89, 12, "5 of 5", "12 weeks", "Data-migration cost not stated"),
    ];

    /// <summary><c>p1of</c>: the round-1 offer, −5%.</summary>
    public static double FirstOffer(CasePriceFacts f) => CaseFormat.Round(f.P0 * 0.95);

    public double CounterPrice(CaseContext ctx, int termMonths)
    {
        var f = CaseRules.Price(ctx);
        return CaseFormat.Round(f.P0 * (termMonths == 12 ? 0.97 : 0.923));
    }

    private static int UpliftCap(CaseContext ctx) =>
        CaseRules.HasUplift(ctx) ? (int)Math.Max(1, CaseFormat.Round(CaseRules.UpliftPct(ctx) * 0.57)) : 0;

    public string SampleReply(RenewalCase c, CaseContext ctx, int round)
    {
        var f = CaseRules.Price(ctx);
        var sup = ctx.Contract.Supplier;
        var cap = UpliftCap(ctx);
        if (round == 1)
        {
            return "Hi Luca,\n\nthanks for reaching out. For a 24-month renewal we can offer " + CaseRules.Ps(f, FirstOffer(f)) + " on " + f.P.Name +
                   " (today: " + CaseRules.Ps(f, f.P0) + ")" + (cap != 0 ? ". We cannot cap the annual uplift below " + CaseFormat.Js(cap) + "%" : string.Empty) +
                   ". On payment terms we may be flexible, subject to a credit review. The liability cap follows our standard terms and is not negotiable.\n\nBest regards,\nAnna Meyer\n" + sup;
        }

        var t = c.Limits is { Term: > 0 } l ? l.Term : 24;
        return "Hi Luca,\n\nthank you. We can do " + CaseRules.Ps(f, CounterPrice(ctx, t)) + " for " + CaseFormat.Js(t) + " months" +
               (cap != 0 ? ", with the annual uplift capped at 3%" : string.Empty) +
               ". Quarterly invoicing in advance is fine. On liability we can go to 18 months of fees, but not 24. Please confirm and we will send the revised Order Form.\n\nBest regards,\nAnna Meyer\n" + sup;
    }

    public CaseReplyAnalysis AnalyzeReply(RenewalCase c, CaseContext ctx, int rid)
    {
        var f = CaseRules.Price(ctx);
        var rep = c.Reps[rid];
        var rows = new List<CaseReplyRow>();
        var terms = new List<CaseTermUpdate>();
        var ev = "reply " + CaseFormat.Js(rid + 1);
        var cap = CaseRules.HasUplift(ctx);
        var hasLiab = c.Terms.ContainsKey("liab");
        var pol = c.Goals.Policy;
        var tgt = c.Goals.Target switch
        {
            "p5" => B("−5% target", "target −5%"),
            "p10" => B("−10% target", "target −10%"),
            "budget" => B("budget target", "target di budget"),
            _ => B("no target set", "nessun target"),
        };

        void Row(CaseText label, CaseText said, CaseText plan, string state, CaseText? warn = null) =>
            rows.Add(new CaseReplyRow { Label = label, Said = said, Plan = plan, State = state, Warn = warn ?? CaseText.Empty });

        void Set(string key, CaseText label, string state, string? val) => terms.Add(new CaseTermUpdate(key, label, state, val, ev));

        var unitPrice = B("Unit price", "Prezzo unitario");
        var termLabel = B("Term", "Durata");
        var uplift = B("Uplift", "Aumento annuo");
        var payment = B("Payment terms", "Pagamenti");
        var liab = B("Liability cap", "Limite di responsabilità");

        if (rep.Round == 1)
        {
            var p1 = FirstOffer(f);
            var dp = CaseFormat.Round((f.P0 - p1) / f.P0 * 100);
            Row(unitPrice,
                B("{price} ({delta}) — only with 24 months", "{price} ({delta}) — solo con 24 mesi",
                    ("price", CaseRules.Ps(f, p1)), ("delta", dp != 0 ? "−" + CaseFormat.Js(dp) + "%" : "=")),
                tgt, "proposed");
            Set("price", unitPrice, "proposed", CaseRules.Ps(f, p1));
            Row(termLabel, B("24 months required", "24 mesi richiesti"),
                c.Goals.Term == "custom" ? CaseText.Of(c.Goals.TermText) : B("{term} months", "{term} mesi", ("term", c.Goals.Term)),
                c.Goals.Term == "24" ? "proposed" : "conflict");
            Set("term", termLabel, "proposed", "24");
            if (cap)
            {
                var capPct = CaseFormat.Js(UpliftCap(ctx));
                Row(uplift, B("cap at {cap}%", "tetto al {cap}%", ("cap", capPct)),
                    c.Terms.ContainsKey("uplift") ? B("cap at 3% or freeze", "tetto al 3% o blocco") : B("not in your plan", "non nel tuo piano"),
                    "proposed");
                Set("uplift", uplift, "proposed", "4%");
            }

            Row(payment, B("“may be flexible, subject to credit review”", "«forse flessibili, previa verifica di credito»"),
                c.Terms.ContainsKey("payment") ? B("quarterly", "trimestrale") : B("not in your plan", "non nel tuo piano"), "unclear");
            Set("payment", payment, "unclear", null);
            Row(liab, B("standard cap, not negotiable", "tetto standard, non negoziabile"),
                hasLiab ? B("24 months", "24 mesi") : B("not in your plan", "non nel tuo piano"), "rejected",
                pol ? B("Conflicts with policy (min. 24 months)", "In conflitto con la policy (min. 24 mesi)") : null);
            Set("liab", liab, "rejected", "12");
        }
        else
        {
            var t = c.Limits is { Term: > 0 } l ? l.Term : 24;
            var p2 = CounterPrice(ctx, t);
            Row(unitPrice, CaseRules.Ps(f, p2),
                c.Limits?.PriceMax is { } pm and not 0
                    ? B("your limit: ≤ {price}", "tuo limite: ≤ {price}", ("price", CaseRules.Ps(f, pm)))
                    : tgt,
                "proposed");
            Set("price", unitPrice, "proposed", CaseRules.Ps(f, p2));
            Row(termLabel, B("{term} months", "{term} mesi", ("term", t)), B("your limit: {term} months", " tuo limite: {term} mesi", ("term", t)), "proposed");
            Set("term", termLabel, "proposed", CaseFormat.Js(t));
            if (cap)
            {
                Row(uplift, B("capped at 3%", "tetto al 3%"), B("cap at 3%", "tetto al 3%"), "proposed");
                Set("uplift", uplift, "proposed", "3%");
            }

            Row(payment, B("quarterly in advance", "trimestrale anticipato"), B("quarterly", "trimestrale"), "proposed");
            Set("payment", payment, "proposed", "quarterly");
            Row(liab, B("18 months of fees (not 24)", "18 mesi di canone (non 24)"), B("24 months", "24 mesi"), "proposed",
                pol ? B("Conflicts with policy (min. 24 months) — needs authorization", "In conflitto con la policy (min. 24 mesi) — serve autorizzazione") : null);
            Set("liab", liab, "proposed", "18");
        }

        return new CaseReplyAnalysis(rows, terms);
    }

    public IReadOnlyList<CaseDraftRow> VerifyDraft(RenewalCase c, CaseContext ctx, int version)
    {
        var f = CaseRules.Price(ctx);
        var a = c.Agreed ?? new CaseAgreed();
        var r = new List<CaseDraftRow>();
        var v1 = version == 1;

        void It(CaseText label, CaseText agreed, CaseText draft, string res, string src) =>
            r.Add(new CaseDraftRow { Label = label, Agreed = agreed, Draft = draft, Res = res, Src = src });

        It(B("Quantities", "Quantità"), RenewalCaseEngine.ProductsText(ctx), B("same", "uguali"), "ok", "p.2 §1");
        It(B("Unit price", "Prezzo unitario"), CaseRules.Ps(f, a.Price), CaseRules.Ps(f, a.Price), "ok", "p.3 §2");
        It(B("Term", "Durata"), B("{term} months", "{term} mesi", ("term", a.Term)), B("{term} months", "{term} mesi", ("term", a.Term)), "ok", "p.2 §3");
        if (a.Cap != 0)
        {
            It(B("Uplift cap", "Tetto aumento"), CaseFormat.Js(a.Cap) + "%", v1 ? "5%" : CaseFormat.Js(a.Cap) + "%", v1 ? "diff" : "ok", "p.9 §6.2");
        }

        It(B("Payment", "Pagamenti"), B("quarterly in advance", "trimestrale anticipato"),
            v1 ? B("annual in advance, Net 30", "annuale anticipato, Net 30") : B("quarterly in advance", "trimestrale anticipato"),
            v1 ? "diff" : "ok", "p.5 §5.1");
        It(B("Liability cap", "Limite di responsabilità"), B("{liab} months of fees", "{liab} mesi di canone", ("liab", a.Liab)),
            B("{liab} months of fees", "{liab} mesi di canone", ("liab", a.Liab)), "ok", "p.27 §17.2");
        var law = ctx.Contract.Terms.FirstOrDefault(t => t.Label == "Governing law")?.Value ?? "—";
        It(B("Governing law", "Legge applicabile"), law, B("same as previous contract", "come contratto precedente"), "ok", "p.32 §22");
        It(B("Annex — SLA schedule", "Allegato — SLA"), B("to be checked", "da verificare"),
            v1 ? B("not read: scanned page unreadable", "non letto: pagina scansionata illeggibile") : B("read; matches 99.9% uptime", "letto; coerente con uptime 99,9%"),
            v1 ? "unread" : "ok", "Annex B");
        return r;
    }

    public CaseSignedCheck CheckSigned(RenewalCase c, CaseContext ctx) => new(8, 0);

    public CasePipe StartExtraction(RenewalCase c, CaseContext ctx) => new()
    {
        Stage = 0,
        Weak =
        [
            new CaseWeakField { N = "uplift", Name = B("Price increase cap", "Tetto aumento prezzo"), Val = "3% (years 2–3)", Conf = 76, State = "open" },
            new CaseWeakField { N = "pay", Name = B("Payment terms", "Pagamenti"), Val = "Quarterly in advance, Net 30", Conf = 78, State = "open" },
        ],
    };

    public IReadOnlyList<string> ExampleVendors() => Offers.Select(o => o.Name).ToList();

    public CaseAltOffer VendorOffer(RenewalCase c, CaseContext ctx, string vendor, string by)
    {
        var f = CaseRules.Price(ctx);
        var o = Offers.FirstOrDefault(x => x.Name == vendor);
        if (o.Name is null)
        {
            o = Offers[0];
        }

        var reqs = c.Alt?.Reqs.Count ?? 0;
        var tot = f.P0 < 1000 ? f.Qty * CaseFormat.Round(f.P0 * o.Price) * f.Mult : CaseFormat.Round(f.P0 * o.Price);
        return new CaseAltOffer
        {
            Price = f.P0 < 1000 ? CaseFormat.Round(f.P0 * o.Price) : tot,
            Total = tot,
            Term = o.Term,
            Cover = CaseFormat.Js(o.Cover.StartsWith('5') ? reqs : reqs - 1) + " of " + CaseFormat.Js(reqs),
            Mig = o.Mig,
            Gap = o.Gap,
            By = by,
        };
    }

    public string? ExampleAddress(CaseEmail email, CaseContext ctx) =>
        email.Kind == "rfq"
            ? "rfp@" + Squash(email.Vendor ?? string.Empty) + ".example"
            : "anna.meyer@" + Squash(ctx.Contract.Supplier) + ".example";

    private static string Squash(string s) => string.Concat(s.ToLowerInvariant().Where(ch => !char.IsWhiteSpace(ch)));

    public CaseFeatureSet Features(CaseContext ctx) => ctx.Contract.Supplier switch
    {
        "DocuSign" => new("electronic signature",
        [
            B("Electronic signature with audit trail", "Firma elettronica con audit trail"), B("Templates and bulk send", "Modelli e invio massivo"),
            B("API and ERP integration", "API e integrazione ERP"), B("Identity verification options", "Opzioni di verifica dell’identità"),
            B("EU data residency", "Residenza dei dati in UE"), B("99.9% uptime SLA", "SLA di disponibilità 99,9%"),
        ]),
        "Salesforce" => new("CRM platform",
        [
            B("Pipeline and forecasting", "Pipeline e previsioni"), B("Account and contact management", "Gestione di conti e contatti"),
            B("Workflow automation", "Automazione dei flussi"), B("ERP integration", "Integrazione ERP"),
            B("EU data residency", "Residenza dei dati in UE"), B("Role-based access", "Accessi per ruolo"),
        ]),
        "Microsoft" => new("productivity suite",
        [
            B("Email and calendar", "Email e calendario"), B("Document co-authoring", "Scrittura condivisa di documenti"),
            B("Video meetings", "Riunioni in video"), B("Endpoint security", "Sicurezza dei dispositivi"),
            B("EU data residency", "Residenza dei dati in UE"), B("Admin and compliance tooling", "Strumenti di amministrazione e conformità"),
        ]),
        "AWS" => new("cloud infrastructure",
        [
            B("Elastic compute", "Calcolo elastico"), B("Managed databases", "Database gestiti"), B("Object storage", "Archiviazione a oggetti"),
            B("Committed-spend discount model", "Modello di sconto a impegno di spesa"), B("EU data residency", "Residenza dei dati in UE"),
            B("Enterprise support tier", "Livello di supporto enterprise"),
        ]),
        _ => new(null,
        [
            B("Core functions equal to your current contract", "Funzioni di base pari al tuo contratto attuale"),
            B("Integration with your ERP", "Integrazione con il tuo ERP"), B("EU data residency", "Residenza dei dati in UE"),
        ]),
    };
}
