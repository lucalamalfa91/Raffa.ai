using Raffa.Renewals.Application.CaseWork;
using Raffa.Renewals.Application.Cases;
using Raffa.SharedKernel.Text;
using static Raffa.Renewals.Tests.Cases.CaseFixtures;

namespace Raffa.Renewals.Tests.Cases;

/// <summary>
/// The renewal case outside the demo script (<see cref="ProductionCaseScriptServices"/> plus the model's
/// readings), on the QA scenario: a CrowdStrike contract (2,850 endpoints at CHF 77.60 a year plus a one-off
/// onboarding fee) renewed on the supplier's written offer of 12 months at CHF 76.50 per endpoint, no
/// uplift. Every figure an email, the agreed position or the new contract shows must be one the supplier
/// wrote or the owner checked — never a prototype value (24 months, a 4% uplift, a credit review,
/// quarterly payment, an 18-month liability cap).
/// </summary>
public sealed class ProductionCaseFlowTests
{
    private static readonly ProductionCaseScriptServices Production = new();

    private static readonly CaseContractFacts CrowdStrike = Contract(1) with
    {
        Supplier = "CrowdStrike",
        Name = "CrowdStrike Order Form",
        Type = "OrderForm",
        File = "CrowdStrike_Falcon_Renewal_2025.docx",
        Spend = 224_160,
        Start = new DateOnly(2025, 7, 5),
        End = new DateOnly(2028, 7, 4),
        Cancel = new DateOnly(2028, 4, 5),
        Notice = 90,
        Auto = false,
        Uplift = "4%",
        Products =
        [
            new CaseProduct("Onboarding fee", "ONB-1", "one-time", 1, 3_000, OneOff: true),
            new CaseProduct("Falcon Enterprise", "FAL-ENT", "endpoint / year", 2_850, 77.60),
        ],
        Push = [],
        Raise = [],
    };

    private static CaseContext Ctx(string who, ICaseScriptServices? services = null, CaseContractFacts? contract = null) =>
        new(contract ?? CrowdStrike, who, Today, services ?? Production);

    private static RenewalCase Act(RenewalCase c, string who, string type, CaseActionPayload? p = null, ICaseScriptServices? services = null) =>
        RenewalCaseEngine.Act(c, type, p, Ctx(who, services));

    private static CaseReplyAnalysis Reading(params (string Key, string Label, string Val)[] terms) => new(
        terms.Select(t => new CaseReplyRow { Label = CaseText.Of(t.Label), Said = CaseText.Of(t.Val), Plan = CaseText.Empty, State = "proposed" }).ToList(),
        terms.Select(t => new CaseTermUpdate(t.Key, CaseText.Of(t.Label), "proposed", t.Val, "reply 0")).ToList(),
        new Dictionary<int, double> { [12] = 76.50 });

    private static RenewalCase SendEmail(RenewalCase c, int eid, int rid)
    {
        c = Act(c, Marta, "ui", new() { K = "to:" + eid, V = "sales@crowdstrike.example" });
        c = Act(c, Marta, "requestAppr", new() { Eid = eid });
        c = Act(c, Luca, "approve", new() { Eid = eid });
        c = Act(c, Marta, "copy", new() { Eid = eid });
        c = Act(c, Marta, "sentOpen", new() { Eid = eid });
        c = Act(c, Marta, "sentOk", new() { Eid = eid });
        return Act(c, Marta, "remOk", new() { Rid = rid });
    }

    private static RenewalCase Reply(RenewalCase c, string text, CaseReplyAnalysis reading)
    {
        // The service stamps the reading's evidence with the reply number; the engine applies it as is.
        var n = c.Reps.Count + 1;
        var stamped = reading with { Terms = reading.Terms.Select(t => t with { Ev = "reply " + n }).ToList() };
        c = Act(c, Marta, "openReply");
        return Act(c, Marta, "reply", new() { Text = CaseText.Of(text) }, new ReadCaseScriptServices(Production, reply: stamped));
    }

    private static RenewalCase ToFirstEmail()
    {
        var c = RenewalCaseEngine.Create(1, Ctx(Luca), owner: Marta);
        c = Act(c, Marta, "ans", new() { Q = 0, Val = "keep" });
        c = Act(c, Marta, "ans", new() { Q = 1, Val = "12" });
        c = Act(c, Marta, "ans", new() { Q = 2, Val = "p5" });
        c = Act(c, Marta, "q3done");
        return Act(c, Marta, "confirmPlan");
    }

    private static RenewalCase ToAgreement()
    {
        var c = SendEmail(ToFirstEmail(), 0, 0);
        c = Reply(c, "We can offer 12 months at CHF 76.50 per endpoint, with no uplift.",
            Reading(("price", "Unit price", "CHF 76.50 per endpoint"), ("term", "Term", "12 months"), ("uplift", "Uplift", "none")));
        c = SendEmail(c, c.Emails.Count - 1, 1);
        return Reply(c, "Confirmed: 2,850 endpoints, 12 months, CHF 76.50 per endpoint, no uplift.",
            Reading(("price", "Unit price", "CHF 76.50 per endpoint"), ("term", "Term", "12 months"), ("qty", "Quantity", "2,850 endpoints"), ("uplift", "Uplift", "none")));
    }

    private static readonly string[] PrototypeValues = ["24-month", "24 months", "4%", "credit", "quarterly", "liability", "Limitation"];

    [Fact]
    public void The_first_email_names_the_contract_and_keeps_one_off_amounts_out_of_the_renewal_scope()
    {
        var first = ToFirstEmail().Emails[0];

        Assert.Equal("CrowdStrike Order Form — renewal discussion", first.Subject.Split(" — ")[0] + " — " + first.Subject.Split(" — ")[1]);
        Assert.DoesNotContain(".docx", first.Subject, StringComparison.Ordinal);
        Assert.Contains("the same products and quantities as today (2,850 × Falcon Enterprise)", first.Body, StringComparison.Ordinal);
        Assert.Contains("• One-off items of the current contract, not part of this renewal: Onboarding fee", first.Body, StringComparison.Ordinal);
    }

    [Fact]
    public void A_draft_written_with_the_file_name_takes_the_contract_name_while_still_a_draft()
    {
        var c = ToFirstEmail();
        c.Emails[0].Subject = "CrowdStrike_Falcon_Renewal_2025.docx — renewal discussion";
        c.Emails[0].Body = "Our CrowdStrike_Falcon_Renewal_2025.docx agreement ends on 04 Jul 2028.";

        var renamed = RenewalCaseEngine.RenameDraftsFromFileName(c, CrowdStrike);

        Assert.Equal("CrowdStrike Order Form — renewal discussion", renamed.Emails[0].Subject);
        Assert.Equal("Our CrowdStrike Order Form agreement ends on 04 Jul 2028.", renamed.Emails[0].Body);
        Assert.Contains("CrowdStrike_Falcon_Renewal_2025.docx", renamed.Emails[0].Attach);
    }

    [Fact]
    public void The_counter_proposal_repeats_only_what_the_supplier_wrote_and_labels_our_requests()
    {
        var c = SendEmail(ToFirstEmail(), 0, 0);
        c = Reply(c, "We can offer 12 months at CHF 76.50 per endpoint, with no uplift.",
            Reading(("price", "Unit price", "CHF 76.50 per endpoint"), ("term", "Term", "12 months"), ("uplift", "Uplift", "none")));

        // No 12/24 trade-off in the reply: no "the discount is tied to 24 months" decision.
        Assert.Empty(c.Qs);
        var counter = c.Emails[^1];
        Assert.Equal("counter", counter.Kind);
        Assert.Contains("This is how we read it:\n• Unit price: CHF 76.50 per endpoint\n• Term: 12 months\n• Uplift: none", counter.Body, StringComparison.Ordinal);
        foreach (var v in PrototypeValues)
        {
            Assert.DoesNotContain(v, counter.Body, StringComparison.Ordinal);
        }

        var analysis = RenewalCaseEngine.View(c, Ctx(Marta)).Cards.SelectMany(card => card.Lines).Select(l => l.Render()).ToList();
        Assert.DoesNotContain(analysis, l => l.Contains("Payment terms are unclear", StringComparison.Ordinal));
    }

    [Fact]
    public void Confirm_as_agreed_records_exactly_the_confirmed_terms_and_the_request_email_adds_nothing()
    {
        var c = ToAgreement();
        c = Act(c, Marta, "agreeOk", new() { Did = c.Qs.Count - 1 });

        var a = Assert.IsType<CaseAgreed>(c.Agreed);
        Assert.Equal(76.50, a.Price);
        Assert.Equal(12, a.Term);
        Assert.Equal(2_850, a.Qty);
        Assert.Equal(0, a.Cap);
        Assert.Null(a.Liab);
        Assert.Null(a.Payment);

        var docreq = c.Emails[^1];
        Assert.Equal("docreq", docreq.Kind);
        Assert.Contains("• Quantities: 2,850 × Falcon Enterprise\n• Unit price: CHF 76.5 per endpoint / year\n• Term: 12 months\n• Uplift: no price increase during the term", docreq.Body, StringComparison.Ordinal);
        Assert.DoesNotContain("quarterly", docreq.Body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("liability", docreq.Body, StringComparison.OrdinalIgnoreCase);

        // The draft check looks only for what was agreed: no liability-cap row to find "missing".
        var rows = Production.VerifyDraft(c, Ctx(Marta), 1);
        Assert.DoesNotContain(rows, r => r.Label.Render().Contains("Liability", StringComparison.Ordinal));
    }

    [Fact]
    public void Confirm_as_agreed_refuses_when_the_replies_never_stated_a_price()
    {
        var c = SendEmail(ToFirstEmail(), 0, 0);
        c = Reply(c, "We can renew for 12 months.", new CaseReplyAnalysis(
            [new CaseReplyRow { Label = CaseText.Of("Term"), Said = CaseText.Of("12 months"), Plan = CaseText.Empty, State = "proposed" }],
            [new CaseTermUpdate("term", CaseText.Of("Term"), "proposed", "12 months", "reply 0")]));
        c = SendEmail(c, c.Emails.Count - 1, 1);
        c = Reply(c, "Yes, 12 months.", new CaseReplyAnalysis(
            [new CaseReplyRow { Label = CaseText.Of("Term"), Said = CaseText.Of("12 months"), Plan = CaseText.Empty, State = "proposed" }],
            [new CaseTermUpdate("term", CaseText.Of("Term"), "proposed", "12 months", "reply 0")]));

        c = Act(c, Marta, "agreeOk", new() { Did = c.Qs.Count - 1 });

        Assert.Null(c.Agreed);
        Assert.Contains("the unit price", c.Ui.Warn!.Render(), StringComparison.Ordinal);
        Assert.Equal("proposed", c.Terms["term"].State);
    }

    [Fact]
    public void The_new_contract_value_is_the_checked_price_times_the_recurring_quantity()
    {
        var c = ToAgreement();
        c = Act(c, Marta, "agreeOk", new() { Did = c.Qs.Count - 1 });
        c = SendEmail(c, c.Emails.Count - 1, 2);
        var draftOk = new ReadCaseScriptServices(Production, draftRows:
            [new CaseDraftRow { Label = CaseText.Of("Unit price"), Agreed = CaseText.Of("CHF 76.5"), Draft = CaseText.Of("CHF 76.50"), Res = "ok", Src = "p.2" }]);
        c = Act(c, Marta, "addDoc", new() { File = "CrowdStrike_OrderForm_draft.pdf" }, draftOk);
        c = Act(c, Marta, "addDoc", new() { File = "CrowdStrike_OrderForm_signed.pdf" });
        c = Act(c, Luca, "sigOk");

        // Nothing was extracted from the signed file: its values are put to the owner, with their source.
        var pipe = Assert.IsType<CasePipe>(c.Pipe);
        Assert.False(pipe.Extracted);
        Assert.False(c.Tick);
        Assert.Equal([CaseSignedFields.Price, CaseSignedFields.Qty, CaseSignedFields.Term, CaseSignedFields.Annual], pipe.Weak.Select(w => w.N));
        Assert.Equal("CHF 218,025", pipe.Weak[3].Val);
        Assert.All(pipe.Weak, w => Assert.Contains("CrowdStrike_OrderForm_signed.pdf", w.Src.Render(), StringComparison.Ordinal));
        var card = RenewalCaseEngine.View(c, Ctx(Marta)).Page.Current;
        Assert.DoesNotContain(card.Lines, l => l.Render().Contains("41 facts", StringComparison.Ordinal));
        Assert.True(Assert.Single(card.Buttons, b => b.Act == "validate").Off);

        c = Act(c, Marta, "validate");
        Assert.Null(c.Outcome);
        for (var i = 0; i < 4; i++)
        {
            c = Act(c, Marta, "acceptF", new() { I = i });
        }

        c = Act(c, Marta, "validate");

        var o = Assert.IsType<CaseOutcome>(c.Outcome);
        Assert.Equal(218_025, o.Spend!.Value, 3);
        Assert.Equal(221_160 - 218_025, o.Delta!.Value, 3);
        Assert.Equal(new DateOnly(2028, 7, 4), o.PrevEnd);
        Assert.Equal(new DateOnly(2029, 7, 4), o.End);

        // Validating again never compounds the renewal.
        var again = Act(c, Marta, "validate");
        Assert.Equal(o.End, again.Outcome!.End);
    }

    [Fact]
    public void A_closed_case_reloaded_on_the_renewed_facts_shows_the_outcome_and_both_contracts_dates()
    {
        var c = ToAgreement();
        c = Act(c, Marta, "agreeOk", new() { Did = c.Qs.Count - 1 });
        c = Act(c, Marta, "addDoc", new() { File = "CrowdStrike_OrderForm_draft.pdf" },
            new ReadCaseScriptServices(Production, draftRows: [new CaseDraftRow { Label = CaseText.Of("Unit price"), Agreed = CaseText.Of("x"), Draft = CaseText.Of("x"), Res = "ok" }]));
        c = Act(c, Marta, "addDoc", new() { File = "CrowdStrike_OrderForm_signed.pdf" });
        c = Act(c, Luca, "sigOk");
        for (var i = 0; i < 4; i++)
        {
            c = Act(c, Marta, "acceptF", new() { I = i });
        }

        c = Act(c, Marta, "validate");
        // The Order Form request was never sent: it must not come back as the step to work on.
        Assert.Contains(c.Emails, e => e.Kind == "docreq" && e.State == EmailStates.Draft);

        // After closure the contract's facts carry the new term (what a reload reads).
        var renewed = CrowdStrike with { Start = c.Outcome!.Start!.Value, End = c.Outcome.End, Cancel = c.Outcome.Cancel!.Value };
        var view = RenewalCaseEngine.View(c, Ctx(Marta, contract: renewed));

        Assert.Equal("out", view.Page.Current.K);
        Assert.Equal("04 Jul 2029 (new contract; previous ends 04 Jul 2028)", view.Hdr.End.Render());
        Assert.Contains(view.Page.Current.Rows, r => r.V.Render() == "stays active until 04 Jul 2028");
        Assert.Contains(view.Page.Current.Rows, r => r.V.Render() == "CrowdStrike_OrderForm_signed.pdf");
    }
}
