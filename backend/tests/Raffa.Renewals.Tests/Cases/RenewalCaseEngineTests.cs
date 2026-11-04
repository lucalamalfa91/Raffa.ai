using System.Text.Json.Nodes;
using Raffa.Renewals.Application.Cases;
using Raffa.SharedKernel.Text;
using static Raffa.Renewals.Tests.Cases.CaseFixtures;

namespace Raffa.Renewals.Tests.Cases;

/// <summary>
/// The AskRaffa 4.0 renewal case engine (<see cref="RenewalCaseEngine"/>, port of the prototype's
/// <c>renewal-case-v4.js</c>): the "Da zero" golden path on Salesforce, the three seeded "Con dati"
/// cases, and the hard product rules — only the approver or an explicit delegate approves, closing
/// needs closing rights, an edit after approval returns the email to draft as a new version, copying
/// is never sending, the recipient is never invented.
/// </summary>
public sealed class RenewalCaseEngineTests
{
    private static RenewalCase Act(RenewalCase c, string who, string type, CaseActionPayload? p = null, int cid = 1) =>
        RenewalCaseEngine.Act(c, type, p, Ctx(cid, who));

    private static CaseCurrentCard Current(RenewalCase c, string who, int cid = 1) => RenewalCaseEngine.View(c, Ctx(cid, who)).Page.Current;

    private static RenewalCase AtFirstEmail(int cid = 1)
    {
        var c = RenewalCaseEngine.Create(cid, Ctx(cid, Luca), owner: Marta);
        c = Act(c, Marta, "ans", new() { Q = 0, Val = "keep" }, cid);
        c = Act(c, Marta, "ans", new() { Q = 1, Val = "12" }, cid);
        c = Act(c, Marta, "ans", new() { Q = 2, Val = "p10" }, cid);
        c = Act(c, Marta, "q3done", null, cid);
        return Act(c, Marta, "confirmPlan", null, cid);
    }

    [Fact]
    public void Golden_path_da_zero_on_Salesforce_goes_from_interview_to_renewed()
    {
        var states = Replay("salesforce_da_zero").ToList();
        var byStep = states.Select(s => (s.Type, s.Case.Phase, Stage: RenewalCaseEngine.Stage(s.Case), Title: Current(s.Case, s.Who).Title.Render())).ToList();

        Assert.Equal(("create", "interview", 0), (byStep[0].Type, byStep[0].Phase, byStep[0].Stage));
        Assert.Equal("First: what do you want to do with the products and quantities?", byStep[0].Title);
        Assert.Contains(byStep, s => s is { Type: "q3done", Phase: "plan", Stage: 1, Title: "Renewal plan — Salesforce" });
        Assert.Contains(byStep, s => s is { Type: "confirmPlan", Phase: "email", Stage: 2 } && s.Title.EndsWith("renewal discussion — Helvetia Foods AG", StringComparison.Ordinal));
        Assert.Contains(byStep, s => s is { Type: "sentOk", Phase: "waiting_rem", Title: "When should I remind you to check for a reply?" });
        Assert.Contains(byStep, s => s is { Type: "remOk", Phase: "waiting" } && s.Title.StartsWith("Check set for ", StringComparison.Ordinal));
        Assert.Contains(byStep, s => s is { Type: "ff", Phase: "waiting_due", Title: "Did you receive a reply?" });
        Assert.Contains(byStep, s => s is { Type: "reply", Phase: "review", Title: "The discount is tied to 24 months. What is your limit?" });
        Assert.Contains(byStep, s => s is { Type: "decide", Phase: "email", Stage: 2, Title: "Re: Sales Cloud Enterprise — MSA + Order Form — counter-proposal" });
        Assert.Contains(byStep, s => s is { Type: "reply", Phase: "agree", Stage: 3, Title: "Mark these as agreed?" });
        Assert.Contains(byStep, s => s is { Type: "agreeOk", Phase: "doc_wait", Stage: 4, Title: "Sales Cloud Enterprise — MSA + Order Form — revised Order Form for signature" });
        Assert.Contains(byStep, s => s is { Type: "addDoc", Phase: "doc_check", Title: "Salesforce_OrderForm_draft_v1.pdf" });
        Assert.Contains(byStep, s => s is { Type: "fixEmail", Phase: "email", Stage: 4, Title: "Sales Cloud Enterprise — MSA + Order Form — corrections to the draft Order Form" });
        Assert.Contains(byStep, s => s is { Type: "addDoc", Phase: "sign_wait", Title: "Ready to sign — outside Raffa" });
        Assert.Contains(byStep, s => s is { Type: "addDoc", Phase: "sign_confirm", Title: "Salesforce_OrderForm_signed.pdf" });
        Assert.Contains(byStep, s => s is { Type: "sigOk", Phase: "pipeline", Stage: 5, Title: "Standard pipeline — same one, inside the chat" });

        var last = states[^1].Case;
        Assert.Equal("done", last.Phase);
        Assert.Equal(5, RenewalCaseEngine.Stage(last));
        Assert.Equal("Renewed — new contract scheduled", Current(last, Marta).Title.Render());
        Assert.Equal("Renewed", RenewalCaseEngine.ShortStatus(last));
        var o = last.Outcome!;
        Assert.Equal("renewed", o.Kind);
        Assert.Equal(new DateOnly(2027, 1, 16), o.Start);
        Assert.Equal(new DateOnly(2029, 1, 15), o.End); // 24-month term agreed (option a)
        Assert.Equal(new DateOnly(2028, 10, 17), o.Cancel); // 90 days' notice
        Assert.Equal(300 * (156 - 144) * 12, o.Delta); // same quantities, signed price vs previous
        Assert.Equal("CHF 144 per user / month (was 156)", o.Price!.Render());
        Assert.Empty(RenewalCaseEngine.Todo(last, Ctx(1, Marta)));

        // The edited sending was recorded, and differs from the approved text.
        var counter = last.Emails.Single(e => e.Kind == "counter");
        Assert.True(counter.Sent!.Edited);
        Assert.Equal("Dear Salesforce team, as discussed on the phone…", counter.Sent.Text);
    }

    [Fact]
    public void Seeded_DocuSign_case_shows_the_counter_proposal_to_approve()
    {
        var c = Replay("seed_docusign").Last().Case;
        var card = Current(c, Luca, 4);
        Assert.Equal("email", c.Phase);
        Assert.Equal("Counter-proposal · v1", card.Kicker.Render());
        Assert.Equal("Re: eSignature Business Pro — counter-proposal", card.Title.Render());
        Assert.Equal("to_approve", card.Email!.StateKey);
        Assert.Equal("To approve — waiting for Luca Lamalfa", card.Email.State.Render());
        Assert.Equal("anna.meyer@docusign.example", card.Email.To);
        Assert.Equal("Approve this version", card.Buttons[0].Label.Render());
        Assert.False(card.Buttons[0].Off);

        // For Marta (owner, not approver) the same button is shown disabled with the reason.
        var hers = Current(c, Marta, 4);
        Assert.True(hers.Buttons[0].Off);
        Assert.Equal("Only the approver (CPO) or an explicit delegate can approve emails.", hers.Buttons[0].Why!.Render());
        var todo = RenewalCaseEngine.Todo(c, Ctx(4, Luca));
        Assert.Contains(todo, t => t.Kind == "approve" && t.Who == Luca && t.Title.Render() == "Approve the counter-proposal");
    }

    [Fact]
    public void Seeded_Microsoft_case_waits_on_the_clause_question()
    {
        var c = Replay("seed_microsoft").Last().Case;
        var card = Current(c, Marta, 2);
        Assert.Equal("interview", c.Phase);
        Assert.Equal("Question 4 / 4", card.Kicker.Render());
        Assert.Equal("Last: which clauses do you want to change?", card.Title.Render());
        Assert.Contains(card.Chips, ch => ch.Label.Render() == "Price protection" && ch.On);
        Assert.Equal("Continue", card.Buttons[0].Label.Render());
    }

    [Fact]
    public void Seeded_AWS_case_asks_to_confirm_terms_with_the_policy_exception()
    {
        var states = Replay("seed_aws").ToList();
        var seeded = states[^4].Case; // the prototype's seed() ends after the second reply
        var card = Current(seeded, Marta, 3);
        Assert.Equal("agree", seeded.Phase);
        Assert.Equal("Mark these as agreed?", card.Title.Render());
        Assert.StartsWith("Liability 18 months conflicts with Procurement Policy v3.2", card.Banner.Render(), StringComparison.Ordinal);
        Assert.Equal("auth", card.Inputs.Single().Ui);

        // Confirming without recording who authorized the exception is refused.
        var refused = states[^3].Case;
        Assert.Equal("agree", refused.Phase);
        Assert.Equal("Record who authorized the exception to the policy first.", refused.Ui.Warn!.Render());
        var agreed = states[^1].Case;
        Assert.Equal("doc_wait", agreed.Phase);
        Assert.Equal("Legal · General Counsel", agreed.Agreed!.Auth);
    }

    [Fact]
    public void Marta_cannot_approve_and_gets_the_reason()
    {
        var c = AtFirstEmail();
        c = Act(c, Marta, "ui", new() { K = "to:0", V = "anna@salesforce.example" });
        c = Act(c, Marta, "requestAppr", new() { Eid = 0 });
        var denied = Act(c, Marta, "approve", new() { Eid = 0 });
        Assert.Equal("to_approve", denied.Emails[0].State);
        Assert.Equal("Only the approver (CPO) or an explicit delegate can approve emails.", denied.Ui.Warn!.Render());
        Assert.Equal("Only the approver (CPO) or an explicit delegate can approve emails.", denied.Ui.Warn.Id);
    }

    [Fact]
    public void A_delegate_with_approve_can_approve_but_closing_needs_can_close()
    {
        var c = AtFirstEmail();
        c = Act(c, Luca, "delegate", new() { Who = Marta, On = true });
        Assert.False(c.Delegates[Marta].Close);
        c = Act(c, Marta, "ui", new() { K = "to:0", V = "anna@salesforce.example" });
        c = Act(c, Marta, "approve", new() { Eid = 0 });
        Assert.Equal("approved", c.Emails[0].State);
        Assert.Equal(Marta, c.Emails[0].ApprovedBy);

        // Closing actions: a delegate without closing rights is refused …
        var sig = new RenewalCase { Owner = Marta, Approver = Luca, Phase = "sign_confirm", Signed = new CaseSigned { File = "x.pdf" } };
        sig.Delegates[Marta] = new CaseDelegate { Close = false };
        var refused = Act(sig, Marta, "sigOk");
        Assert.Equal("sign_confirm", refused.Phase);
        Assert.Equal("Only the approver (CPO), or a delegate with closing rights, can confirm this.", refused.Ui.Warn!.Render());

        // … until the approver grants can_close.
        var granted = Act(sig, Luca, "delegateClose", new() { Who = Marta });
        Assert.True(granted.Delegates[Marta].Close);
        var closed = Act(granted, Marta, "sigOk");
        Assert.Equal("pipeline", closed.Phase);
        Assert.Equal(Marta, closed.Signed!.ConfirmedBy);

        // Only the approver manages delegations.
        var notYours = Act(c, Marta, "delegate", new() { Who = "Someone", On = true });
        Assert.Equal("Only the approver (CPO) manages delegations.", notYours.Ui.Warn!.Render());
        Assert.False(notYours.Delegates.ContainsKey("Someone"));
    }

    [Fact]
    public void A_collaborator_cannot_work_the_case()
    {
        var c = RenewalCaseEngine.Create(1, Ctx(1, Luca), owner: Marta, members: [Luca, Marta, "Paolo Rossi"]);
        c = Act(c, Marta, "ans", new() { Q = 0, Val = "keep" });
        c = Act(c, Marta, "ans", new() { Q = 1, Val = "24" });
        c = Act(c, Marta, "ans", new() { Q = 2, Val = "none" });
        c = Act(c, Marta, "q3done");
        var denied = Act(c, "Paolo Rossi", "confirmPlan");
        Assert.Equal("plan", denied.Phase);
        Assert.Equal("Only people working on this renewal can do this.", denied.Ui.Warn!.Render());
        var view = RenewalCaseEngine.View(c, Ctx(1, "Paolo Rossi"));
        Assert.Equal("collab", view.Permissions.Role);
        Assert.True(view.Page.Current.Buttons.Single(b => b.Act == "confirmPlan").Off);
    }

    [Fact]
    public void Edit_after_approval_returns_to_draft_as_a_new_version()
    {
        var c = AtFirstEmail();
        c = Act(c, Marta, "ui", new() { K = "to:0", V = "anna@salesforce.example" });
        c = Act(c, Marta, "requestAppr", new() { Eid = 0 });
        c = Act(c, Luca, "approve", new() { Eid = 0 });
        Assert.Equal(("approved", 1, 1), (c.Emails[0].State, c.Emails[0].Ver, c.Emails[0].ApprovedVer));
        c = Act(c, Marta, "edit", new() { Eid = 0 });
        c = Act(c, Marta, "ui", new() { K = "editBody", V = "Dear Salesforce team,\n\nShorter.\n\nKind regards,\nMarta" });
        c = Act(c, Marta, "saveEdit", new() { Eid = 0 });
        var e = c.Emails[0];
        Assert.Equal("draft", e.State);
        Assert.Equal(2, e.Ver);
        Assert.Null(e.ApprovedBy);
        Assert.Null(e.ApprovedVer);
        Assert.Equal("Email edited — previous approval invalidated", c.Decs[^1].Text.Render());
        Assert.Equal("First email · v2", Current(c, Marta).Kicker.Render());
    }

    [Fact]
    public void Copy_never_marks_sent_and_sending_needs_an_explicit_confirmation()
    {
        var c = AtFirstEmail();
        c = Act(c, Marta, "ui", new() { K = "to:0", V = "anna@salesforce.example" });
        var early = Act(c, Marta, "copy", new() { Eid = 0 });
        Assert.Null(early.Clip); // a draft cannot be copied
        c = Act(c, Luca, "approve", new() { Eid = 0 });
        c = Act(c, Marta, "copy", new() { Eid = 0 });
        Assert.Equal("copied", c.Emails[0].State);
        Assert.StartsWith("To: anna@salesforce.example\nSubject: ", c.Clip, StringComparison.Ordinal);
        c = Act(c, Marta, "copy", new() { Eid = 0 });
        Assert.Equal("copied", c.Emails[0].State);
        Assert.Null(c.Emails[0].Sent);
        c = Act(c, Marta, "sentOpen", new() { Eid = 0 });
        Assert.Equal("copied", c.Emails[0].State);
        c = Act(c, Marta, "sentOk", new() { Eid = 0 });
        Assert.Equal("sent", c.Emails[0].State);
        Assert.Equal(Marta, c.Emails[0].Sent!.By);
        Assert.Equal("08 Sep 2026 · 14:40", c.Emails[0].Sent!.At);
    }

    [Fact]
    public void The_recipient_is_required_and_never_invented()
    {
        var c = AtFirstEmail();
        Assert.Equal(string.Empty, c.Emails[0].To);
        var refused = Act(c, Marta, "requestAppr", new() { Eid = 0 });
        Assert.Equal("draft", refused.Emails[0].State);
        Assert.Equal("Add the recipient first — I never invent addresses.", refused.Ui.Warn!.Render());
        var notApproved = Act(c, Luca, "approve", new() { Eid = 0 });
        Assert.Equal("draft", notApproved.Emails[0].State);
        Assert.Equal("Add the recipient first — I never invent addresses.", notApproved.Ui.Warn!.Render());

        var card = Current(c, Marta);
        Assert.True(card.Email!.ToMissing);
        Assert.Equal("Use example address (demo)", card.Buttons[0].Label.Render()); // demo services only
        Assert.True(card.Buttons.Single(b => b.Act == "requestAppr").Off);
        Assert.Contains(RenewalCaseEngine.Todo(c, Ctx(1, Marta)), t => t.Kind == "recipient" && t.Title.Render() == "Add the recipient: first email");

        // With a production-like service (no example address) the shortcut disappears.
        var noExample = new CaseContext(Contract(1), Marta, Today, new NoExampleAddressServices());
        var prodCard = RenewalCaseEngine.View(c, noExample).Page.Current;
        Assert.DoesNotContain(prodCard.Buttons.Concat(prodCard.More), b => b.Act == "toEx");
        Assert.Equal(string.Empty, RenewalCaseEngine.Act(c, "toEx", new() { Eid = 0 }, noExample).Emails[0].To);
    }

    [Fact]
    public void Act_does_not_mutate_its_input_and_the_case_round_trips_as_camel_case_json()
    {
        var c = AtFirstEmail();
        var before = RenewalCaseJson.Serialize(c);
        _ = Act(c, Marta, "ui", new() { K = "to:0", V = "x@y.example" });
        Assert.Equal(before, RenewalCaseJson.Serialize(c));

        var done = Replay("docusign_alternatives").Last().Case;
        var json = RenewalCaseJson.Serialize(done);
        var node = JsonNode.Parse(json)!.AsObject();
        Assert.True(node.ContainsKey("planVer"));
        Assert.True(node["delegates"]!.AsObject().ContainsKey(Marta));
        Assert.Equal("{\"id\":\"Email v{ver} sent for approval\",\"vars\":{\"ver\":\"1\"}}", node["decs"]![0]!["text"]!.ToJsonString());
        Assert.Equal(json, RenewalCaseJson.Serialize(RenewalCaseJson.Deserialize(json)));
    }

    [Fact]
    public void Dates_and_feed_times_follow_the_prototype_formulas_from_ctx_today()
    {
        var other = new DateOnly(2026, 3, 1);
        var ctx = new CaseContext(Contract(1), Luca, other, FixtureCaseScriptServices.Instance);
        var c = RenewalCaseEngine.Create(1, ctx);
        Assert.Equal("01 Mar · 14:20", c.Feed[0].At);
        Assert.Equal("01 Mar · 14:21", c.Feed[1].At);
        Assert.Equal(other, c.Updated);
        var view = RenewalCaseEngine.View(c, ctx);
        Assert.Equal("231 days", view.Cards[0].Stats[0].V.Render()); // 01 Mar → 18 Oct 2026
        Assert.Equal("03 Mar 2026", CaseFormat.Date(CaseFormat.AddMonths(new DateOnly(2026, 1, 31), 1))); // JS setMonth overflow
    }

    [Fact]
    public void The_counter_price_is_the_one_the_supplier_offered_else_the_rule()
    {
        var ctx = new CaseContext(Contract(1), Luca, Today, FixtureCaseScriptServices.Instance);
        var c = RenewalCaseEngine.Create(1, ctx);
        var byRule = RenewalCaseEngine.CounterPrice(c, ctx, 24);

        c.OfferedPrices = new Dictionary<string, double> { ["24"] = 12.34 };

        Assert.Equal(12.34, RenewalCaseEngine.CounterPrice(c, ctx, 24));
        Assert.Equal(RenewalCaseEngine.CounterPrice(new RenewalCase(), ctx, 12), RenewalCaseEngine.CounterPrice(c, ctx, 12));
        Assert.NotEqual(12.34, byRule);
    }

    [Fact]
    public void A_read_supplier_reply_sets_the_counter_price_and_survives_the_next_steps()
    {
        var inner = FixtureCaseScriptServices.Instance;
        var c = AtFirstEmail();
        var reading = new CaseReplyAnalysis(
            [new CaseReplyRow { Label = CaseText.Of("Prezzo"), Said = CaseText.Of("CHF 11.11 per 24 mesi"), Plan = CaseText.Of("-10%"), State = "proposed" }],
            [],
            new Dictionary<int, double> { [24] = 11.11 });
        var read = new CaseContext(Contract(1), Marta, Today, new ReadCaseScriptServices(inner, reply: reading));

        // The supplier's reply is read: the case keeps the price it offered for 24 months ...
        c = RenewalCaseEngine.Act(c, "reply", new CaseActionPayload { Text = CaseText.Of("We offer 11.11 for 24 months.") }, read);
        Assert.Equal(11.11, c.OfferedPrices!["24"]);

        // ... and every later step (a plain context, no model any more) counters from it; the rule is the fallback only.
        var plain = Ctx(1, Marta);
        Assert.Equal(11.11, RenewalCaseEngine.CounterPrice(c, plain, 24));
        Assert.Equal(inner.CounterPrice(plain, 12), RenewalCaseEngine.CounterPrice(c, plain, 12));
        var after = RenewalCaseJson.Deserialize(RenewalCaseJson.Serialize(c));
        Assert.Equal(11.11, after.OfferedPrices!["24"]);
    }

    [Fact]
    public void Texts_are_messages_with_the_prototype_english_ids()
    {
        var c = AtFirstEmail();
        var plan = c.Decs.Single().Text;
        Assert.Equal("Plan v{planVer} confirmed", plan.Id);
        Assert.Equal("1", plan.Vars!["planVer"].Plain);
        Assert.Equal("Piano v1 confermato", plan.Render(id => id == "Plan v{planVer} confirmed" ? "Piano v{planVer} confermato" : null));
        var decision = CaseText.Message("Decision: {label} · {by}", ("label", CaseText.Message("Stay at 12 months — decline the discount tied to 24")), ("by", Marta));
        var json = System.Text.Json.JsonSerializer.Serialize(decision, RenewalCaseJson.Options);
        Assert.Equal("{\"id\":\"Decision: {label} · {by}\",\"vars\":{\"label\":{\"id\":\"Stay at 12 months — decline the discount tied to 24\"},\"by\":\"Marta Keller\"}}", json);
    }

    [Fact]
    public void The_plan_card_offers_each_secondary_path_once()
    {
        var c = RenewalCaseEngine.Create(1, Ctx(1, Luca), owner: Marta);
        c = Act(c, Marta, "ans", new() { Q = 0, Val = "keep" });
        c = Act(c, Marta, "ans", new() { Q = 1, Val = "12" });
        c = Act(c, Marta, "ans", new() { Q = 2, Val = "p10" });
        c = Act(c, Marta, "q3done");

        var card = Current(c, Marta);
        var acts = card.Buttons.Concat(card.More).Select(b => b.Act).ToList();

        Assert.Single(acts, a => a == "startNotice");
        Assert.Single(acts, a => a == "startAlt");
    }

    [Fact]
    public void A_member_known_only_by_email_is_shown_by_the_local_part_in_the_chat()
    {
        const string member = "pietro.nasuti@example.com";
        var c = RenewalCaseEngine.Create(1, Ctx(1, member), owner: member);
        c = Act(c, member, "ask", new() { Text = CaseText.Of("ricordami le date del contratto") });

        var bubble = RenewalCaseEngine.View(c, Ctx(1, member)).Page.Msgs[^1];

        Assert.False(bubble.Raffa);
        Assert.Equal("pietro.nasuti", bubble.By);
        Assert.Equal("Marta", RenewalCaseEngine.View(Act(c, Marta, "ask", new() { Text = CaseText.Of("e il preavviso?") }), Ctx(1, Marta)).Page.Msgs[^1].By);
    }

    private sealed class NoExampleAddressServices : ICaseScriptServices
    {
        private static readonly FixtureCaseScriptServices Inner = FixtureCaseScriptServices.Instance;

        public string SampleReply(RenewalCase c, CaseContext ctx, int round) => string.Empty;

        public CaseReplyAnalysis AnalyzeReply(RenewalCase c, CaseContext ctx, int rid) => Inner.AnalyzeReply(c, ctx, rid);

        public IReadOnlyList<CaseDraftRow> VerifyDraft(RenewalCase c, CaseContext ctx, int version) => Inner.VerifyDraft(c, ctx, version);

        public CaseSignedCheck CheckSigned(RenewalCase c, CaseContext ctx) => Inner.CheckSigned(c, ctx);

        public CasePipe StartExtraction(RenewalCase c, CaseContext ctx) => Inner.StartExtraction(c, ctx);

        public IReadOnlyList<string> ExampleVendors() => [];

        public CaseAltOffer VendorOffer(RenewalCase c, CaseContext ctx, string vendor, string by) => Inner.VendorOffer(c, ctx, vendor, by);

        public string? ExampleAddress(CaseEmail email, CaseContext ctx) => null;

        public double CounterPrice(CaseContext ctx, int termMonths) => Inner.CounterPrice(ctx, termMonths);

        public CaseFeatureSet Features(CaseContext ctx) => Inner.Features(ctx);
    }
}
