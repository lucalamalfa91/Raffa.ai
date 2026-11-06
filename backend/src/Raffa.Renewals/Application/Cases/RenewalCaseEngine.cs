using System.Text.RegularExpressions;
using Raffa.SharedKernel.Text;
using static Raffa.Renewals.Application.Cases.Tx;

namespace Raffa.Renewals.Application.Cases;

/// <summary>
/// The renewal case engine — a pure, deterministic C# port of the AskRaffa 4.0 prototype's
/// <c>renewal-case-v4.js</c> (<c>RC</c>): <see cref="Create"/>, <see cref="Act"/>, <see cref="View"/>,
/// <see cref="Todo"/>, <see cref="Stage"/>. No persistence, no clock (today comes from
/// <see cref="CaseContext.Today"/>), no HTTP, no LLM: the scripted parts go through
/// <see cref="ICaseScriptServices"/>. Every user-facing text is a <see cref="CaseText"/> whose message
/// id is the prototype's English source string.
/// </summary>
public static partial class RenewalCaseEngine
{
    private const string Luca = "Luca Lamalfa";
    private const string Marta = "Marta Keller";

    private static readonly CaseText NoApprove = B("Only the approver (CPO) or an explicit delegate can approve emails.", "Solo l’approvatore (CPO) o un delegato esplicito può approvare le email.");
    private static readonly CaseText NoClose = B("Only the approver (CPO), or a delegate with closing rights, can confirm this.", "Solo l’approvatore (CPO), o un delegato con diritto di chiusura, può confermarlo.");
    private static readonly CaseText NoWork = B("Only people working on this renewal can do this.", "Solo chi lavora a questo rinnovo può farlo.");
    private static readonly CaseText NoRecipient = B("Add the recipient first — I never invent addresses.", "Aggiungi prima il destinatario — non invento mai indirizzi.");
    private static readonly CaseText NoDelegations = B("Only the approver (CPO) manages delegations.", "Solo l’approvatore (CPO) gestisce le deleghe.");

    /* ---------- create ---------- */

    /// <summary>
    /// Opens a case for contract <paramref name="cid"/>. <paramref name="branch"/> "notice" starts on the
    /// non-renewal track; anything else starts the interview. <paramref name="owner"/> defaults to the viewer.
    /// The approver is the workspace CPO (the prototype's Luca Lamalfa) unless <paramref name="approver"/> is given.
    /// </summary>
    public static RenewalCase Create(int cid, CaseContext ctx, string? branch = null, string? owner = null, string? approver = null, IReadOnlyList<string>? members = null, string? brief = null, CaseInterviewTailoring? tailoring = null)
    {
        var who = owner ?? ctx.Viewer;
        var c = new RenewalCase
        {
            Cid = cid,
            Owner = who,
            Approver = approver ?? Luca,
            Updated = ctx.Today,
            Members = members?.ToList() ?? [Luca, Marta],
            Phase = CasePhases.Interview,
            Interview = tailoring is { Questions.Count: > 0 } ? tailoring : null,
            History = [new CaseOwnerChange { Who = who, At = ctx.Today }],
        };
        Push(c, ctx, new CaseFeedItem { K = "ov" });
        if (branch == "notice")
        {
            StartNotice(c, ctx);
        }
        else
        {
            Push(c, ctx, new CaseFeedItem { K = "outlook" });
            AttachBrief(c, ctx, brief);
            Push(c, ctx, new CaseFeedItem { K = "q", I = 0 });
        }

        return c;
    }

    /// <summary>The most of the chat's levers a case keeps.</summary>
    public const int BriefMaxChars = 3500;

    /// <summary>
    /// The levers and strategy worked out in chat before this case existed: kept on the case and shown as a card, so
    /// the questions and the emails start from them instead of from zero. No-op for an empty brief or one already kept.
    /// </summary>
    public static void AttachBrief(RenewalCase c, CaseContext ctx, string? brief)
    {
        var text = (brief ?? string.Empty).Trim();
        if (text.Length == 0 || !string.IsNullOrEmpty(c.Brief))
        {
            return;
        }

        c.Brief = text.Length > BriefMaxChars ? text[..BriefMaxChars].TrimEnd() + "…" : text;
        Say(c, ctx, B(
            "I carry over what we worked out in chat. I use these levers to shape the questions and the email — tell me if anything changed:",
            "Porto con me quello che abbiamo elaborato in chat. Uso queste leve per impostare le domande e la mail — dimmi se qualcosa è cambiato:"));
        Say(c, ctx, CaseText.Of(c.Brief));
    }

    private static readonly Regex BriefTerm = new(@"(?<![\d.,])(12|24|36)\s*(?:mesi|months)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(200));

    private static readonly Regex BriefPercent = new(
        @"(?:riduzion\w*|sconto|reduction|discount|cut|taglio)[^%\n]{0,60}?(\d+(?:[.,]\d+)?)\s*%", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(200));

    private static readonly Regex BriefVolumes = new(
        @"(?:riduzion\w*|ridimension\w*|reduc\w*)\s+(?:dei\s+|of\s+(?:the\s+)?|the\s+)?(?:volum\w*|quantit\w*)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(200));

    /// <summary>What the chat's levers say about interview question <paramref name="question"/> (0 products, 1 term,
    /// 2 economic goal), as one line under the question; null when they say nothing about it.</summary>
    internal static CaseText? BriefHint(string? brief, int question)
    {
        if (string.IsNullOrWhiteSpace(brief))
        {
            return null;
        }

        switch (question)
        {
            case 0 when BriefVolumes.IsMatch(brief):
                return B("From our chat: the levers include reducing volumes at renewal.", "Dalla chat: tra le leve c’è la riduzione dei volumi al rinnovo.");
            case 1 when BriefTerm.Match(brief) is { Success: true } term:
                return B("From our chat: the levers rest on a {n}-month term.", "Dalla chat: le leve si basano su una durata di {n} mesi.", ("n", term.Groups[1].Value));
            case 2 when BriefPercent.Match(brief) is { Success: true } pct:
                return B("From our chat: the levers aim at about {pct}% off.", "Dalla chat: le leve puntano a circa il {pct}% di sconto.", ("pct", pct.Groups[1].Value));
            default:
                return null;
        }
    }

    /* ---------- helpers ---------- */

    /// <summary>The recurring lines the renewal covers ("2,850 × Falcon Enterprise"); one-off amounts are
    /// listed apart by <see cref="OneOffText"/>.</summary>
    internal static string ProductsText(CaseContext ctx) =>
        string.Join(", ", ctx.Contract.Products.Where(p => !p.OneOff).Select(p => (p.Qty > 1 ? CaseFormat.Num(p.Qty) + " × " : string.Empty) + p.Name));

    /// <summary>The current contract's one-off amounts ("Onboarding fee"); empty when there are none.</summary>
    internal static string OneOffText(CaseContext ctx) =>
        string.Join(", ", ctx.Contract.Products.Where(p => p.OneOff).Select(p => p.Name));

    private static string D(DateOnly d) => CaseFormat.Date(d);

    private static int DaysTo(CaseContext ctx, DateOnly d) => CaseFormat.DaysBetween(ctx.Today, d);

    private static CaseFeedItem Push(RenewalCase c, CaseContext ctx, CaseFeedItem o)
    {
        c.Seq++;
        o.Id = "f" + CaseFormat.Js(c.Seq);
        o.St = Stage(c);
        var minute = Math.Min(59, 20 + (int)Math.Floor(c.Seq * 0.8));
        o.At = D(ctx.Today)[..6] + " · 14:" + minute.ToString("00", System.Globalization.CultureInfo.InvariantCulture);
        if (string.IsNullOrEmpty(o.By))
        {
            o.By = CaseRules.Raffa;
        }

        c.Feed.Add(o);
        return o;
    }

    private static CaseFeedItem Say(RenewalCase c, CaseContext ctx, CaseText text, string? by = null) =>
        Push(c, ctx, new CaseFeedItem { K = "say", Text = text, By = by ?? CaseRules.Raffa });

    private static CaseFeedItem UserSays(RenewalCase c, CaseContext ctx, string by, CaseText text, string? thread = null) =>
        Push(c, ctx, new CaseFeedItem { K = "user", By = by, Text = text, Thread = thread });

    private static void Log(RenewalCase c, CaseContext ctx, string by, CaseText text) =>
        c.Decs.Add(new CaseDecisionEntry { By = by, At = ctx.Today, Text = text });

    private static RenewalCase Deny(RenewalCase c, CaseText why)
    {
        c.Ui.Warn = why;
        return c;
    }

    private static CaseEmail? EmailById(RenewalCase c, int? id) => id is { } i && i >= 0 && i < c.Emails.Count ? c.Emails[i] : null;

    private static void CancelOpenReminders(RenewalCase c)
    {
        foreach (var x in c.Rems.Where(x => x.State is "set" or "due" or "proposed"))
        {
            x.State = "cancelled";
        }
    }

    private static DateOnly CapReminder(CaseContext ctx, DateOnly d)
    {
        var cap = ctx.Contract.Cancel.AddDays(-3);
        return d > cap ? cap : d;
    }

    /* ---------- interview ---------- */

    /// <summary>The person can still change their interview answers: the case is open, on the renewal path, and nothing
    /// has reached the supplier yet (no email copied or sent).</summary>
    public static bool CanChangeChoices(RenewalCase c) =>
        c.Outcome is null && c.Notice is null && c.Alt is null
        && c.Phase is CasePhases.Interview or CasePhases.Plan or CasePhases.Email
        && !c.Emails.Any(e => e.State is EmailStates.Sent or EmailStates.Copied);

    /// <summary>After an answer: the next question, or back to where the person was when they changed an earlier one.</summary>
    private static void NextQuestion(RenewalCase c, CaseContext ctx)
    {
        if (c.ResumeQ is { } resume && resume > c.Q + 1)
        {
            c.ResumeQ = null;
            c.Q = resume;
            c.Goals.Clauses ??= c.Q == 3 ? PushClauseKeys(ctx) : null;
        }
        else
        {
            c.ResumeQ = null;
            c.Q++;
            if (c.Q == 3)
            {
                c.Goals.Clauses = PushClauseKeys(ctx);
            }
        }

        Push(c, ctx, new CaseFeedItem { K = "q", I = c.Q });
    }

    private sealed record QuestionDef(CaseText Text, CaseText Hint, IReadOnlyList<(string Key, CaseText Label)> Opts, bool Multi);

    /// <summary>The question as this case words it: the standard text, with the model's wording for this contract and chat where it wrote one.</summary>
    private static QuestionDef QDef(int i, CaseContext ctx, RenewalCase? c)
    {
        var def = StandardQuestion(i, ctx);
        if (c?.Interview?.Questions.FirstOrDefault(x => x.Index == i) is not { } t)
        {
            return def;
        }

        return def with
        {
            Text = string.IsNullOrWhiteSpace(t.Text) ? def.Text : CaseText.Of(t.Text.Trim()),
            Hint = string.IsNullOrWhiteSpace(t.Hint) ? def.Hint : CaseText.Of(t.Hint.Trim()),
            Opts = t.Options is { Count: > 0 } labels
                ? def.Opts.Select(o => (o.Key, labels.TryGetValue(o.Key, out var label) && !string.IsNullOrWhiteSpace(label) ? CaseText.Of(label.Trim()) : o.Label)).ToList()
                : def.Opts,
        };
    }

    private static QuestionDef StandardQuestion(int i, CaseContext ctx)
    {
        var f = CaseRules.Price(ctx);
        var cn = ctx.Contract;
        return i switch
        {
            0 => new QuestionDef(
                B("First: what do you want to do with the products and quantities?", "Prima cosa: che cosa vuoi fare con prodotti e quantità?"),
                B("The contract has no usage data, so I cannot say how many licences are unused. Tell me if you know — I will not guess.", "Il contratto non contiene dati d’uso, quindi non posso dire quante licenze siano inutilizzate. Dimmelo se lo sai — non lo indovino."),
                [("keep", B("Keep everything as today", "Mantieni tutto come oggi")), ("reduce", B("Reduce quantities", "Riduci le quantità")), ("add", B("Add products", "Aggiungi prodotti"))],
                false),
            1 => new QuestionDef(
                B("Term: how long should the renewed contract run?", "Durata: quanto deve durare il contratto rinnovato?"),
                B("Current cycle ends {end}; the new term would start on {start}.", "Il ciclo attuale termina il {end}; il nuovo periodo partirebbe il {start}.", ("end", D(cn.End)), ("start", D(cn.End.AddDays(1)))),
                [("12", B("12 months", "12 mesi")), ("24", B("24 months", "24 mesi")), ("36", B("36 months", "36 mesi"))],
                false),
            2 => new QuestionDef(
                B("Economic goal: do you have a price or budget target?", "Obiettivo economico: hai un target di prezzo o di budget?"),
                B("“No target for now” is a valid answer. I compare only with your current contract price ({price}) and offers you receive — I have no reliable market benchmark.", "«Nessun target per ora» è una risposta valida. Confronto solo con il prezzo del tuo contratto attuale ({price}) e con le offerte che ricevi — non ho benchmark di mercato affidabili.", ("price", CaseRules.Ps(f, f.P0))),
                [
                    ("none", B("No target for now", "Nessun target per ora")), ("p5", B("−5% on unit price", "−5% sul prezzo unitario")),
                    ("p10", B("−10% on unit price", "−10% sul prezzo unitario")),
                    ("budget", B("Budget: {amount} / yr (−10%)", "Budget: {amount} / anno (−10%)", ("amount", CaseFormat.Chf(cn.Spend * 0.9)))),
                ],
                false),
            _ => new QuestionDef(
                B("Last: which clauses do you want to change?", "Ultima: quali clausole vuoi cambiare?"),
                B("Pre-selected are the ones I found costing you money or freedom. Add or remove, or upload a company policy.", "Ho preselezionato quelle che ti costano denaro o libertà. Aggiungi o togli, oppure carica una policy aziendale."),
                CaseRules.Clauses(ctx).Select(x => (x.K, CaseText.Of(x.Type))).ToList(),
                true),
        };
    }

    private static CaseText AnsLabel(RenewalCase c, CaseContext ctx, int i)
    {
        var g = c.Goals;
        if (i == 3)
        {
            var sel = g.Clauses ?? [];
            var cl = CaseRules.Clauses(ctx).Where(x => sel.Contains(x.K)).Select(x => x.Type).ToList();
            if (!string.IsNullOrEmpty(g.ClausesText))
            {
                cl.Add(g.ClausesText);
            }

            return cl.Count > 0 ? CaseText.Of(string.Join(", ", cl)) : B("none for now", "nessuna per ora");
        }

        var (v, text) = i switch
        {
            0 => (g.Prod, g.ProdText),
            1 => (g.Term, g.TermText),
            _ => (g.Target, g.TargetText),
        };
        if (v == "custom")
        {
            return CaseText.Of(text);
        }

        foreach (var o in QDef(i, ctx, c).Opts)
        {
            if (o.Key == v)
            {
                return o.Label;
            }
        }

        return CaseText.Empty;
    }

    private static void SetGoal(CaseGoals g, int q, string? val, string? text = null)
    {
        switch (q)
        {
            case 0: g.Prod = val; if (text is not null) { g.ProdText = text; } break;
            case 1: g.Term = val; if (text is not null) { g.TermText = text; } break;
            case 2: g.Target = val; if (text is not null) { g.TargetText = text; } break;
        }
    }

    private static List<string> PushClauseKeys(CaseContext ctx) =>
        CaseRules.Clauses(ctx).Where(x => x.Stance == "push").Select(x => x.K).ToList();

    /* ---------- plan ---------- */

    private static void MakePlan(RenewalCase c, CaseContext ctx)
    {
        c.PlanVer++;
        c.PlanOk = false;
        c.Phase = CasePhases.Plan;
        Push(c, ctx, new CaseFeedItem { K = "plan", Ver = c.PlanVer });
    }

    private static void InitTerms(RenewalCase c, CaseContext ctx)
    {
        c.Terms = new OrderedDictionary<string, CaseTerm>(StringComparer.Ordinal)
        {
            ["price"] = new() { Label = B("Unit price", "Prezzo unitario"), State = "open" },
            ["term"] = new() { Label = B("Term", "Durata"), State = "open" },
        };
        var sel = c.Goals.Clauses ?? [];
        foreach (var x in CaseRules.Clauses(ctx).Where(x => sel.Contains(x.K)))
        {
            c.Terms[x.Lk] = new CaseTerm { Label = x.Type, State = "open", Plan = true };
        }
    }

    private static void SetTerm(RenewalCase c, CaseTermUpdate u)
    {
        if (!c.Terms.TryGetValue(u.Key, out var t))
        {
            t = new CaseTerm { Label = u.Label, State = "open", Plan = false };
            c.Terms[u.Key] = t;
        }

        t.State = u.State;
        t.Val = u.Val;
        t.Ev = u.Ev;
    }

    private static void ConfirmPlanCore(RenewalCase c, CaseContext ctx, string v)
    {
        c.PlanOk = true;
        InitTerms(c, ctx);
        c.Phase = CasePhases.Email;
        Log(c, ctx, v, B("Plan v{planVer} confirmed", "Piano v{planVer} confermato", ("planVer", c.PlanVer)));
    }

    /* ---------- emails ---------- */

    private sealed record EmailOpts(string? To = null, bool ToGiven = false, string? Since = null, string? Thread = null, string? Vendor = null);

    private static CaseEmail MakeEmail(RenewalCase c, CaseContext ctx, string kind, EmailOpts? o = null)
    {
        o ??= new EmailOpts();
        var cn = ctx.Contract;
        var f = CaseRules.Price(ctx);
        var g = c.Goals;
        var sup = cn.Supplier;
        var owner = c.Owner;
        var today = D(ctx.Today);
        var attach = new List<string> { cn.File };
        var sel = g.Clauses ?? [];
        var cl = CaseRules.Clauses(ctx).Where(x => sel.Contains(x.K)).ToList();
        var prodTxt = g.Prod == "reduce" ? "a reduction of quantities (details to follow)"
            : g.Prod == "add" ? "additional products (details to follow)"
            : "the same products and quantities as today (" + ProductsText(ctx) + ")";
        var priceTxt = g.Target switch
        {
            "p5" => "a unit price about 5% below today’s " + CaseRules.Ps(f, f.P0),
            "p10" => "a unit price about 10% below today’s " + CaseRules.Ps(f, f.P0),
            "budget" => "an annual budget of " + CaseFormat.Chf(cn.Spend * 0.9),
            _ => "a revised price proposal",
        };
        var to = o.ToGiven ? o.To ?? string.Empty : c.Emails.LastOrDefault(e => !string.IsNullOrEmpty(e.To))?.To ?? string.Empty;
        const string sign = "\n\nKind regards,\n";
        // The buyer signs as the workspace's company; never a name Raffa made up.
        var company = cn.Company.Trim();
        var tag = company.Length == 0 ? string.Empty : " — " + company;
        var signature = sign + owner + "\nProcurement" + (company.Length == 0 ? string.Empty : ", " + company);
        string subject;
        string body;
        switch (kind)
        {
            case "first":
            {
                subject = cn.Name + " — renewal discussion" + tag;
                var lines = new List<string>
                {
                    "• Products and quantities: " + prodTxt,
                    "• Term: " + (g.Term == "custom" ? g.TermText : (g.Term ?? "null") + " months"),
                    "• Commercials: " + priceTxt,
                };
                lines.AddRange(cl.Select(x => "• " + x.Type + ": " + x.Ask));
                if (g.Prod != "reduce" && g.Prod != "add" && OneOffText(ctx) is { Length: > 0 } oneOff)
                {
                    // Paid once under the current contract: named apart so the supplier does not price it again.
                    lines.Insert(1, "• One-off items of the current contract, not part of this renewal: " + oneOff);
                }

                body = "Dear " + sup + " team,\n\nOur " + cn.Name + " agreement ends on " + D(cn.End) + ". Ahead of the " + D(cn.Cancel) +
                       " notice date we would like to open the renewal discussion.\n\nWhat we are looking for:\n" + string.Join("\n", lines) +
                       "\n\nCould you send a revised proposal by " + D(ctx.Today.AddDays(10)) + "? We are keeping all options open until we receive it." + signature;
                break;
            }

            case "followup":
                subject = "Re: " + cn.Name + " — renewal discussion — follow-up";
                body = "Dear " + sup + " team,\n\nFollowing up on our message of " + (o.Since ?? today) + " about the renewal of " + cn.Name +
                       ". We have not registered a reply on our side yet. Could you let us know when we can expect your proposal? Our notice date under the agreement is " +
                       D(cn.Cancel) + "." + signature;
                break;
            case "counter" when !ctx.Services.Scripted:
            {
                // What the supplier proposed and what we ask, in two blocks: a figure is attributed to the
                // supplier only when its reply stated it, and every request of ours is labelled as ours.
                subject = "Re: " + cn.Name + " — counter-proposal";
                var theirs = CaseAgreement.SupplierProposalLines(c);
                var ours = CaseAgreement.OurRequestLines(c, ctx, f, cl);
                var text = "Dear " + sup + " team,\n\nThank you for your proposal.";
                if (theirs.Count > 0)
                {
                    text += " This is how we read it:\n" + string.Join("\n", theirs);
                }

                text += ours.Count > 0
                    ? "\n\nOn our side we ask for:\n" + string.Join("\n", ours)
                    : "\n\nWe can move forward on this basis.";
                body = text + "\n\nIf this works for you, please confirm in writing and send the revised Order Form." + signature;
                break;
            }

            case "counter":
            {
                var l = c.Limits ?? new CaseLimits();
                var p2 = CounterPrice(c, ctx, l.Term == 12 ? 12 : 24);
                subject = "Re: " + cn.Name + " — counter-proposal";
                var lines = new[]
                {
                    "• Unit price: " + CaseRules.Ps(f, p2) + (l.Term == 24 ? " for a 24-month term" : " for a 12-month term") +
                    (l.Exit is { } ex && ex != 0 ? ", with an exit option at month " + CaseFormat.Js(ex) : string.Empty),
                    "• Uplift: capped at 3% (you offered 4%)",
                    "• Payment terms: quarterly in advance. Please clarify the credit-review conditions you mentioned",
                    "• Limitation of liability: we ask for 24 months of fees",
                };
                body = "Dear " + sup + " team,\n\nThank you for your proposal. We can move forward on the following basis:\n" + string.Join("\n", lines) +
                       "\n\nIf this works for you, please confirm in writing and send the revised Order Form." + signature;
                break;
            }

            case "docreq" when !ctx.Services.Scripted && c.Agreed is { } agreedNow:
                // Only the points the owner confirmed: a clause nobody agreed is never presented as agreed.
                subject = cn.Name + " — revised Order Form for signature";
                body = "Dear " + sup + " team,\n\nThank you for confirming. Please send the revised Order Form / amendment reflecting what we agreed:\n" +
                       string.Join("\n", CaseAgreement.AgreedLines(agreedNow, ctx, f)) +
                       "\n\nWe will review it and come back before signature." + signature;
                break;
            case "docreq":
            {
                var a = c.Agreed ?? new CaseAgreed();
                subject = cn.Name + " — revised Order Form for signature";
                body = "Dear " + sup + " team,\n\nThank you for confirming. Please send the revised Order Form / amendment reflecting what we agreed:\n• Quantities: unchanged\n• Unit price: " +
                       CaseRules.Ps(f, a.Price) + "\n• Term: " + CaseFormat.Js(a.Term) + " months\n• Uplift: capped at " + CaseFormat.Js(a.Cap ?? 0) +
                       "%\n• Payment: quarterly in advance\n• Limitation of liability: " + CaseFormat.Js(a.Liab ?? 0) +
                       " months of fees\n\nWe will review it and come back before signature." + signature;
                break;
            }

            case "fix" when !ctx.Services.Scripted:
            {
                // The differences and unreadable points of the last verified draft, as the check found them.
                subject = cn.Name + " — corrections to the draft Order Form";
                var fixes = CaseAgreement.DraftCorrectionLines(c);
                body = "Dear " + sup + " team,\n\nThank you for the draft. Before we can sign, " + fixes.Count switch
                {
                    0 => "please confirm that it reflects every point we agreed:\n" + string.Join("\n", c.Agreed is { } fa ? CaseAgreement.AgreedLines(fa, ctx, f) : []),
                    1 => "one point differs from what we agreed:\n" + fixes[0],
                    _ => "these points differ from what we agreed:\n" + string.Join("\n", fixes),
                } + signature;
                break;
            }

            case "fix":
                subject = cn.Name + " — corrections to the draft Order Form";
                body = "Dear " + sup + " team,\n\nThank you for the draft. Before we can sign, two points differ from what we agreed:\n• Uplift: the draft states 5%; we agreed a cap of " +
                       (c.Agreed is { } ag ? CaseFormat.Js(ag.Cap ?? 0) : "undefined") +
                       "%.\n• Payment: the draft states annual in advance; we agreed quarterly in advance.\nPlease also send the Annex (SLA schedule) in a readable version." + signature;
                break;
            case "notice":
                subject = "Notice of non-renewal — " + cn.Name + tag;
                body = "Dear " + sup + " team,\n\nIn accordance with the renewal clause of our agreement, " + (company.Length == 0 ? "we give" : company + " gives") + " written notice that " + (company.Length == 0 ? "we do" : "it does") + " not intend to renew " +
                       cn.Name + " beyond the current term ending " + D(cn.End) + ".\n\nThis notice is sent on " + today + ", ahead of the " + D(cn.Cancel) +
                       " deadline. Please confirm receipt in writing and share the offboarding and data-return steps set out in the agreement." + signature;
                break;
            default:
            {
                var fe = ctx.Services.Features(ctx);
                subject = "Request for proposal — " + (fe.CategoryEn is { } cat ? cat + " solution" : "replacement solution") + tag;
                attach = [];
                var what = fe.CategoryEn is { } cat2 ? (VowelStart().IsMatch(cat2) ? "an " : "a ") + cat2 + " solution" : "a replacement solution";
                var reqs = (c.Alt?.Reqs ?? []).Select(r => "• " + r.Text.Render());
                body = "Dear " + o.Vendor + " team,\n\n" + (company.Length == 0 ? "We are" : company + " is") + " looking for " + what + " from " + D(cn.End.AddDays(1)) +
                       ". We ask you to answer on functions, not on product names.\n\nFunctions we need:\n" + string.Join("\n", reqs) +
                       "\n\nSizing: " + SizeText(f) + ".\n\nFor each function please tell us how your product covers it, and send your offer with pricing, term, migration plan and support model by " +
                       D(ctx.Today.AddDays(14)) + "." + signature;
                break;
            }
        }

        var e = new CaseEmail
        {
            Id = c.Emails.Count,
            Kind = kind,
            Ver = 1,
            To = to,
            Subject = subject,
            Body = body,
            Attach = attach,
            State = EmailStates.Draft,
            Thread = o.Thread,
            Vendor = o.Vendor,
        };
        c.Emails.Add(e);
        Push(c, ctx, new CaseFeedItem { K = "email", Eid = e.Id, Thread = e.Thread });
        return e;
    }

    internal static string SizeText(CasePriceFacts f)
    {
        var u = f.Unit ?? string.Empty;
        var m = SizeRe().Match(u);
        if (!m.Success)
        {
            return CaseFormat.Num(f.Qty) + " " + u;
        }

        var unit = m.Groups[1].Value;
        return CaseFormat.Num(f.Qty) + " " + unit + (unit.EndsWith('s') ? string.Empty : "s") + (m.Groups[2].Success && m.Groups[2].Length > 0 ? " " + m.Groups[2].Value : string.Empty);
    }

    private static void AfterSent(RenewalCase c, CaseContext ctx, CaseEmail e)
    {
        if (e.Kind == "notice")
        {
            if (c.Notice is not null)
            {
                c.Notice.State = "sent";
                c.Notice.SentAt = ctx.Today;
            }

            Push(c, ctx, new CaseFeedItem { K = "nrec" });
            return;
        }

        if (e.Kind == "rfq")
        {
            Push(c, ctx, new CaseFeedItem
            {
                K = "say",
                Thread = e.Thread,
                Text = B("RFQ to {vendor} recorded as sent. Paste or attach their offer here when it arrives — it stays in this thread.", "RFQ a {vendor} registrata come inviata. Incolla o allega qui la sua offerta quando arriva — resta in questo filone.", ("vendor", e.Vendor)),
            });
            if (c.Emails.Where(x => x.Kind == "rfq").All(x => x.State == EmailStates.Sent))
            {
                c.Thread = null;
                Push(c, ctx, new CaseFeedItem { K = "recap" });
            }

            return;
        }

        ProposeReminder(c, ctx, e);
    }

    private static void ProposeReminder(RenewalCase c, CaseContext ctx, CaseEmail? e)
    {
        var d = ctx.Today.AddDays(e is { Kind: "followup" } ? 3 : c.Round != 0 ? 5 : 7);
        var cap = ctx.Contract.Cancel.AddDays(-3);
        if (d > cap)
        {
            d = cap;
        }

        var rid = c.Rems.Count;
        c.Rems.Add(new CaseReminder { Rid = rid, Date = d, State = "proposed" });
        c.Ui.RemDate = d;
        c.Ui.RemN = DaysTo(ctx, d);
        c.Ui.RemCustom = false;
        c.Phase = CasePhases.WaitingRem;
        Push(c, ctx, new CaseFeedItem { K = "rem", Rid = rid });
    }

    /* ---------- notice / alternatives ---------- */

    private static void StartNotice(RenewalCase c, CaseContext ctx)
    {
        if (c.Notice is not null)
        {
            return;
        }

        c.Notice = new CaseNotice { State = "check", Evidence = false };
        Push(c, ctx, new CaseFeedItem { K = "ncheck" });
    }

    private static void StartAlt(RenewalCase c, CaseContext ctx)
    {
        if (c.Alt is not null)
        {
            return;
        }

        c.Alt = new CaseAlt
        {
            State = "req",
            Reqs = ctx.Services.Features(ctx).Items.Select(x => new CaseAltRequirement { Text = x, On = true }).ToList(),
        };
        var sup = ctx.Contract.Supplier;
        Push(c, ctx, new CaseFeedItem
        {
            K = "info",
            Kicker = B("New chat · Replace the supplier", "Nuova chat · Sostituire il fornitore"),
            Title = B("Replacing {supplier}: eight steps, in this order", "Sostituire {supplier}: otto passi, in quest’ordine", ("supplier", sup)),
            Bullets =
            [
                B("1 · Features — what the new vendor must do (not product names)", "1 · Funzioni — cosa deve fare il nuovo fornitore (non i nomi dei prodotti)"),
                B("2 · Candidates — the vendors you want to ask", "2 · Candidati — i fornitori che vuoi interpellare"),
                B("3 · Request offers — one email per vendor, approved by the CPO", "3 · Richiesta offerte — un’email per fornitore, approvata dal CPO"),
                B("4 · Collect the offers as they arrive", "4 · Raccogli le offerte man mano che arrivano"),
                B("5 · Compare with your current contract and choose", "5 · Confronta con il tuo contratto attuale e scegli"),
                B("6 · Exit the old contract — non-renewal notice", "6 · Esci dal vecchio contratto — disdetta"),
                B("7 · Sign the new contract and add it", "7 · Firma il nuovo contratto e aggiungilo"),
                B("8 · Done — old contract closed, new one acquired", "8 · Fatto — vecchio contratto chiuso, nuovo acquisito"),
            ],
            Note = B("Your renewal stays safe: when the requests are sent I bring you back to your plan. Nothing is sent from here.", "Il tuo rinnovo resta al sicuro: quando le richieste sono inviate ti riporto al tuo piano. Da qui non parte nulla."),
        });
        Push(c, ctx, new CaseFeedItem { K = "areq" });
    }

    /* ---------- reducer ---------- */

    /// <summary>
    /// Applies one action (see <see cref="CaseActions.All"/>) and returns a new case; the input is not
    /// modified. A denied action returns the case with <c>Ui.Warn</c> set to the reason.
    /// </summary>
    public static RenewalCase Act(RenewalCase c0, string type, CaseActionPayload? payload, CaseContext ctx)
    {
        var c = c0.DeepClone();
        return ActOn(c, type, payload ?? CaseActionPayload.None, ctx);
    }

    private static RenewalCase ActOn(RenewalCase c, string type, CaseActionPayload p, CaseContext ctx)
    {
        c.Ui.Warn = null;
        var v = ctx.Viewer;
        var cn = ctx.Contract;
        var f = CaseRules.Price(ctx);
        CaseEmail? e;
        if (!CaseActions.Passive.Contains(type))
        {
            c.Updated = ctx.Today;
        }

        switch (type)
        {
            case "visit":
            {
                var seen = c.Seen.TryGetValue(v, out var s) ? s : 0;
                var news = seen == 0
                    ? []
                    : c.Feed.Where(x => int.Parse(x.Id[1..], System.Globalization.CultureInfo.InvariantCulture) > seen && x.By != v && x.K != "q").ToList();
                c.Ui.Since = news.Count > 0
                    ? new CaseSince { N = news.Count, Items = news.TakeLast(4).Select(x => new CaseSinceItem { By = x.By, K = x.K }).ToList() }
                    : null;
                c.Seen[v] = c.Seq;
                break;
            }

            case "sinceOk":
                c.Ui.Since = null;
                break;
            case "ui":
                SetUi(c, ctx, p);
                break;
            case "ans":
            {
                if (p.Q is not { } q || q is < 0 or > 2 || c.Q != q)
                {
                    break;
                }

                SetGoal(c.Goals, q, p.Val);
                UserSays(c, ctx, v, AnsLabel(c, ctx, q));
                NextQuestion(c, ctx);
                break;
            }

            case "toggle":
            {
                var a = c.Goals.Clauses ?? [];
                if (p.K is { } key)
                {
                    if (!a.Remove(key))
                    {
                        a.Add(key);
                    }
                }

                c.Goals.Clauses = a;
                break;
            }

            case "policy":
                c.Goals.Policy = true;
                Push(c, ctx, new CaseFeedItem { K = "policy", By = v });
                break;
            case "q3done":
                c.ResumeQ = null;
                UserSays(c, ctx, v, AnsLabel(c, ctx, 3));
                MakePlan(c, ctx);
                break;
            case "reask":
            {
                // Change one earlier answer: back to that question, the other answers kept, then straight back to
                // where the person was. Before anything reached the supplier only; unsent drafts are withdrawn.
                if (!CaseRules.CanWork(c, v))
                {
                    return Deny(c, NoWork);
                }

                if (p.Q is not { } rq || rq is < 0 or > 3 || !CanChangeChoices(c) || (c.Phase == CasePhases.Interview && rq >= c.Q))
                {
                    break;
                }

                c.ResumeQ = c.Phase == CasePhases.Interview ? c.Q : 3;
                c.Q = rq;
                c.PlanOk = false;
                c.Phase = CasePhases.Interview;
                c.Ui.Other = null;
                c.Ui.OtherText = string.Empty;
                c.Ui.Edit = null;
                var withdrawn = 0;
                foreach (var draft in c.Emails.Where(x => x.State is EmailStates.Draft or EmailStates.ToApprove or EmailStates.Approved))
                {
                    draft.State = EmailStates.Superseded;
                    withdrawn++;
                }

                if (withdrawn > 0)
                {
                    Log(c, ctx, v, B("Interview answer changed — unsent email drafts withdrawn", "Risposta dell’intervista cambiata — bozze email non inviate ritirate"));
                }

                Say(c, ctx, B("Okay — change your answer here. The other answers stay; then I bring you back to where you were.", "Va bene — cambia qui la risposta. Le altre risposte restano; poi ti riporto dove eri."));
                Push(c, ctx, new CaseFeedItem { K = "q", I = rq });
                break;
            }

            case "editPlan":
                c.Q = 0;
                c.PlanOk = false;
                c.Phase = CasePhases.Interview;
                Say(c, ctx, B("Okay — reopening the interview. Your previous answers stay; change what you need.", "Va bene — riapro l’intervista. Le risposte precedenti restano; cambia quello che serve."));
                Push(c, ctx, new CaseFeedItem { K = "q", I = 0 });
                break;
            case "confirmPlan":
                if (!CaseRules.CanWork(c, v))
                {
                    return Deny(c, NoWork);
                }

                ConfirmPlanCore(c, ctx, v);
                Say(c, ctx, B("Plan v{planVer} confirmed by {viewer}. Here is the first email. I do not have the supplier contact in the contract, so add the recipient before approving.", "Piano v{planVer} confermato da {viewer}. Ecco la prima email. Nel contratto non ho il contatto del fornitore: aggiungi il destinatario prima di approvare.", ("planVer", c.PlanVer), ("viewer", v)));
                MakeEmail(c, ctx, "first", new EmailOpts(To: string.Empty, ToGiven: true));
                break;
            case "requestAppr":
                if (!CaseRules.CanWork(c, v))
                {
                    return Deny(c, NoWork);
                }

                e = EmailById(c, p.Eid);
                if (e is null || e.State != EmailStates.Draft)
                {
                    break;
                }

                if (string.IsNullOrEmpty(e.To))
                {
                    c.Ui.Warn = NoRecipient;
                    break;
                }

                e.State = EmailStates.ToApprove;
                Log(c, ctx, v, B("Email v{ver} sent for approval", "Email v{ver} inviata in approvazione", ("ver", e.Ver)));
                break;
            case "approve":
                if (!CaseRules.CanApprove(c, v))
                {
                    return Deny(c, NoApprove);
                }

                e = EmailById(c, p.Eid);
                if (e is null || (e.State != EmailStates.Draft && e.State != EmailStates.ToApprove))
                {
                    break;
                }

                if (string.IsNullOrEmpty(e.To))
                {
                    c.Ui.Warn = NoRecipient;
                    break;
                }

                e.State = EmailStates.Approved;
                e.ApprovedBy = v;
                e.ApprovedVer = e.Ver;
                Log(c, ctx, v, B("Email v{ver} approved", "Email v{ver} approvata", ("ver", e.Ver)));
                break;
            case "edit":
                e = EmailById(c, p.Eid);
                if (e is not null)
                {
                    c.Ui.Edit = e.Id;
                    c.Ui.EditBody = e.Body;
                }

                break;
            case "cancelEdit":
                c.Ui.Edit = null;
                break;
            case "reviseEmail":
            {
                // Raffa's proposal for a change the person asked for in the composer: a new version of the
                // draft, never sent and never approved. Same effect as saving an edit (version up, approval
                // invalidated), so the person reviews it in the panel like any other draft.
                if (!CaseRules.CanWork(c, v))
                {
                    return Deny(c, NoWork);
                }

                e = EmailById(c, p.Eid);
                var revised = (p.Body ?? string.Empty).Trim();
                if (e is null || revised.Length == 0 || e.State is EmailStates.Sent or EmailStates.Copied or EmailStates.Superseded)
                {
                    break;
                }

                e.Body = revised;
                if (!string.IsNullOrWhiteSpace(p.Subject))
                {
                    e.Subject = p.Subject.Trim();
                }

                e.Ver++;
                if (e.State != EmailStates.Draft)
                {
                    Log(c, ctx, v, B("Email revised by Raffa on request — previous approval invalidated", "Email rivista da Raffa su richiesta — approvazione precedente invalidata"));
                }

                e.State = EmailStates.Draft;
                e.ApprovedBy = null;
                e.ApprovedVer = null;
                c.Ui.Edit = null;
                if (p.Text is { } note && (note.IsMessage || !string.IsNullOrEmpty(note.Plain)))
                {
                    Say(c, ctx, note);
                }

                break;
            }

            case "saveEdit":
                e = EmailById(c, p.Eid);
                if (e is null)
                {
                    break;
                }

                if (c.Ui.EditBody is { } nb && nb != e.Body)
                {
                    e.Body = nb;
                    e.Ver++;
                    if (e.State != EmailStates.Draft)
                    {
                        Log(c, ctx, v, B("Email edited — previous approval invalidated", "Email modificata — approvazione precedente invalidata"));
                    }

                    e.State = EmailStates.Draft;
                    e.ApprovedBy = null;
                    e.ApprovedVer = null;
                }

                c.Ui.Edit = null;
                break;
            case "copy":
                e = EmailById(c, p.Eid);
                if (e is null || e.State is EmailStates.Draft or EmailStates.ToApprove)
                {
                    break;
                }

                c.Clip = "To: " + e.To + "\nSubject: " + e.Subject + "\n\n" + e.Body;
                if (e.State == EmailStates.Approved)
                {
                    e.State = EmailStates.Copied;
                }

                break;
            case "sentOpen":
                c.Ui.SentOpen = p.Eid;
                c.Ui.SentDate = D(ctx.Today) + " · 14:40";
                c.Ui.SentEdited = false;
                c.Ui.SentText = string.Empty;
                break;
            case "sentCancel":
                c.Ui.SentOpen = null;
                break;
            case "sentOk":
                if (!CaseRules.CanWork(c, v))
                {
                    return Deny(c, NoWork);
                }

                e = EmailById(c, p.Eid);
                if (e is null || e.State is EmailStates.Draft or EmailStates.ToApprove or EmailStates.Superseded)
                {
                    break;
                }

                e.Sent = new CaseSentDeclaration
                {
                    By = v,
                    At = string.IsNullOrEmpty(c.Ui.SentDate) ? D(ctx.Today) : c.Ui.SentDate,
                    To = e.To,
                    Edited = c.Ui.SentEdited,
                    Text = c.Ui.SentEdited && !string.IsNullOrEmpty(c.Ui.SentText) ? c.Ui.SentText : null,
                };
                e.State = EmailStates.Sent;
                c.Ui.SentOpen = null;
                Log(c, ctx, v, e.Sent.Edited
                    ? B("Send confirmed (declared by user) — text edited after approval", "Invio confermato (dichiarato dall’utente) — testo modificato dopo l’approvazione")
                    : B("Send confirmed (declared by user)", "Invio confermato (dichiarato dall’utente)"));
                AfterSent(c, ctx, e);
                break;
            case "toEx":
                e = EmailById(c, p.Eid);
                if (e is not null && ctx.Services.ExampleAddress(e, ctx) is { } ex)
                {
                    e.To = ex;
                }

                break;
            case "remOk":
            {
                if (!CaseRules.CanWork(c, v))
                {
                    return Deny(c, NoWork);
                }

                if (p.Rid is not { } rid || rid < 0 || rid >= c.Rems.Count)
                {
                    break;
                }

                var r = c.Rems[rid];
                r.Date = c.Ui.RemDate ?? r.Date;
                r.State = "set";
                c.Phase = CasePhases.Waiting;
                Log(c, ctx, v, B("Check date set: {date}", "Data di controllo impostata: {date}", ("date", D(r.Date))));
                Say(c, ctx, B("Check date {date} confirmed. It stays active when the chat is closed and posts an app notice linked to this case. Your contractual notice date remains {cancel}.", "Data di controllo {date} confermata. Resta attiva a chat chiusa e genera un avviso nell’app collegato a questo caso. La scadenza contrattuale di disdetta resta il {cancel}.", ("date", D(r.Date)), ("cancel", D(cn.Cancel))));
                break;
            }

            case "remChange":
            {
                var rr = c.Rems.FirstOrDefault(x => x.State == "due");
                if (rr is not null)
                {
                    rr.State = "proposed";
                    c.Ui.RemDate = ctx.Today.AddDays(5);
                    rr.Date = c.Ui.RemDate.Value;
                }

                break;
            }

            case "ff":
            {
                var rs = c.Rems.FirstOrDefault(x => x.State == "set");
                if (rs is not null)
                {
                    rs.State = "due";
                    c.Phase = CasePhases.WaitingDue;
                    Say(c, ctx, B("Check date reached ({date}). No reply has been registered in Raffa — I cannot see your mailbox, so I will not say the supplier did not answer.", "Data di controllo raggiunta ({date}). Nessuna risposta è registrata in Raffa — non vedo la tua casella, quindi non dico che il fornitore non ha risposto.", ("date", D(rs.Date))));
                }

                break;
            }

            case "followup":
            {
                var last = c.Emails.LastOrDefault(x => x.State == EmailStates.Sent);
                foreach (var x in c.Rems.Where(x => x.State == "due"))
                {
                    x.State = "cancelled";
                }

                c.Phase = CasePhases.Email;
                MakeEmail(c, ctx, "followup", new EmailOpts(Since: last?.Sent is { } sd ? sd.At[..Math.Min(6, sd.At.Length)] : D(ctx.Today)));
                break;
            }

            case "openReply":
                c.Ui.ReplyOpen = true;
                c.Ui.ReplyText ??= string.Empty;
                break;
            case "closeReply":
                c.Ui.ReplyOpen = false;
                break;
            case "sampleReply":
                c.Ui.ReplyText = ctx.Services.SampleReply(c, ctx, c.Round + 1);
                break;
            case "reply":
                Reply(c, ctx, p, v, f);
                break;
            case "decide":
            {
                if (!CaseRules.CanWork(c, v))
                {
                    return Deny(c, NoWork);
                }

                if (p.Did is not { } did || did < 0 || did >= c.Qs.Count)
                {
                    break;
                }

                var q = c.Qs[did];
                if (q.State != "open")
                {
                    break;
                }

                var p2b = CounterPrice(c, ctx, 24);
                (int Term, double? PriceMax, int? Exit, CaseText Label)? o = p.Opt switch
                {
                    "a" => (24, p2b, null, B("Accept up to 24 months if the unit price is ≤ {price}", "Accetto fino a 24 mesi se il prezzo unitario è ≤ {price}", ("price", CaseRules.Ps(f, p2b)))),
                    "b" => (12, null, null, B("Stay at 12 months — decline the discount tied to 24", "Resto a 12 mesi — rifiuto lo sconto legato ai 24")),
                    "c" => (24, p2b, 12, B("Counter: 24 months with an exit option at month 12", "Controproposta: 24 mesi con uscita al mese 12")),
                    _ => null,
                };
                if (o is not { } O)
                {
                    break;
                }

                q.State = "done";
                q.Opt = p.Opt;
                q.By = v;
                q.Label = O.Label;
                c.Limits = new CaseLimits { Term = O.Term, PriceMax = O.PriceMax, Exit = O.Exit };
                Log(c, ctx, v, B("Limit recorded: {label}", "Limite registrato: {label}", ("label", O.Label)));
                Say(c, ctx, B("Registered. I will use this limit in the next turns. Here is the counter-proposal — it needs approval like any email.", "Registrato. Userò questo limite nei prossimi turni. Ecco la controproposta — richiede approvazione come ogni email."));
                MakeEmail(c, ctx, "counter");
                c.Phase = CasePhases.Email;
                break;
            }

            case "agreeOk":
            {
                if (!CaseRules.CanWork(c, v))
                {
                    return Deny(c, NoWork);
                }

                if (p.Did is not { } did || did < 0 || did >= c.Qs.Count)
                {
                    break;
                }

                var q2 = c.Qs[did];
                if (q2.State != "agree")
                {
                    break;
                }

                if (LiabilityConflict(c) && string.IsNullOrWhiteSpace(c.Ui.Auth))
                {
                    c.Ui.Warn = B("Record who authorized the exception to the policy first.", "Registra prima chi ha autorizzato la deroga alla policy.");
                    break;
                }

                CaseAgreed agreed;
                if (ctx.Services.Scripted)
                {
                    foreach (var t0 in c.Terms.Values.Where(t0 => t0.State == "proposed"))
                    {
                        t0.State = "agreed";
                    }

                    var t = c.Limits is { Term: > 0 } lim ? lim.Term : 24;
                    agreed = new CaseAgreed { Price = CounterPrice(c, ctx, t), Term = t, Cap = CaseRules.HasUplift(ctx) ? 3 : 0, Liab = 18 };
                }
                else
                {
                    // The agreed position is exactly what the owner confirms: the terms the supplier's replies
                    // proposed. Price and term must be in writing; nothing is filled in from today's contract.
                    var proposed = c.Terms.Values.Where(t0 => t0.State == "proposed").ToList();
                    proposed.ForEach(t0 => t0.State = "agreed");
                    if (CaseAgreement.FromConfirmedTerms(c, out var missing) is not { } snapshot)
                    {
                        proposed.ForEach(t0 => t0.State = "proposed");
                        c.Ui.Warn = B("The supplier’s replies do not state {what} in writing. Register the reply that confirms it, then confirm the terms.", "Le risposte del fornitore non indicano per iscritto {what}. Registra la risposta che lo conferma, poi conferma le condizioni.", ("what", missing));
                        break;
                    }

                    agreed = snapshot;
                }

                q2.State = "done";
                q2.By = v;
                c.Agreed = agreed;
                var auth = c.Ui.Auth;
                if (!string.IsNullOrEmpty(auth))
                {
                    c.Agreed.Auth = auth;
                    Log(c, ctx, v, B("Terms confirmed as agreed (evidence: reply {n}) · exception authorized by {auth} (recorded outside Raffa)", "Condizioni confermate come concordate (evidenza: risposta {n}) · deroga autorizzata da {auth} (registrata fuori da Raffa)", ("n", c.Reps.Count), ("auth", auth)));
                }
                else
                {
                    Log(c, ctx, v, B("Terms confirmed as agreed (evidence: reply {n})", "Condizioni confermate come concordate (evidenza: risposta {n})", ("n", c.Reps.Count)));
                }

                c.Phase = CasePhases.DocWait;
                Say(c, ctx, B("Agreed terms recorded. Next I need the contract draft. Here is the request email; when the draft arrives, add it with “Add contract” — it will not become an active contract.", "Condizioni concordate registrate. Ora serve la bozza di contratto. Ecco l’email di richiesta; quando arriva, aggiungila con «Aggiungi contratto» — non diventerà un contratto attivo."));
                MakeEmail(c, ctx, "docreq");
                break;
            }

            case "addDoc":
                AddDoc(c, ctx, p, v);
                break;
            case "fixEmail":
                c.Phase = CasePhases.Email;
                MakeEmail(c, ctx, "fix");
                break;
            case "markAnnex":
            {
                if (!CaseRules.CanWork(c, v))
                {
                    return Deny(c, NoWork);
                }

                var idx = (p.Version ?? 0) - 1;
                if (idx < 0 || idx >= c.Docs.Count)
                {
                    break;
                }

                foreach (var r in c.Docs[idx].Rows.Where(r => r.Res == "unread"))
                {
                    r.Res = "manual";
                    r.Draft = B("verified manually by {viewer}", "verificato manualmente da {viewer}", ("viewer", v));
                }

                Log(c, ctx, v, B("Annex verified manually", "Allegato verificato manualmente"));
                break;
            }

            case "signDeclared":
                c.Phase = CasePhases.SignDeclared;
                Say(c, ctx, B("Status: signature declared — document to acquire. The task stays open until you add the signed file.", "Stato: firma dichiarata — documento da acquisire. L’attività resta aperta finché non aggiungi il file firmato."));
                break;
            case "sigOk":
                if (!CaseRules.CanClose(c, v))
                {
                    return Deny(c, NoClose);
                }

                if (c.Signed is null)
                {
                    break;
                }

                c.Signed.ConfirmedBy = v;
                CancelOpenReminders(c);
                c.Phase = CasePhases.Pipeline;
                c.Pipe = ctx.Services.StartExtraction(c, ctx);
                Log(c, ctx, v, B("Signed document confirmed as final — negotiation completed", "Documento firmato confermato come definitivo — negoziazione completata"));
                Push(c, ctx, new CaseFeedItem { K = "pipe" });
                // Stages advance only while an extraction is really running.
                c.Tick = c.Pipe.Stage < 3;
                break;
            case "tick":
                if (c.Pipe is null || c.Pipe.Stage >= 3)
                {
                    break;
                }

                c.Pipe.Stage++;
                if (c.Pipe.Stage < 3)
                {
                    c.Tick = true;
                }

                break;
            case "acceptF":
                if (c.Pipe is not null && p.I is { } ai && ai >= 0 && ai < c.Pipe.Weak.Count)
                {
                    c.Pipe.Weak[ai].State = "accepted";
                }

                break;
            case "correctF":
                if (c.Pipe is not null && p.I is { } ci && ci >= 0 && ci < c.Pipe.Weak.Count)
                {
                    var nv = (c.Ui.Cf.TryGetValue("cf" + CaseFormat.Js(ci), out var cfv) ? cfv : string.Empty).Trim();
                    if (nv.Length > 0)
                    {
                        c.Pipe.Weak[ci].Val = nv;
                        c.Pipe.Weak[ci].State = "corrected";
                    }
                }

                break;
            case "validate":
                Validate(c, ctx, f);
                break;
            case "startNotice":
                StartNotice(c, ctx);
                break;
            case "nStart":
                if (c.Notice is null)
                {
                    break;
                }

                c.Notice.State = "email";
                MakeEmail(c, ctx, "notice");
                break;
            case "nReceipt":
                if (c.Notice is null)
                {
                    break;
                }

                c.Notice.Evidence = true;
                c.Notice.EvFile = "Notice_receipt_confirmation.pdf";
                UserSays(c, ctx, v, B("Added receipt: {file}", "Aggiunta ricevuta: {file}", ("file", c.Notice.EvFile)));
                break;
            case "nOut":
            {
                if (!CaseRules.CanClose(c, v))
                {
                    return Deny(c, NoClose);
                }

                if (c.Notice is null)
                {
                    break;
                }

                if (!c.Notice.Evidence && !c.Ui.NoEv)
                {
                    c.Ui.Warn = B("Attach evidence of receipt, or tick that you proceed without it.", "Allega la prova di ricezione, oppure spunta che procedi senza.");
                    break;
                }

                CancelOpenReminders(c);
                c.Notice.State = "out";
                c.Notice.By = v;
                c.Phase = c.Phase == CasePhases.Done ? CasePhases.Done : CasePhases.NoticeOut;
                if (c.Alt is null || c.Alt.State != "selected")
                {
                    c.Outcome = new CaseOutcome { Kind = "terminated", End = cn.End, When = ctx.Today };
                }
                else
                {
                    c.Alt.ExitDone = true;
                    if (c.Alt.NewDone)
                    {
                        c.Outcome = new CaseOutcome { Kind = "terminated", End = cn.End, When = ctx.Today, ReplacedBy = c.Alt.Chosen };
                    }
                }

                Log(c, ctx, v, B("Outcome recorded: not renewed", "Esito registrato: non rinnovato"));
                Push(c, ctx, new CaseFeedItem { K = "nout" });
                break;
            }

            case "startAlt":
                StartAlt(c, ctx);
                break;
            case "altReqs":
                if (c.Alt is null)
                {
                    break;
                }

                if (!c.Alt.Reqs.Any(r => r.On))
                {
                    c.Ui.Warn = B("Keep at least one feature.", "Tieni almeno una funzione.");
                    break;
                }

                c.Alt.Reqs = c.Alt.Reqs.Where(r => r.On).ToList();
                c.Alt.State = "cand";
                UserSays(c, ctx, v, B("Requirements confirmed", "Requisiti confermati"));
                Push(c, ctx, new CaseFeedItem { K = "acand" });
                break;
            case "altReqToggle":
                if (c.Alt is not null && p.I is { } ri && ri >= 0 && ri < c.Alt.Reqs.Count)
                {
                    c.Alt.Reqs[ri].On = !c.Alt.Reqs[ri].On;
                }

                break;
            case "altFeatAdd":
            {
                var ft = (c.Ui.AltFeat ?? string.Empty).Trim();
                if (ft.Length > 0 && c.Alt is not null)
                {
                    c.Alt.Reqs.Add(new CaseAltRequirement { Text = ft, On = true });
                    c.Ui.AltFeat = string.Empty;
                }

                break;
            }

            case "remPick":
                c.Ui.RemCustom = false;
                c.Ui.RemN = p.N;
                c.Ui.RemDate = CapReminder(ctx, ctx.Today.AddDays(p.N ?? 0));
                break;
            case "recapYes":
                if (!CaseRules.CanWork(c, v))
                {
                    return Deny(c, NoWork);
                }

                c.Ui.RecapDone = "yes";
                UserSays(c, ctx, v, B("Yes — prepare the email to {supplier}", "Sì — prepara l’email a {supplier}", ("supplier", cn.Supplier)));
                if (c.PlanOk)
                {
                    if (!c.Emails.Any(x => x.Kind == "first"))
                    {
                        c.Phase = CasePhases.Email;
                        MakeEmail(c, ctx, "first", new EmailOpts(To: string.Empty, ToGiven: true));
                    }
                }
                else if (c.PlanVer > 0)
                {
                    ConfirmPlanCore(c, ctx, v);
                    Say(c, ctx, B("Plan confirmed. Here is the email to {supplier} — add the recipient before approving.", "Piano confermato. Ecco l’email a {supplier} — aggiungi il destinatario prima di approvare.", ("supplier", cn.Supplier)));
                    MakeEmail(c, ctx, "first", new EmailOpts(To: string.Empty, ToGiven: true));
                }
                else
                {
                    Say(c, ctx, B("First finish the four questions above — then I draft the email to {supplier}.", "Prima finisci le quattro domande sopra — poi preparo l’email a {supplier}.", ("supplier", cn.Supplier)));
                }

                break;
            case "recapWait":
                c.Ui.RecapDone = "wait";
                UserSays(c, ctx, v, B("Not yet — wait for the offers first", "Non ancora — aspetto prima le offerte"));
                Say(c, ctx, B("Okay. The renewal with {supplier} stays on hold. Add the offers from the vendor threads when they arrive; your notice date is {cancel}.", "Va bene. Il rinnovo con {supplier} resta in attesa. Aggiungi le offerte dai filoni dei fornitori quando arrivano; la tua disdetta è entro il {cancel}.", ("supplier", cn.Supplier), ("cancel", D(cn.Cancel))));
                break;
            case "recapNo":
                c.Ui.RecapDone = "no";
                UserSays(c, ctx, v, B("No — I will only replace the supplier", "No — sostituisco solo il fornitore"));
                Say(c, ctx, B("Understood: no email to {supplier}. I continue with the replacement only.", "Capito: nessuna email a {supplier}. Continuo solo con la sostituzione.", ("supplier", cn.Supplier)));
                break;
            case "altAdd":
            {
                var nm = (c.Ui.AltName ?? string.Empty).Trim();
                if (nm.Length > 0 && c.Alt is not null && !c.Alt.Vendors.Contains(nm))
                {
                    c.Alt.Vendors.Add(nm);
                }

                c.Ui.AltName = string.Empty;
                break;
            }

            case "altEx":
                if (c.Alt is not null)
                {
                    foreach (var n in ctx.Services.ExampleVendors().Where(n => !c.Alt.Vendors.Contains(n)))
                    {
                        c.Alt.Vendors.Add(n);
                    }
                }

                break;
            case "altRfq":
                if (c.Alt is null)
                {
                    break;
                }

                c.Alt.State = "rfq";
                c.Thread = c.Alt.Vendors.FirstOrDefault();
                foreach (var vn in c.Alt.Vendors.ToList())
                {
                    if (!c.Alt.RfqFor.Contains(vn))
                    {
                        c.Alt.RfqFor.Add(vn);
                        MakeEmail(c, ctx, "rfq", new EmailOpts(To: string.Empty, ToGiven: true, Thread: vn, Vendor: vn));
                    }
                }

                break;
            case "thread":
                c.Thread = p.T;
                break;
            case "altOffer":
            {
                if (c.Alt is null || string.IsNullOrEmpty(p.Vendor))
                {
                    break;
                }

                c.Alt.Offers[p.Vendor] = ctx.Services.VendorOffer(c, ctx, p.Vendor, v);
                UserSays(c, ctx, v, B("Added offer from {vendor}", "Aggiunta offerta di {vendor}", ("vendor", p.Vendor)), p.Vendor);
                Push(c, ctx, new CaseFeedItem { K = "aoff", Vendor = p.Vendor, Thread = p.Vendor });
                if (c.Alt.Offers.Count >= 1 && !c.Alt.Cmp)
                {
                    c.Alt.Cmp = true;
                    c.Alt.State = "compare";
                    Push(c, ctx, new CaseFeedItem { K = "acmp" });
                }

                break;
            }

            case "altSel":
                if (!CaseRules.CanClose(c, v))
                {
                    return Deny(c, NoClose);
                }

                if (c.Alt is null)
                {
                    break;
                }

                c.Alt.State = "selected";
                c.Alt.Chosen = p.Vendor;
                Log(c, ctx, v, B("Alternative selected: {vendor} (current contract not closed)", "Alternativa scelta: {vendor} (contratto attuale non chiuso)", ("vendor", p.Vendor)));
                Push(c, ctx, new CaseFeedItem { K = "asel" });
                break;
            case "altNew":
                if (c.Alt is null)
                {
                    break;
                }

                c.Alt.NewDone = true;
                c.Alt.NewFile = p.Vendor + "_Contract_signed.pdf";
                UserSays(c, ctx, v, B("Added new vendor contract: {file}", "Aggiunto contratto del nuovo fornitore: {file}", ("file", c.Alt.NewFile)));
                c.PipeNew = true;
                if (c.Alt.ExitDone)
                {
                    c.Outcome = new CaseOutcome { Kind = "terminated", End = cn.End, When = ctx.Today, ReplacedBy = c.Alt.Chosen };
                }

                Push(c, ctx, new CaseFeedItem { K = "asel2" });
                break;
            case "delegate":
                if (v != c.Approver)
                {
                    return Deny(c, NoDelegations);
                }

                if (string.IsNullOrEmpty(p.Who))
                {
                    break;
                }

                if (p.On == true)
                {
                    c.Delegates[p.Who] = new CaseDelegate { Close = false };
                    Log(c, ctx, v, B("Delegate added: {who}", "Delega aggiunta: {who}", ("who", p.Who)));
                }
                else
                {
                    c.Delegates.Remove(p.Who);
                    Log(c, ctx, v, B("Delegation removed: {who}", "Delega rimossa: {who}", ("who", p.Who)));
                }

                break;
            case "delegateClose":
                if (v != c.Approver)
                {
                    return Deny(c, NoDelegations);
                }

                if (p.Who is not null && c.Delegates.TryGetValue(p.Who, out var dg))
                {
                    dg.Close = !dg.Close;
                }

                break;
            case "transfer":
                if (v != c.Approver)
                {
                    return Deny(c, B("Only the approver (CPO) can reassign.", "Solo l’approvatore (CPO) può riassegnare."));
                }

                if (string.IsNullOrEmpty(p.Who))
                {
                    break;
                }

                c.Owner = p.Who;
                c.History.Add(new CaseOwnerChange { Who = p.Who, At = ctx.Today });
                Log(c, ctx, v, B("Ownership reassigned to {who}", "Responsabilità riassegnata a {who}", ("who", p.Who)));
                break;
            case "say":
                return SayAction(c, ctx, p, v);
            case "ask":
            {
                // A free question from the case composer: the person's own message, answered by the
                // "answer" action that follows it. Never an interview answer and never a command —
                // those are "say" — so asking about the SLA during the interview cannot become a goal.
                if (p.Text is { } qt && qt.Render().Trim().Length > 0)
                {
                    UserSays(c, ctx, v, qt);
                }

                break;
            }

            case "note":
            {
                var nt = (p.Text?.Render() ?? string.Empty).Trim();
                if (nt.Length == 0)
                {
                    break;
                }

                Push(c, ctx, new CaseFeedItem { K = "user", By = v, Text = nt, Tag = B("Team note — not a question to Raffa", "Nota per il team — non è una domanda a Raffa") });
                break;
            }

            case "answer":
                if (p.Text is { } at && (at.IsMessage || !string.IsNullOrEmpty(at.Plain)))
                {
                    Say(c, ctx, at);
                }

                if (p.Resume is { } rs2 && (rs2.IsMessage || !string.IsNullOrEmpty(rs2.Plain)))
                {
                    Say(c, ctx, rs2);
                }

                break;
            case "collab":
                Collab(c, ctx, p, v);
                break;
            case "conflictRecover":
            {
                var cf = c.Ui.Conflict;
                if (cf is null)
                {
                    break;
                }

                c.Ui.Edit = cf.Eid;
                c.Ui.EditBody = cf.Yours ?? EmailById(c, cf.Eid)?.Body;
                c.Ui.Conflict = null;
                break;
            }

            case "conflictDismiss":
                c.Ui.Conflict = null;
                break;
        }

        return c;
    }

    private static bool LiabilityConflict(RenewalCase c) => CaseAgreement.LiabilityBelowPolicy(c);

    private static void SetUi(RenewalCase c, CaseContext ctx, CaseActionPayload p)
    {
        var k = p.K ?? string.Empty;
        var s = p.ValueString();
        if (k == "remNum")
        {
            var rn = Math.Max(1, Math.Min(90, LeadingInt(s) is { } n && n != 0 ? n : 1));
            c.Ui.RemNum = s;
            c.Ui.RemN = rn;
            c.Ui.RemDate = CapReminder(ctx, ctx.Today.AddDays(rn));
            return;
        }

        if (k.StartsWith("to:", StringComparison.Ordinal))
        {
            if (int.TryParse(k[3..], System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var id) && EmailById(c, id) is { } em)
            {
                em.To = s;
            }

            return;
        }

        switch (k)
        {
            case "other": c.Ui.Other = p.ValueInt(); break;
            case "otherText": c.Ui.OtherText = s; break;
            case "remCustom": c.Ui.RemCustom = p.ValueBool(); break;
            case "editBody": c.Ui.EditBody = s; break;
            case "sentDate": c.Ui.SentDate = s; break;
            case "sentEdited": c.Ui.SentEdited = p.ValueBool(); break;
            case "sentText": c.Ui.SentText = s; break;
            case "replyText": c.Ui.ReplyText = s; break;
            case "replyOpen": c.Ui.ReplyOpen = p.ValueBool(); break;
            case "auth": c.Ui.Auth = s; break;
            case "noEv": c.Ui.NoEv = p.ValueBool(); break;
            case "altFeat": c.Ui.AltFeat = s; break;
            case "altName": c.Ui.AltName = s; break;
            default:
                if (k.StartsWith("cf", StringComparison.Ordinal))
                {
                    c.Ui.Cf[k] = s;
                }
                else if (k.Length > 0)
                {
                    c.Ui.Extra[k] = s;
                }

                break;
        }
    }

    private static int? LeadingInt(string s)
    {
        var m = LeadingIntRe().Match(s);
        return m.Success && int.TryParse(m.Value, System.Globalization.NumberStyles.AllowLeadingSign, System.Globalization.CultureInfo.InvariantCulture, out var n) ? n : null;
    }

    private static void Reply(RenewalCase c, CaseContext ctx, CaseActionPayload p, string v, CasePriceFacts f)
    {
        var text = (p.Text?.Render() is { Length: > 0 } pt ? pt : c.Ui.ReplyText ?? string.Empty).Trim();
        if (text.Length == 0)
        {
            return;
        }

        c.Round++;
        var rid = c.Reps.Count;
        c.Reps.Add(new CaseReply { Rid = rid, Round = c.Round, Text = text, By = v, At = ctx.Today });
        c.Ui.ReplyOpen = false;
        c.Ui.ReplyText = string.Empty;
        CancelOpenReminders(c);
        foreach (var x in c.Emails.Where(x => x.State != EmailStates.Sent && x.State != EmailStates.Superseded && x.Kind != "notice" && x.Kind != "rfq"))
        {
            if (x.State is EmailStates.Approved or EmailStates.Copied)
            {
                Log(c, ctx, CaseRules.Raffa, B("Draft v{ver} superseded by a newer reply", "Bozza v{ver} superata da una nuova risposta", ("ver", x.Ver)));
            }

            x.State = EmailStates.Superseded;
        }

        Push(c, ctx, new CaseFeedItem { K = "reply", Rid = rid, By = v });
        var analysis = ctx.Services.AnalyzeReply(c, ctx, rid);
        foreach (var u in analysis.Terms)
        {
            SetTerm(c, u);
        }

        c.Reps[rid].Rows = analysis.Rows.ToList();
        if (analysis.Prices is { Count: > 0 } offered)
        {
            c.OfferedPrices ??= new Dictionary<string, double>(StringComparer.Ordinal);
            foreach (var (months, price) in offered)
            {
                c.OfferedPrices[months.ToString(System.Globalization.CultureInfo.InvariantCulture)] = price;
            }
        }
        Push(c, ctx, new CaseFeedItem { K = "ana", Rid = rid });
        c.Phase = CasePhases.Review;
        if (c.Round == 1 && !ctx.Services.Scripted)
        {
            // A trade-off is put to the owner only when the reply shows one (a lower price tied to 24
            // months); otherwise the counter-proposal starts from what the supplier actually offered.
            if (CaseAgreement.OffersTermTradeOff(c))
            {
                c.Qs.Add(new CaseQuestion { Did = c.Qs.Count, State = "open" });
                Push(c, ctx, new CaseFeedItem { K = "dec", Did = c.Qs.Count - 1 });
            }
            else
            {
                var term = CaseAgreement.OfferedTerm(c, analysis.Benchmark);
                c.Limits = new CaseLimits { Term = term ?? 0, PriceMax = term is { } t0 ? CaseAgreement.OfferedPrice(c, t0) : null };
                Say(c, ctx, B("The reply ties no discount to a longer term, so there is no trade-off to decide. I drafted the counter-proposal: what the supplier proposed, then what we ask.", "La risposta non lega sconti a una durata più lunga, quindi non c’è un compromesso da decidere. Ho preparato la controproposta: ciò che ha proposto il fornitore, poi ciò che chiediamo noi."));
                MakeEmail(c, ctx, "counter");
                c.Phase = CasePhases.Email;
            }
        }
        else if (c.Round == 1)
        {
            if (c.Goals.Term != "24")
            {
                c.Qs.Add(new CaseQuestion { Did = c.Qs.Count, State = "open" });
                Push(c, ctx, new CaseFeedItem { K = "dec", Did = c.Qs.Count - 1 });
            }
            else
            {
                c.Limits = new CaseLimits { Term = 24, PriceMax = CounterPrice(c, ctx, 24) };
                Say(c, ctx, B("Your plan already asks for 24 months, so no trade-off to decide. I drafted the counter-proposal.", "Il tuo piano chiede già 24 mesi, quindi nessun compromesso da decidere. Ho preparato la controproposta."));
                MakeEmail(c, ctx, "counter");
                c.Phase = CasePhases.Email;
            }
        }
        else
        {
            c.Qs.Add(new CaseQuestion { Did = c.Qs.Count, State = "agree" });
            Push(c, ctx, new CaseFeedItem { K = "agree", Did = c.Qs.Count - 1, Rid = rid });
            c.Phase = CasePhases.Agree;
        }
    }

    /// <summary>The unit price the supplier would settle on for a term: what it offered in a reply when
    /// that was read, else the script services' rule.</summary>
    public static double CounterPrice(RenewalCase c, CaseContext ctx, int termMonths) =>
        c.OfferedPrices is { } offered && offered.TryGetValue(termMonths.ToString(System.Globalization.CultureInfo.InvariantCulture), out var price) && price > 0
            ? price
            : ctx.Services.CounterPrice(ctx, termMonths);

    private static void AddDoc(RenewalCase c, CaseContext ctx, CaseActionPayload p, string v)
    {
        var sup = ctx.Contract.Supplier;
        if (c.Agreed is null)
        {
            Say(c, ctx, B("I received a document before terms were confirmed as agreed. I cannot verify it against an agreed position yet — confirm the terms first.", "Ho ricevuto un documento prima che le condizioni fossero confermate come concordate. Non posso ancora verificarlo su una posizione concordata — conferma prima le condizioni."));
            return;
        }

        if (c.Phase is CasePhases.SignWait or CasePhases.SignConfirm or CasePhases.SignDeclared)
        {
            c.Signed = new CaseSigned { File = p.File ?? sup + "_OrderForm_signed.pdf", By = v, At = ctx.Today };
            c.Phase = CasePhases.SignConfirm;
            UserSays(c, ctx, v, B("Added signed contract: {file}", "Aggiunto contratto firmato: {file}", ("file", c.Signed.File)));
            Push(c, ctx, new CaseFeedItem { K = "sig" });
            return;
        }

        CancelOpenReminders(c);
        var ver = c.Docs.Count + 1;
        var doc = new CaseDraftDoc
        {
            V = ver,
            File = p.File ?? sup + "_OrderForm_draft_v" + CaseFormat.Js(ver) + ".pdf",
            By = v,
            At = ctx.Today,
            Rows = ctx.Services.VerifyDraft(c, ctx, ver).ToList(),
        };
        c.Docs.Add(doc);
        UserSays(c, ctx, v, B("Added contract draft: {file}", "Aggiunta bozza di contratto: {file}", ("file", doc.File)));
        Push(c, ctx, new CaseFeedItem { K = "doc", V = ver });
        c.Phase = CasePhases.DocCheck;
        if (doc.Rows.All(r => r.Res == "ok"))
        {
            c.Phase = CasePhases.SignWait;
            Push(c, ctx, new CaseFeedItem { K = "signwait" });
        }
    }

    private static void Validate(RenewalCase c, CaseContext ctx, CasePriceFacts f)
    {
        // Once closed, the contract's facts already carry the new term: validating again would compound it.
        if (c.Outcome is not null || c.Pipe is null || c.Pipe.Stage < 3 || c.Pipe.Weak.Any(x => x.State == "open") || c.Agreed is null)
        {
            return;
        }

        var cn = ctx.Contract;
        c.Pipe.Stage = 4;
        c.Phase = CasePhases.Done;
        var a = c.Agreed;
        double newSpend;
        double oldSpend;
        var term = a.Term;
        var price = a.Price;
        if (ctx.Services.Scripted)
        {
            newSpend = cn.Spend - f.Qty * (f.P0 - a.Price) * f.Mult;
            oldSpend = cn.Spend;
        }
        else
        {
            // The values the owner checked against the signed document (accepted or corrected) become the
            // contract's new terms; the yearly value is the recurring lines at those values, never a delta
            // on a header figure that may include one-off amounts.
            double? Checked(string key, Func<string, double?> read) =>
                c.Pipe.Weak.FirstOrDefault(w => w.N == key) is { } w ? read(w.Val) : null;
            price = Checked(CaseSignedFields.Price, CaseAgreement.Number) ?? a.Price;
            term = (int?)Checked(CaseSignedFields.Term, x => CaseAgreement.Months(x)) ?? a.Term;
            var qty = Checked(CaseSignedFields.Qty, CaseAgreement.Number) ?? a.Qty ?? f.Qty;
            var computed = CaseAgreement.NewAnnual(ctx, f, price, qty);
            var annual = c.Pipe.Weak.FirstOrDefault(w => w.N == CaseSignedFields.Annual);
            newSpend = annual is { State: "corrected" } && CaseAgreement.Number(annual.Val) is { } typed ? typed : computed;
            oldSpend = CaseAgreement.CurrentRecurringAnnual(ctx);
        }

        var start = cn.End.AddDays(1);
        var end = CaseFormat.AddMonths(cn.End, term);
        var nc = end.AddDays(-cn.Notice);
        c.Outcome = new CaseOutcome
        {
            Kind = "renewed",
            Start = start,
            End = end,
            Cancel = nc,
            CancelDays = DaysTo(ctx, nc),
            Spend = newSpend,
            Delta = oldSpend - newSpend,
            Price = B("{price} (was {old})", "{price} (era {old})", ("price", CaseRules.Ps(f, price)), ("old", CaseFormat.Js(f.P0))),
            File = c.Signed?.File,
            PrevStart = cn.Start,
            PrevEnd = cn.End,
        };
        Push(c, ctx, new CaseFeedItem { K = "out" });
    }

    private static RenewalCase SayAction(RenewalCase c, CaseContext ctx, CaseActionPayload p, string v)
    {
        var text = (p.Text?.Render() ?? string.Empty).Trim();
        if (text.Length == 0)
        {
            return c;
        }

        if (c.Phase == CasePhases.Interview && c.Q < 3 && c.Feed.Any(x => x.K == "q" && x.I == c.Q))
        {
            c.Ui.Other = null;
            c.Ui.OtherText = string.Empty;
            SetGoal(c.Goals, c.Q, "custom", text);
            UserSays(c, ctx, v, text);
            NextQuestion(c, ctx);
            return c;
        }

        if (c.Phase == CasePhases.Interview && c.Q == 3)
        {
            c.Ui.Other = null;
            c.Ui.OtherText = string.Empty;
            c.Goals.ClausesText = text;
            UserSays(c, ctx, v, text);
            return c;
        }

        var cur = c.Emails.LastOrDefault(x => x.State != EmailStates.Sent && x.State != EmailStates.Superseded);
        if (cur is not null && ApproveCmd().IsMatch(text))
        {
            return ActOn(c, "approve", new CaseActionPayload { Eid = cur.Id }, ctx);
        }

        if (cur is not null && CopyCmd().IsMatch(text))
        {
            return ActOn(c, "copy", new CaseActionPayload { Eid = cur.Id }, ctx);
        }

        if (cur is not null && SentCmd().IsMatch(text))
        {
            c.Ui.SentOpen = cur.Id;
            c.Ui.SentDate = D(ctx.Today) + " · 14:40";
            return c;
        }

        if (c.Phase == CasePhases.Plan && ConfirmPlanCmd().IsMatch(text))
        {
            return ActOn(c, "confirmPlan", CaseActionPayload.None, ctx);
        }

        UserSays(c, ctx, v, text);
        return c;
    }

    private static void Collab(RenewalCase c, CaseContext ctx, CaseActionPayload p, string v)
    {
        var e = EmailById(c, p.Eid);
        if (e is null || e.State is EmailStates.Sent or EmailStates.Superseded)
        {
            return;
        }

        var oth = v == Marta ? Luca : Marta;
        var prevSt = e.State;
        var idx = e.Body.IndexOf("\n\nKind regards", StringComparison.Ordinal);
        if (idx >= 0)
        {
            e.Body = e.Body[..idx] + "\n\nPlease also confirm the SLA schedule in writing." + e.Body[idx..];
        }

        e.Ver++;
        if (prevSt != EmailStates.Draft)
        {
            Log(c, ctx, oth, B("Email edited by {who} — approval of v{ver} invalidated", "Email modificata da {who} — approvazione della v{ver} invalidata", ("who", oth), ("ver", e.Ver - 1)));
        }

        e.State = EmailStates.Draft;
        e.ApprovedBy = null;
        e.ApprovedVer = null;
        c.Ui.Conflict = new CaseConflict { Eid = e.Id, By = oth, At = "14:42", Ver = e.Ver, PrevState = prevSt, Yours = c.Ui.Edit == e.Id ? c.Ui.EditBody : null };
        if (c.Ui.Edit == e.Id)
        {
            c.Ui.Edit = null;
        }

        Say(c, ctx, B("Updated the draft to v{ver}: added the line asking for the SLA schedule in writing.", "Ho aggiornato la bozza alla v{ver}: aggiunta la riga che chiede lo SLA per iscritto.", ("ver", e.Ver)), oth);
    }

    /* ---------- stage ---------- */

    /// <summary>The phase index 0–5 (Start · Plan · Negotiation · Agreement · Contract &amp; signature · Acquisition).</summary>
    public static int Stage(RenewalCase c)
    {
        var p = c.Phase;
        if (p == CasePhases.Interview)
        {
            return 0;
        }

        if (p == CasePhases.Plan)
        {
            return 1;
        }

        if (p is CasePhases.Email or CasePhases.WaitingRem or CasePhases.Waiting or CasePhases.WaitingDue or CasePhases.Review)
        {
            return c.Agreed is not null ? 4 : 2;
        }

        if (p == CasePhases.Agree)
        {
            return 3;
        }

        if (p is CasePhases.DocWait or CasePhases.DocCheck || p.StartsWith("sign", StringComparison.Ordinal))
        {
            return 4;
        }

        return 5;
    }

    [GeneratedRegex(@"^(\w+)\s*(\/.*)?$")]
    private static partial Regex SizeRe();

    [GeneratedRegex("^[aeiou]", RegexOptions.IgnoreCase)]
    private static partial Regex VowelStart();

    [GeneratedRegex(@"^\s*[-+]?\d+")]
    private static partial Regex LeadingIntRe();

    [GeneratedRegex("^(approva|approve)", RegexOptions.IgnoreCase)]
    private static partial Regex ApproveCmd();

    [GeneratedRegex("^(copia|copy)", RegexOptions.IgnoreCase)]
    private static partial Regex CopyCmd();

    [GeneratedRegex("^(ho inviato|i sent|sent)", RegexOptions.IgnoreCase)]
    private static partial Regex SentCmd();

    [GeneratedRegex("^(conferma piano|confirm plan)", RegexOptions.IgnoreCase)]
    private static partial Regex ConfirmPlanCmd();
}
