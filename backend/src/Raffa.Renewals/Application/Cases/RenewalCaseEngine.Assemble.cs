using Raffa.SharedKernel.Text;
using static Raffa.Renewals.Application.Cases.Tx;

namespace Raffa.Renewals.Application.Cases;

public static partial class RenewalCaseEngine
{
    private static CaseView Assemble(RenewalCase c, CaseContext ctx, List<CaseCard> cards, CasePriceFacts f, string rl, bool wk, bool ap, bool cc)
    {
        var v = ctx.Viewer;
        var cn = ctx.Contract;
        var ph = Ph(c.Phase);
        CaseText stat = ph.Status;
        if (c.Outcome is { Kind: "terminated" })
        {
            stat = c.Alt?.Chosen is { } chosen
                ? B("Not renewed · replaced by {vendor}", "Non rinnovato · sostituito da {vendor}", ("vendor", chosen))
                : B("Not renewed", "Non rinnovato");
        }

        var rem = c.Rems.FirstOrDefault(x => x.State is "set" or "due");
        var curE = c.Emails.LastOrDefault(e => e.State != EmailStates.Sent && e.State != EmailStates.Superseded && e.Kind != "notice" && e.Kind != "rfq");
        var apw2 = Apw(c);
        var next = ph.Next;
        var who = ph.Who;
        if (c.Phase == CasePhases.Email && curE is not null)
        {
            if (curE.State == EmailStates.Draft && string.IsNullOrEmpty(curE.To))
            {
                (next, who) = (B("Add the recipient", "Aggiungi il destinatario"), "owner");
            }
            else if (curE.State == EmailStates.Draft)
            {
                (next, who) = (B("Send the email for approval", "Invia l’email in approvazione"), "owner");
            }
            else if (curE.State == EmailStates.ToApprove)
            {
                (next, who) = (B("Approve this version", "Approva questa versione"), "approver");
            }
            else
            {
                (next, who) = (B("Copy, send from your mailbox, confirm", "Copia, invia dalla tua casella, conferma"), "owner");
            }
        }

        CaseText? nextW = who switch
        {
            "approver" => apw2,
            "supplier" => B("Supplier", "Fornitore"),
            "you" => B("You", "Tu"),
            "owner" => c.Owner,
            _ => null,
        };
        var nextTxt = rem is not null && c.Phase == CasePhases.Waiting
            ? B("{next} · check {date}", "{next} · controllo {date}", ("next", next), ("date", D(rem.Date)))
            : next;
        var hdrNext = nextW is null ? nextTxt : N("{who} — {next}", ("who", nextW), ("next", nextTxt));

        var tracks = new List<CaseText>();
        if (c.Notice is { } nt)
        {
            CaseText ns = nt.State switch
            {
                "check" => B("to prepare", "da preparare"),
                "email" => B("email in progress", "email in corso"),
                "sent" => B("communicated — outcome to verify", "comunicata — esito da verificare"),
                _ => B("not renewed", "non rinnovato"),
            };
            tracks.Add(B("Notice: {state}", "Disdetta: {state}", ("state", ns)));
        }

        if (c.Alt is { } alt0)
        {
            CaseText asx = alt0.State switch
            {
                "req" => B("requirements", "requisiti"),
                "cand" => B("candidates", "candidati"),
                "rfq" => B("RFQ sent", "RFQ"),
                "compare" => B("comparing", "confronto"),
                _ => B("selected {vendor}", "scelto {vendor}", ("vendor", alt0.Chosen)),
            };
            tracks.Add(B("Alternatives: {state}", "Alternative: {state}", ("state", asx)));
        }

        var renewed = c.Outcome is { Kind: "renewed" } ? c.Outcome : null;
        CaseText RoleLabel(string m)
        {
            var r = CaseRules.Role(c, m);
            return r switch
            {
                "approver" => B("Approver · CPO", "Approvatore · CPO"),
                "owner" => B("Owner · works the case", "Responsabile · lavora il caso"),
                "delegate" => c.Delegates[m].Close ? B("Delegate · can close", "Delegato · può chiudere") : B("Delegate", "Delegato"),
                _ => B("Collaborator", "Collaboratore"),
            };
        }

        var hdr = new CaseHeader
        {
            // A renewed case keeps naming the contract it renewed: once closed, the facts carry the new term.
            Title = B("Renewal · {supplier} · {contract} {from}–{to}", "Rinnovo · {supplier} · {contract} {from}–{to}",
                ("supplier", cn.Supplier), ("contract", ShortName(cn.Name)),
                ("from", CaseFormat.Js((renewed?.PrevStart ?? cn.Start).Year)), ("to", CaseFormat.Js((renewed?.PreviousEnd(cn.End) ?? cn.End).Year))),
            Supplier = cn.Supplier,
            Contract = cn.Name,
            Status = stat,
            Next = hdrNext,
            Approver = c.Approver,
            NoticeBy = D(renewed?.Cancel ?? cn.Cancel),
            NoticeDays = renewed?.CancelDays ?? DaysTo(ctx, cn.Cancel),
            End = renewed is not null
                ? B("{end} (new contract; previous ends {prev})", "{end} (nuovo contratto; il precedente termina il {prev})", ("end", D(renewed.End)), ("prev", D(renewed.PreviousEnd(cn.End))))
                : D(cn.End),
            Owner = c.Owner,
            Tracks = tracks,
            Warn = c.Ui.Warn,
            People = c.Members.Select(m =>
            {
                var r = CaseRules.Role(c, m);
                return new CasePerson(m, RoleLabel(m), r is "owner" or "approver", r == "delegate", r == "delegate" && c.Delegates[m].Close, m == v);
            }).ToList(),
            YouRole = rl switch
            {
                "approver" => B("Approver", "Approvatore"),
                "owner" => B("Owner", "Responsabile"),
                "delegate" => B("Delegate", "Delegato"),
                _ => B("Collaborator", "Collaboratore"),
            },
        };

        var termsView = c.Terms.Select(kv =>
        {
            var t = kv.Value;
            var tone = t.State == "agreed" ? Strong : t.State is "rejected" or "unclear" ? Alert : Muted;
            return new CaseTermView(t.Label, t.Val ?? "—", TermStateLabel(t.State), t.State, tone, t.State == "agreed" ? 700 : 400);
        }).ToList();
        var dash = CaseText.Of("—");
        var planV = c.PlanVer != 0
            ? (c.PlanOk ? B("v{ver} · confirmed", "v{ver} · confermato", ("ver", c.PlanVer)) : B("v{ver} · to confirm", "v{ver} · da confermare", ("ver", c.PlanVer)))
            : dash;
        var summary = new CaseSummary(
            [
                new CaseRow(B("Products", "Prodotti"), c.Goals.Prod is not null ? AnsLabel(c, ctx, 0) : dash),
                new CaseRow(B("Term", "Durata"), c.Goals.Term is not null ? AnsLabel(c, ctx, 1) : dash),
                new CaseRow(B("Target", "Obiettivo"), c.Goals.Target is not null ? AnsLabel(c, ctx, 2) : dash),
                new CaseRow(B("Clauses", "Clausole"), c.Goals.Clauses is not null ? AnsLabel(c, ctx, 3) : dash),
                new CaseRow(B("Plan", "Piano"), planV),
            ],
            termsView,
            c.Decs.AsEnumerable().Reverse().Select(d => new CaseDecisionView(d.By, D(d.At), d.Text)).ToList(),
            B("{emails} emails · {replies} replies · {drafts} drafts{signed}", "{emails} email · {replies} risposte · {drafts} bozze{signed}",
                ("emails", c.Emails.Count), ("replies", c.Reps.Count), ("drafts", c.Docs.Count),
                ("signed", c.Signed is not null ? B(" · 1 signed", " · 1 firmato") : CaseText.Empty)));

        var closed = c.Outcome is not null || c.Phase == CasePhases.Pipeline;
        var actions = new List<CaseLink>();
        if ((!closed && c.Emails.Any(e => e.State == EmailStates.Sent)) || c.Round > 0)
        {
            actions.Add(new CaseLink(B("Paste reply", "Incolla risposta"), "openReply", CaseActionPayload.None));
        }

        if (!closed && c.Agreed is not null)
        {
            actions.Add(new CaseLink(B("Add contract", "Aggiungi contratto"), "addDoc", CaseActionPayload.None));
        }

        if (!c.PlanOk && !c.Goals.Policy && c.Phase != CasePhases.Interview)
        {
            actions.Add(new CaseLink(B("Add policy", "Aggiungi policy"), "policy", CaseActionPayload.None));
        }

        var threads = c.Alt is { } a1 && a1.Vendors.Count > 0 && a1.State is "rfq" or "compare" or "selected"
            ? a1.Vendors.Select(n => new CaseThread(n, c.Thread == n, !a1.Offers.ContainsKey(n) && a1.State != "selected", a1.Offers.ContainsKey(n))).ToList()
            : [];

        var demo = new List<CaseLink>();
        if (!closed && c.Rems.Any(r => r.State == "set"))
        {
            demo.Add(new CaseLink(B("Jump to the check date", "Salta alla data di controllo"), "ff", CaseActionPayload.None));
        }

        var ce = c.Emails.LastOrDefault(e => e.State != EmailStates.Sent && e.State != EmailStates.Superseded);
        if (!closed && ce is not null && c.Ui.Conflict is null && c.Ui.Edit is null)
        {
            demo.Add(new CaseLink(B("Simulate a colleague editing this draft", "Simula una modifica del collega alla bozza"), "collab", new CaseActionPayload { Eid = ce.Id }));
        }

        var altView = c.Alt is not null ? AltView(c, ctx) : null;
        var st = Stage(c);
        var steps7 = Stg.Select((x, i) => new CaseStepItem(x, i + 1, i < st, i == st)).ToList();

        CaseSinceView? since = null;
        if (c.Ui.Since is { } sn)
        {
            var by = new OrderedDictionary<string, List<CaseText>>(StringComparer.Ordinal);
            foreach (var x in sn.Items)
            {
                var label = SinceLabel(x.K) ?? B("updated the case", "ha aggiornato il caso");
                if (!by.TryGetValue(x.By, out var list))
                {
                    list = [];
                    by[x.By] = list;
                }

                if (!list.Contains(label))
                {
                    list.Add(label);
                }
            }

            since = new CaseSinceView(sn.N, by.Select(kv => N("{who}: {what}", ("who", kv.Key), ("what", CaseText.Join(", ", kv.Value)))).ToList());
        }

        var timeline = new List<CaseTimelineEntry>();
        foreach (var it in c.Feed)
        {
            if (it.K is "q" or "outlook")
            {
                continue;
            }

            CaseText lbl;
            CaseText txt;
            if (it.K == "say")
            {
                lbl = "Raffa.ai";
                txt = FirstSentence(it.Text ?? CaseText.Empty);
            }
            else if (it.K == "user")
            {
                lbl = it.By;
                var t = it.Text ?? CaseText.Empty;
                txt = !t.IsMessage && (t.Plain ?? string.Empty).Length > 90 ? t.Plain![..88] + "…" : t;
            }
            else if (it.K == "email")
            {
                var e = c.Emails[it.Eid ?? 0];
                lbl = B("Email", "Email");
                txt = N("{kind} v{ver} — {state}", ("kind", Kx(e.Kind)), ("ver", e.Ver), ("state", EmailStateLower(e.State)));
            }
            else
            {
                lbl = TimelineLabel(it.K) ?? it.K;
                txt = SinceLabel(it.K) ?? CaseText.Empty;
            }

            timeline.Add(new CaseTimelineEntry(it.At[..Math.Min(6, it.At.Length)], it.At.Length >= 5 ? it.At[^5..] : it.At, it.By, lbl, txt, it.By == v));
        }

        timeline.Reverse();

        var upcoming = new List<CaseUpcoming>();
        if (c.Outcome is null)
        {
            var go = cn.Cancel.AddDays(-30);
            foreach (var (date, label) in new[]
                     {
                         (go, B("Go / no-go call: renew, renegotiate or exit", "Decisione: rinnovare, rinegoziare o uscire")),
                         (cn.Cancel, B("Notice deadline", "Scadenza di disdetta")),
                         (cn.End, B("Term ends", "Fine termine")),
                     })
            {
                var days = DaysTo(ctx, date);
                if (days >= 0)
                {
                    upcoming.Add(new CaseUpcoming(D(date), label, B("{n} days", "{n} giorni", ("n", days))));
                }
            }
        }

        var emailsList = c.Emails.Select(e => new CaseRefItem(
            N("{kind} · v{ver}", ("kind", Kx(e.Kind)), ("ver", e.Ver)),
            EmailStateShort(e.State),
            e.State is EmailStates.Draft or EmailStates.ToApprove or EmailStates.Approved or EmailStates.Copied ? Alert : Muted)).ToList();
        var docsList = c.Docs.Select(d =>
        {
            var diffs = d.Rows.Count(x => x.Res is "diff" or "unread");
            return new CaseRefItem(d.File, diffs > 0 ? B("{n} open points", "{n} punti aperti", ("n", diffs)) : B("matches", "coincide"), diffs > 0 ? Alert : Muted);
        }).ToList();
        if (c.Signed is not null)
        {
            docsList.Add(new CaseRefItem(c.Signed.File, c.Signed.ConfirmedBy is not null ? B("final · confirmed", "definitivo · confermato") : B("to confirm", "da confermare"), Strong));
        }

        var replyFrom = c.Emails.LastOrDefault(e => !string.IsNullOrEmpty(e.To))?.To ?? "—";
        var stepsX = altView?.Steps7 ?? steps7;
        var page = ComposePage(c, ctx, cards, hdr, stepsX, actions, demo);

        return new CaseView
        {
            Hdr = hdr,
            Summary = summary,
            Cards = cards,
            Page = page,
            Outlook = OutlookTable(ctx),
            Timeline = timeline,
            Upcoming = upcoming,
            Stage = st,
            Steps7 = steps7,
            StepsX = stepsX,
            Alt = altView,
            TermsCmp = TermsCompare(c, ctx),
            OffersCmp = OffersTable(c, ctx),
            Check = altView?.Check ?? Checklist(c, ctx),
            Example = altView?.Example ?? Ex[st],
            Since = since,
            EmailsList = emailsList,
            DocsList = docsList,
            Actions = actions,
            Threads = threads,
            Demo = demo,
            Reply = new CaseReplyBox(c.Ui.ReplyOpen, c.Ui.ReplyText ?? string.Empty, replyFrom),
            ShortStatus = ShortStatus(c),
            Permissions = new CasePermissions(wk, ap, cc, rl),
        };
    }

    /// <summary><c>C.name.split(' — ')[0]</c>.</summary>
    private static string ShortName(string name)
    {
        var i = name.IndexOf(" — ", StringComparison.Ordinal);
        return i >= 0 ? name[..i] : name;
    }

    /// <summary>
    /// The timeline shows only a Raffa line's first sentence. A message is split in English here; the
    /// client shows the whole translated message (it cannot be split before translation).
    /// </summary>
    private static CaseText FirstSentence(CaseText t)
    {
        if (t.IsMessage)
        {
            return t;
        }

        var s = t.Plain ?? string.Empty;
        var parts = SentenceEnd().Split(s, 2);
        return parts[0];
    }

    private static CaseText? SinceLabel(string k) => k switch
    {
        "email" => B("prepared an email", "ha preparato un’email"),
        "user" => B("wrote in the chat", "ha scritto nella chat"),
        "reply" => B("pasted a supplier reply", "ha incollato una risposta del fornitore"),
        "ana" => B("analysed the reply", "ha analizzato la risposta"),
        "dec" => B("asked for a decision", "ha chiesto una decisione"),
        "agree" => B("proposed terms to confirm", "ha proposto condizioni da confermare"),
        "doc" => B("checked a draft", "ha verificato una bozza"),
        "sig" => B("received the signed file", "ha ricevuto il file firmato"),
        "plan" => B("prepared the plan", "ha preparato il piano"),
        "rem" => B("proposed a check date", "ha proposto una data di controllo"),
        "say" => B("posted an update", "ha pubblicato un aggiornamento"),
        "policy" => B("added a policy", "ha aggiunto una policy"),
        _ => null,
    };

    private static CaseText? TimelineLabel(string k) => k switch
    {
        "reply" => B("Supplier reply", "Risposta del fornitore"),
        "ana" => B("Analysis", "Analisi"),
        "dec" => B("Decision asked", "Decisione richiesta"),
        "agree" => B("Terms to confirm", "Condizioni da confermare"),
        "doc" => B("Draft checked", "Bozza verificata"),
        "sig" => B("Signed file", "File firmato"),
        "plan" => B("Plan", "Piano"),
        "rem" => B("Check date", "Data di controllo"),
        "out" => B("Outcome", "Esito"),
        "nrec" => B("Notice", "Disdetta"),
        "nout" => B("Outcome", "Esito"),
        "pipe" => B("Acquisition", "Acquisizione"),
        "policy" => B("Policy", "Policy"),
        "ncheck" => B("Notice check", "Verifica disdetta"),
        "areq" => B("Alternatives", "Alternative"),
        "acand" => B("Alternatives", "Alternative"),
        "aoff" => B("Offer", "Offerta"),
        "acmp" => B("Comparison", "Confronto"),
        "asel" => B("Replacement", "Sostituzione"),
        "signwait" => B("Signature", "Firma"),
        "ov" => B("Case opened", "Caso aperto"),
        "info" => B("Guide", "Guida"),
        "recap" => B("Back to the plan", "Torna al piano"),
        _ => null,
    };

    /* ---------- the case screen (AskRaffa V4.dc.html, `scr.caso`) ---------- */

    private static CasePage ComposePage(RenewalCase c, CaseContext ctx, List<CaseCard> all, CaseHeader hdr, IReadOnlyList<CaseStepItem> stepsX, List<CaseLink> actions, List<CaseLink> demo)
    {
        var cards = all.Where(x => x.K != "outlook").ToList();
        var curI = cards.FindLastIndex(x => x.Current);
        CaseCard cur;
        if (curI >= 0)
        {
            cur = cards[curI];
        }
        else
        {
            cur = cards.LastOrDefault(x => !x.Plain) ?? cards[^1];
            curI = cards.IndexOf(cur);
        }

        var done = cards.Take(curI).Where(x => !x.Plain).Select((x, i) => new CaseDoneStep(
            x.Kicker,
            x.Title,
            x.Email is not null ? x.Email.State : i == 0 ? CaseText.Empty : x.Lines.FirstOrDefault() ?? CaseText.Empty,
            x.Lines,
            x.Rows,
            x.Steps,
            x.Chips)).ToList();
        var msgs = cards.Skip(curI + 1).Where(x => x.Plain).TakeLast(4)
            .Select(x => new CaseBubble(x.By == CaseRules.Raffa ? CaseRules.Raffa : x.By.Split(' ')[0], x.By == CaseRules.Raffa, x.Text)).ToList();

        var buttons = cur.Buttons.ToList();
        var ph0 = c.Phase;
        var bx = buttons.FindIndex(b => b.Act == "toEx");
        var br = buttons.FindIndex(b => b.Act == "requestAppr");
        if (bx >= 0 && br >= 0 && buttons[br].Off)
        {
            buttons[bx] = buttons[bx] with { Primary = true };
            buttons[br] = buttons[br] with { Primary = false };
        }

        bool Has(string act) => buttons.Any(b => b.Act == act);
        if (ph0 == CasePhases.Waiting && demo.Any(d => d.Act == "ff"))
        {
            buttons.Add(new CaseButton(B("Jump to the check date (demo)", "Salta alla data di controllo (demo)"), "ff", CaseActionPayload.None, true));
        }

        if (c.Agreed is not null && ph0 is CasePhases.Waiting or CasePhases.WaitingDue or CasePhases.DocWait && !Has("addDoc"))
        {
            if (ph0 != CasePhases.Waiting)
            {
                buttons = buttons.Select(b => b with { Primary = false }).ToList();
            }

            buttons.Add(new CaseButton(B("Add the contract received (demo)", "Aggiungi il contratto ricevuto (demo)"), "addDoc", CaseActionPayload.None, ph0 != CasePhases.Waiting));
        }

        if (ph0 == CasePhases.Waiting && c.Agreed is null && !Has("openReply"))
        {
            buttons.Add(new CaseButton(B("Paste the supplier reply", "Incolla la risposta del fornitore"), "openReply", CaseActionPayload.None));
        }

        var ordered = buttons.Where(b => b.Primary).Concat(buttons.Where(b => !b.Primary)).ToList();
        var visible = ordered.Take(3).ToList();
        var more = ordered.Skip(3).ToList();
        more.AddRange(actions.Select(a => new CaseButton(N("+ {label}", ("label", a.Label)), a.Act, a.P)));
        if (c.Notice is null)
        {
            more.Add(new CaseButton(B("Prepare the non-renewal notice", "Prepara la disdetta"), "startNotice", CaseActionPayload.None));
        }

        if (c.Alt is null)
        {
            more.Add(new CaseButton(B("Evaluate alternatives", "Valuta alternative"), "startAlt", CaseActionPayload.None));
        }

        more.AddRange(demo.Select(d => new CaseButton(B("Demo: {label}", "Demo: {label}", ("label", d.Label)), d.Act, d.P)));

        var current = new CaseCurrentCard
        {
            K = cur.K,
            Kicker = cur.Kicker.IsMessage || !string.IsNullOrEmpty(cur.Kicker.Plain) ? cur.Kicker : cur.By,
            Title = cur.Title.IsMessage || !string.IsNullOrEmpty(cur.Title.Plain) ? cur.Title : cur.Text,
            Banner = cur.Banner,
            Lines = cur.Lines,
            Rows = cur.Rows,
            Table = cur.Table,
            Steps = cur.Steps,
            Email = cur.Email,
            Pre = cur.Pre,
            Fields = cur.Fields,
            Chips = cur.Chips,
            Inputs = cur.Inputs,
            Buttons = visible,
            More = more,
            Note = cur.Note,
            Bullets = cur.Bullets,
            Stats = cur.Stats,
        };

        var phNow = stepsX.FirstOrDefault(x => x.Now);
        var warnText = hdr.Warn?.Id ?? string.Empty;
        var aboutClosing = warnText.Contains("approv", StringComparison.OrdinalIgnoreCase) || warnText.Contains("close", StringComparison.OrdinalIgnoreCase) || warnText.Contains("confirm", StringComparison.OrdinalIgnoreCase);
        return new CasePage
        {
            Title = B("Renewal · {supplier} · {contract}", "Rinnovo · {supplier} · {contract}", ("supplier", hdr.Supplier), ("contract", ShortName(hdr.Contract))),
            Phase = phNow is not null ? B("Phase {n}/{total} · {label}", "Fase {n}/{total} · {label}", ("n", phNow.N), ("total", stepsX.Count), ("label", phNow.Label)) : hdr.Status,
            Next = hdr.Next,
            Notice = B("{date} · {n} d", "{date} · {n} g", ("date", hdr.NoticeBy), ("n", hdr.NoticeDays)),
            NoticeUrgent = hdr.NoticeDays <= 30,
            End = hdr.End,
            Warn = hdr.Warn,
            WhoCan = aboutClosing
                ? B("Who can proceed: {who}.", "Può procedere: {who}.", ("who", Apw(c)))
                : B("Who can proceed: {who} (owner) or a delegate.", "Può procedere: {who} (responsabile) o un delegato.", ("who", c.Owner)),
            Done = done,
            Msgs = msgs,
            Current = current,
        };
    }

    /* ---------- checklist ---------- */

    private static List<CaseChecklistItem> Checklist(RenewalCase c, CaseContext ctx)
    {
        var st = Stage(c);
        var em = c.Emails;
        var list = new List<CaseChecklistItem>();
        void It(bool done, CaseText label, string who = "") => list.Add(new CaseChecklistItem(done, label, who));
        switch (st)
        {
            case 0:
                It(!string.IsNullOrEmpty(c.Owner), B("Owner assigned", "Responsabile assegnato"), c.Approver);
                It(c.Goals.Prod is not null, B("Products and quantities", "Prodotti e quantità"), c.Owner);
                It(c.Goals.Term is not null, B("Term", "Durata"), c.Owner);
                It(c.Goals.Target is not null, B("Economic goal (or “no target”)", "Obiettivo economico (o «nessun target»)"), c.Owner);
                It(c.PlanVer > 0, B("Clauses to change", "Clausole da cambiare"), c.Owner);
                break;
            case 1:
                It(c.PlanVer > 0, B("Plan drafted by Raffa", "Piano preparato da Raffa"));
                It(c.PlanOk, B("Plan confirmed", "Piano confermato"), c.Owner);
                break;
            case 2:
            {
                var cur = em.LastOrDefault(e => e.State != EmailStates.Superseded && e.Kind != "notice" && e.Kind != "rfq");
                var cs = cur?.State;
                if (cs == EmailStates.Draft && string.IsNullOrEmpty(cur!.To))
                {
                    It(false, B("Recipient added", "Destinatario aggiunto"), c.Owner);
                }

                It(cs is not null && cs != EmailStates.Draft, B("Sent for approval", "Inviata in approvazione"), c.Owner);
                It(cs is EmailStates.Approved or EmailStates.Copied or EmailStates.Sent, B("Email approved by {who}", "Email approvata da {who}", ("who", c.Approver)), c.Approver);
                It(cs == EmailStates.Sent, B("Send confirmed", "Invio confermato"), c.Owner);
                It(c.Rems.Any(x => x.State != "proposed") && !c.Rems.Any(x => x.State == "proposed"), B("Check date set", "Data di controllo impostata"), c.Owner);
                It(c.Reps.Count >= (cs == EmailStates.Sent ? c.Round + 1 : c.Round), B("Supplier reply registered", "Risposta del fornitore registrata"), c.Owner);
                if (c.Qs.Any(q => q.State == "open" || q.Opt is not null))
                {
                    It(c.Qs.All(q => q.State != "open"), B("Trade-off decided", "Compromesso deciso"), c.Owner);
                }

                break;
            }

            case 3:
                if (LiabilityConflict(c))
                {
                    It(!string.IsNullOrWhiteSpace(c.Ui.Auth), B("Policy exception authorized", "Deroga alla policy autorizzata"), c.Approver);
                }

                It(c.Agreed is not null, B("Terms confirmed as agreed", "Condizioni confermate come concordate"), c.Owner);
                break;
            case 4:
            {
                var dr = em.FirstOrDefault(e => e.Kind == "docreq");
                var ld = c.Docs.LastOrDefault();
                It(dr is { State: EmailStates.Sent }, B("Contract requested from the supplier", "Contratto richiesto al fornitore"), c.Owner);
                It(c.Docs.Count > 0, B("Draft received", "Bozza ricevuta"), c.Owner);
                It(ld is not null && !ld.Rows.Any(x => x.Res == "diff"), B("No differences with the agreement", "Nessuna differenza con l’accordo"), c.Owner);
                It(ld is not null && !ld.Rows.Any(x => x.Res == "unread"), B("Every point read", "Ogni punto letto"), c.Owner);
                It(c.Signed is not null, B("Signed file added", "File firmato aggiunto"), c.Owner);
                It(c.Signed?.ConfirmedBy is not null, B("Confirmed as final by {who}", "Confermato come definitivo da {who}", ("who", c.Approver)), c.Approver);
                break;
            }

            default:
                It(c.Pipe is { Stage: >= 3 }, B("Extraction done", "Estrazione completata"));
                It(c.Pipe is not null && (c.Pipe.Extracted || c.Pipe.Weak.Count > 0) && !c.Pipe.Weak.Any(w => w.State == "open"), B("Flagged fields decided", "Campi segnalati decisi"), c.Owner);
                It(c.Outcome is not null, B("New contract validated and scheduled", "Nuovo contratto validato e programmato"));
                break;
        }

        return list;
    }

    private static int AltStage(RenewalCase c)
    {
        var a = c.Alt;
        if (a is null)
        {
            return -1;
        }

        if (c.Outcome?.ReplacedBy is not null)
        {
            return 8;
        }

        switch (a.State)
        {
            case "req": return 0;
            case "cand": return 1;
            case "rfq":
            {
                var rf = c.Emails.Where(e => e.Kind == "rfq").ToList();
                return rf.Count > 0 && rf.All(e => e.State == EmailStates.Sent) ? 3 : 2;
            }

            case "compare": return 4;
            case "selected": return !a.ExitDone ? 5 : !a.NewDone ? 6 : 7;
            default: return 0;
        }
    }

    private static CaseAltView AltView(RenewalCase c, CaseContext ctx)
    {
        var st = AltStage(c);
        var steps = AStg.Select((x, i) => new CaseStepItem(x, i + 1, i < st, i == st)).ToList();
        return new CaseAltView(st, steps, st > 7 ? [] : AltChecklist(c, ctx, st), AEx[Math.Min(st, 7)]);
    }

    private static List<CaseChecklistItem> AltChecklist(RenewalCase c, CaseContext ctx, int st)
    {
        var a = c.Alt!;
        var list = new List<CaseChecklistItem>();
        var rf = c.Emails.Where(e => e.Kind == "rfq").ToList();
        var n = c.Notice;
        void It(bool done, CaseText label, string who = "") => list.Add(new CaseChecklistItem(done, label, who));
        switch (st)
        {
            case 0:
                It(true, B("Features extracted from your contract", "Funzioni estratte dal tuo contratto"));
                It(a.State != "req", B("Features confirmed", "Funzioni confermate"), c.Owner);
                break;
            case 1:
                It(a.Vendors.Count > 0, B("At least one vendor named", "Almeno un fornitore indicato"), c.Owner);
                It(a.Vendors.Count > 1, B("A second vendor to compare (recommended)", "Un secondo fornitore da confrontare (consigliato)"), c.Owner);
                It(a.State != "cand", B("Request emails prepared", "Email di richiesta preparate"), c.Owner);
                break;
            case 2:
                It(rf.Count > 0 && rf.All(e => e.State is EmailStates.Approved or EmailStates.Copied or EmailStates.Sent), B("Request emails approved by {who}", "Email di richiesta approvate da {who}", ("who", c.Approver)), c.Approver);
                It(rf.Count > 0 && rf.All(e => e.State == EmailStates.Sent), B("Requests sent and confirmed", "Richieste inviate e confermate"), c.Owner);
                break;
            case 3:
                It(a.Offers.Count > 0, B("Offer received from at least one vendor", "Offerta ricevuta da almeno un fornitore"), c.Owner);
                break;
            case 4:
                It(a.Cmp, B("Offers compared with your current contract", "Offerte confrontate con il tuo contratto attuale"), c.Approver);
                It(a.State == "selected", B("Vendor chosen by {who}", "Fornitore scelto da {who}", ("who", c.Approver)), c.Approver);
                break;
            case 5:
                It(n is not null, B("Notice email prepared", "Email di disdetta preparata"), c.Owner);
                It(n is { State: "sent" or "out" }, B("Notice sent and confirmed", "Disdetta inviata e confermata"), c.Owner);
                It(n is { State: "out" }, B("Outcome recorded: not renewed", "Esito registrato: non rinnovato"), c.Approver);
                break;
            case 6:
                It(a.NewDone, B("Signed contract of {vendor} added", "Contratto firmato di {vendor} aggiunto", ("vendor", a.Chosen)), c.Owner);
                break;
            case 7:
                It(c.Outcome is not null, B("Replacement recorded", "Sostituzione registrata"));
                break;
        }

        return list;
    }

    /* ---------- todo ---------- */

    /// <summary>The Today-feed items this case produces (<c>RC.todo</c>); empty once an outcome is recorded.</summary>
    public static IReadOnlyList<CaseTodoItem> Todo(RenewalCase c, CaseContext ctx)
    {
        var cn = ctx.Contract;
        var apw = Apw(c);
        var st = Stage(c);
        var raw = new List<(string Kind, string Who, CaseText Title, CaseText Why, DateOnly Due)>();
        if (c.Outcome is not null)
        {
            return [];
        }

        void Add(string kind, string who, CaseText title, CaseText why, DateOnly? due = null) => raw.Add((kind, who, title, why, due ?? cn.Cancel));

        foreach (var e in c.Emails)
        {
            if (e.State is EmailStates.Superseded or EmailStates.Sent)
            {
                continue;
            }

            var k = Kx(e.Kind);
            if (e.State == EmailStates.Draft && string.IsNullOrEmpty(e.To))
            {
                Add("recipient", c.Owner, B("Add the recipient: {kind}", "Aggiungi il destinatario: {kind}", ("kind", k)), B("Not in the contract — Raffa never invents addresses.", "Non è nel contratto — Raffa non inventa indirizzi."));
            }
            else if (e.State == EmailStates.Draft)
            {
                Add("request", c.Owner, B("Send the {kind} for approval", "Invia la {kind} in approvazione", ("kind", k)), B("Draft ready. Nothing is sent until it is approved and you confirm the send.", "Bozza pronta. Nulla parte finché non è approvata e non confermi l’invio."));
            }
            else if (e.State == EmailStates.ToApprove)
            {
                Add("approve", apw, B("Approve the {kind}", "Approva la {kind}", ("kind", k)), B("Submitted by {owner}. Nothing is sent until you approve this exact version.", "Inviata da {owner}. Nulla parte finché non approvi questa versione esatta.", ("owner", c.Owner)));
            }
            else
            {
                Add("send", c.Owner, B("Send and confirm: {kind}", "Invia e conferma: {kind}", ("kind", k)), B("Approved by {who}. Copy, send from your mailbox, then confirm.", "Approvata da {who}. Copia, invia dalla tua casella, poi conferma.", ("who", e.ApprovedBy)));
            }
        }

        if (c.Phase is CasePhases.Interview or CasePhases.Plan)
        {
            Add("plan", c.Owner, B("Complete the renewal plan", "Completa il piano di rinnovo"),
                c.Phase == CasePhases.Plan ? B("Raffa drafted the plan — confirm or edit it.", "Raffa ha preparato il piano — confermalo o modificalo.") : B("Question {n} of 4 is open.", "La domanda {n} di 4 è aperta.", ("n", c.Q + 1)));
        }

        foreach (var x in c.Rems)
        {
            if (x.State == "proposed")
            {
                Add("remind", c.Owner, B("Confirm the check date", "Conferma la data di controllo"), B("Proposed: {date}", "Proposta: {date}", ("date", D(x.Date))));
            }

            if (x.State == "due")
            {
                Add("reply", c.Owner, B("Register the supplier reply", "Registra la risposta del fornitore"), B("Check date {date} reached — no reply registered in Raffa.", "Data di controllo {date} raggiunta — nessuna risposta registrata in Raffa.", ("date", D(x.Date))), x.Date);
            }
        }

        foreach (var q in c.Qs)
        {
            if (q.State == "open")
            {
                Add("decide", c.Owner, B("Decide the price / term trade-off", "Decidi il compromesso prezzo / durata"), B("The supplier ties the discount to 24 months.", "Il fornitore lega lo sconto a 24 mesi."));
            }

            if (q.State == "agree")
            {
                if (LiabilityConflict(c) && string.IsNullOrWhiteSpace(c.Ui.Auth))
                {
                    Add("policy", apw, B("Policy exception: liability {liab} vs 24 months", "Deroga alla policy: responsabilità {liab} vs 24 mesi", ("liab", CaseAgreement.ProposedLiability(c))), B("Binding rule in Procurement Policy v3.2. Record who authorizes it.", "Regola vincolante della Procurement Policy v3.2. Registra chi la autorizza."));
                }
                else
                {
                    Add("agree", c.Owner, B("Confirm the agreed terms", "Conferma le condizioni concordate"), B("The supplier confirmed in writing.", "Il fornitore ha confermato per iscritto."));
                }
            }
        }

        if (c.Phase == CasePhases.DocCheck)
        {
            Add("draft", c.Owner, B("Resolve the draft differences", "Risolvi le differenze della bozza"), B("The draft differs from what was agreed.", "La bozza differisce da quanto concordato."));
        }

        if (c.Phase is CasePhases.SignWait or CasePhases.SignDeclared)
        {
            Add("sign", c.Owner, B("Add the signed contract", "Aggiungi il contratto firmato"), B("Signing happens outside Raffa.", "La firma avviene fuori da Raffa."));
        }

        if (c.Phase == CasePhases.SignConfirm)
        {
            Add("sigok", apw, B("Confirm the signed contract", "Conferma il contratto firmato"), B("Only you can close the negotiation.", "Solo tu puoi chiudere la negoziazione."));
        }

        if (c.Pipe is { Stage: 3 } pipe)
        {
            Add("fields", c.Owner, B("Validate the flagged fields", "Valida i campi segnalati"),
                pipe.Extracted
                    ? B("2 fields below 80% in the new contract.", "2 campi sotto l’80% nel nuovo contratto.")
                    : B("{n} values to check against the signed contract.", "{n} valori da verificare sul contratto firmato.", ("n", pipe.Weak.Count(w => w.State == "open"))));
        }

        if (c.Notice is { State: "check" })
        {
            Add("notice", c.Owner, B("Prepare the non-renewal notice", "Prepara la disdetta"), B("Clause and deadline already checked.", "Clausola e scadenza già verificate."));
        }

        if (c.Notice is { State: "sent" })
        {
            Add("nout", apw, B("Record the notice outcome", "Registra l’esito della disdetta"), B("Notice communicated — outcome to verify.", "Disdetta comunicata — esito da verificare."));
        }

        if (c.Alt is { State: "compare" })
        {
            Add("alt", apw, B("Evaluate the offers and choose", "Valuta le offerte e scegli"), B("Offers received — compare them with your current contract. The current contract stays open.", "Offerte ricevute — confrontale con il tuo contratto attuale. Il contratto attuale resta aperto."));
        }

        if (c.Alt is { } a && c.Outcome is null)
        {
            var rfqs = c.Emails.Where(e => e.Kind == "rfq").ToList();
            if (a.State == "req")
            {
                Add("altreq", c.Owner, B("Confirm the features the new supplier must cover", "Conferma le funzioni che il nuovo fornitore deve coprire"), B("Step 1 of 8 — features, not product names, go to the new vendors.", "Passo 1 di 8 — ai nuovi fornitori vanno le funzioni, non i nomi dei prodotti."));
            }

            if (a.State == "cand")
            {
                Add("altcand", c.Owner, B("Choose the vendors to ask", "Scegli i fornitori da interpellare"), B("Step 2 of 8 — name at least one candidate.", "Passo 2 di 8 — indica almeno un candidato."));
            }

            if (a.State == "rfq" && rfqs.Count > 0 && rfqs.All(e => e.State == EmailStates.Sent))
            {
                Add("altoffer", c.Owner, B("Add the offers as they arrive", "Aggiungi le offerte man mano che arrivano"), B("Step 4 of 8 — paste or attach each vendor’s offer.", "Passo 4 di 8 — incolla o allega l’offerta di ciascun fornitore."));
            }

            if (a.State == "selected" && !a.ExitDone && c.Notice is null)
            {
                Add("altexit", apw, B("Give notice to {supplier}", "Dai disdetta a {supplier}", ("supplier", cn.Supplier)), B("Step 6 of 8 — the old contract ends only with a notice.", "Passo 6 di 8 — il vecchio contratto termina solo con la disdetta."));
            }

            if (a.State == "selected" && a.ExitDone && !a.NewDone)
            {
                Add("altnew", c.Owner, B("Add the signed contract of {vendor}", "Aggiungi il contratto firmato di {vendor}", ("vendor", a.Chosen)), B("Step 7 of 8 — then the replacement is complete.", "Passo 7 di 8 — poi la sostituzione è completa."));
            }
        }

        var stl = -DaysTo(ctx, c.Updated);
        return raw.Select(x => new CaseTodoItem
        {
            Kind = x.Kind,
            Who = x.Who,
            Title = x.Title,
            Why = x.Why,
            Due = D(x.Due),
            DueDays = DaysTo(ctx, x.Due),
            Stage = st,
            StageLabel = Stg[st],
            Stalled = stl >= 7 ? stl : 0,
        }).ToList();
    }
}
