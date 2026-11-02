using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Raffa.Renewals.Application.Cases;
using Raffa.Renewals.Domain.Cases;
using Raffa.Renewals.Infrastructure;
using Raffa.SharedKernel;
using Raffa.SharedKernel.Cases;
using Raffa.SharedKernel.Renewals;
using Raffa.SharedKernel.Savings;
using Raffa.SharedKernel.Contracts;
using Raffa.SharedKernel.Tenancy;
using Raffa.SharedKernel.Text;

namespace Raffa.Renewals.Application.CaseWork;

/// <summary>The caller's rights on a case (BACKEND-SPEC §2.3), returned with every view.</summary>
public sealed record CaseCapabilities(bool CanWork, bool CanApprove, bool CanClose);

/// <summary>Everything a case response carries: the view, the rights, the concurrency version.</summary>
/// <param name="Clip">Text to put on the clipboard after <c>copy</c> (copy is never send).</param>
/// <param name="Tick">The client should post <c>tick</c> shortly (extraction progress).</param>
/// <param name="Demo">Scripted demo services are on (fixture mode): demo shortcuts may be shown.</param>
public sealed record CaseEnvelope(
    Guid CaseId,
    Guid ContractId,
    int Version,
    CaseView View,
    CaseCapabilities Capabilities,
    string? Clip,
    bool Tick,
    bool Closed,
    bool Demo);

public enum CaseResultKind
{
    Ok,
    NotFound,
    BadRequest,
    Conflict,
    Forbidden,
    Duplicate,
}

public sealed record CaseResult(CaseResultKind Kind, CaseEnvelope? Envelope = null, string? Error = null, Guid? ExistingMessageId = null)
{
    public static CaseResult Ok(CaseEnvelope e) => new(CaseResultKind.Ok, e);

    public static CaseResult NotFound(string error) => new(CaseResultKind.NotFound, Error: error);

    public static CaseResult Bad(string error) => new(CaseResultKind.BadRequest, Error: error);
}

/// <summary>
/// The AskRaffa 4.0 renewal case application service: opens cases (idempotently, one open case per
/// contract), runs the pure <see cref="RenewalCaseEngine"/> for every action under optimistic
/// concurrency, enforces the permission matrix (403 on approve/close by someone without the right),
/// writes the projections (emails, delegations, append-only events + audit, vendor messages with
/// hash dedup, reminders) and, when a case closes as renewed, the renewed terms and the saving result.
/// </summary>
public sealed partial class RenewalCaseService(
    RenewalsDbContext db,
    ITenantContext tenantContext,
    IContractFactsReader factsReader,
    ICaseMemberDirectory memberDirectory,
    ICaseScriptServices scriptServices,
    ICaseQuestionAnswerer answerer,
    IContractTermWriter termWriter,
    ISavingResultRecorder savingRecorder,
    ICaseObjectiveWriter objectiveWriter,
    IOpenQuoteDecisions quoteDecisions,
    IDocumentsNeedingReview documentsNeedingReview,
    IAuditWriter auditWriter,
    IClock clock,
    IContractSituationAssessor? situationAssessor = null,
    Microsoft.Extensions.Logging.ILogger<RenewalCaseService>? logger = null,
    ICaseAiReader? aiReader = null,
    Raffa.SharedKernel.Market.IMarketObservationSink? marketSink = null)
{
    public const int SnoozeDays = 3;

    private static readonly string[] PhaseKeys = ["avvio", "piano", "negoziazione", "accordo", "contratto_firma", "acquisizione"];

    private static readonly Dictionary<string, string> EventTypes = new(StringComparer.Ordinal)
    {
        ["confirmPlan"] = "plan_confirmed",
        ["requestAppr"] = "email_submitted",
        ["approve"] = "email_approved",
        ["copy"] = "email_copied",
        ["sentOk"] = "email_sent_confirmed",
        ["reply"] = "reply_added",
        ["decide"] = "decision_recorded",
        ["remOk"] = "reminder_set",
        ["remPick"] = "reminder_set",
        ["addDoc"] = "doc_added",
        ["markAnnex"] = "doc_checked",
        ["signDeclared"] = "signed_declared",
        ["sigOk"] = "signed_added",
        ["validate"] = "closed",
        ["nOut"] = "closed",
        ["altSel"] = "decision_recorded",
        ["transfer"] = "owner_changed",
        ["delegate"] = "delegation_changed",
        ["delegateClose"] = "delegation_changed",
        ["agreeOk"] = "decision_recorded",
    };

    public bool Demo => scriptServices is FixtureCaseScriptServices;

    private DateOnly Today => DateOnly.FromDateTime(clock.UtcNow.UtcDateTime);

    /* ---------------- open / read ---------------- */

    /// <summary><c>POST /api/renewals</c>: idempotent — an open case of the contract is returned (and visited), never a second one.</summary>
    public async Task<CaseResult> OpenAsync(TenantId tenantId, string? callerIdentity, Guid contractId, string? branch, Guid? ownerUserId, CancellationToken ct = default)
    {
        using var scope = tenantContext.BeginScope(tenantId);
        var members = await memberDirectory.ListAsync(tenantId, ct).ConfigureAwait(false);
        company ??= await memberDirectory.CompanyNameAsync(tenantId, ct).ConfigureAwait(false);
        var caller = CaseCaller.From(members, callerIdentity);
        var facts = await factsReader.GetAsync(tenantId, new EntityId(contractId), ct).ConfigureAwait(false);
        if (facts is null)
        {
            return CaseResult.NotFound("No validated contract with this id.");
        }

        var cf = CaseFactsMapper.ToCaseFacts(facts, Today);
        var existing = await OpenRecordAsync(tenantId, contractId, ct).ConfigureAwait(false);
        if (existing is null && cf.Cancel == DateOnly.MinValue)
        {
            // A renewal runs on the notice deadline; with no dates read there is nothing to plan against.
            return CaseResult.Bad("The dates of this contract have not been read yet: verify them before starting a renewal.");
        }

        if (existing is not null)
        {
            var c0 = RenewalCaseJson.Deserialize(existing.State);
            var ctx0 = Ctx(cf, caller.Name);
            if (branch == "notice" && c0.Notice is null)
            {
                c0 = RenewalCaseEngine.Act(c0, "startNotice", CaseActionPayload.None, ctx0);
                await AppendEventAsync(tenantId, existing, caller.Name, "startNotice", CaseActionPayload.None, null, ct).ConfigureAwait(false);
            }

            c0 = RenewalCaseEngine.Act(c0, "visit", CaseActionPayload.None, ctx0);
            await SaveStateAsync(existing, c0, cf, members, ct).ConfigureAwait(false);
            return CaseResult.Ok(Envelope(existing, c0, cf, caller.Name));
        }

        var owner = ownerUserId is { } oid ? members.FirstOrDefault(m => m.UserId == oid) : null;
        if (ownerUserId is not null && owner is null)
        {
            return CaseResult.Bad("The owner must be a member of this workspace.");
        }

        var approver = Approver(members) ?? caller.Name;
        var c = RenewalCaseEngine.Create(cf.Id, Ctx(cf, caller.Name), branch, owner?.Name ?? caller.Name, approver, members.Select(m => m.Name).ToList());
        var record = await InsertAsync(tenantId, contractId, c, cf, members, caller.Name, ct).ConfigureAwait(false);
        // An open case is an objective "to set" in Risultati from the moment it exists.
        await UpsertObjectiveAsync(tenantId, record, c, ct).ConfigureAwait(false);
        return CaseResult.Ok(Envelope(record, RenewalCaseJson.Deserialize(record.State), cf, caller.Name));
    }

    /// <summary><c>GET /api/renewals/{caseId}</c>; with <paramref name="visit"/> the caller's "last seen" is recorded (no version change).</summary>
    public async Task<CaseResult> GetAsync(TenantId tenantId, string? callerIdentity, Guid caseId, bool visit, CancellationToken ct = default)
    {
        using var scope = tenantContext.BeginScope(tenantId);
        var record = await db.RenewalCases.FirstOrDefaultAsync(x => x.Id == new EntityId(caseId), ct).ConfigureAwait(false);
        if (record is null)
        {
            return CaseResult.NotFound("No case with this id.");
        }

        var members = await memberDirectory.ListAsync(tenantId, ct).ConfigureAwait(false);
        company ??= await memberDirectory.CompanyNameAsync(tenantId, ct).ConfigureAwait(false);
        var caller = CaseCaller.From(members, callerIdentity);
        var cf = await FactsAsync(tenantId, record, ct).ConfigureAwait(false);
        if (cf is null)
        {
            return CaseResult.NotFound("The contract of this case is no longer available.");
        }

        var c = RenewalCaseJson.Deserialize(record.State);
        if (visit)
        {
            c = RenewalCaseEngine.Act(c, "visit", CaseActionPayload.None, Ctx(cf, caller.Name));
            record.State = RenewalCaseJson.Serialize(Clean(c));
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
        }

        return CaseResult.Ok(Envelope(record, c, cf, caller.Name));
    }

    /// <summary><c>GET /api/renewals/{caseId}/timeline</c>: the append-only events.</summary>
    public async Task<IReadOnlyList<CaseEventRecord>?> TimelineAsync(TenantId tenantId, Guid caseId, CancellationToken ct = default)
    {
        using var scope = tenantContext.BeginScope(tenantId);
        var id = new EntityId(caseId);
        if (!await db.RenewalCases.AnyAsync(x => x.Id == id, ct).ConfigureAwait(false))
        {
            return null;
        }

        return await db.CaseEvents.AsNoTracking().Where(x => x.CaseId == id).OrderBy(x => x.At).ToListAsync(ct).ConfigureAwait(false);
    }

    /// <summary><c>GET /api/renewals/{caseId}/vendor-messages</c>.</summary>
    public async Task<IReadOnlyList<CaseVendorMessageRecord>?> VendorMessagesAsync(TenantId tenantId, Guid caseId, CancellationToken ct = default)
    {
        using var scope = tenantContext.BeginScope(tenantId);
        var id = new EntityId(caseId);
        if (!await db.RenewalCases.AnyAsync(x => x.Id == id, ct).ConfigureAwait(false))
        {
            return null;
        }

        return await db.CaseVendorMessages.AsNoTracking().Where(x => x.CaseId == id).OrderBy(x => x.ReceivedAt).ToListAsync(ct).ConfigureAwait(false);
    }

    /* ---------------- act ---------------- */

    /// <summary>
    /// <c>POST /api/renewals/{caseId}/actions</c>: one engine action under optimistic concurrency.
    /// Stale <paramref name="version"/> → Conflict (with the current case); a refused right → Forbidden
    /// (nothing saved, the view carries the reason); the same supplier reply twice → Duplicate.
    /// </summary>
    public async Task<CaseResult> ActAsync(TenantId tenantId, string? callerIdentity, Guid caseId, string type, CaseActionPayload? payload, int? version, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(type) || !CaseActions.All.Contains(type))
        {
            return CaseResult.Bad($"Unknown case action '{type}'.");
        }

        return await RunAsync(tenantId, callerIdentity, caseId, version, ct, (c, ctx, _) => [(type, payload ?? CaseActionPayload.None)]).ConfigureAwait(false);
    }

    /// <summary>
    /// Several engine actions as one mutation (the granular email routes: PATCH = recipient/body edit,
    /// sent = open + text + confirm). All or nothing; one version bump.
    /// </summary>
    public Task<CaseResult> ActManyAsync(TenantId tenantId, string? callerIdentity, Guid caseId, int? version, Func<RenewalCase, IReadOnlyList<(string Type, CaseActionPayload Payload)>> steps, CancellationToken ct = default) =>
        RunAsync(tenantId, callerIdentity, caseId, version, ct, (c, _, _) => steps(c));

    /// <summary>
    /// The case composer (README §3): a free question is answered with its source, then Raffa brings
    /// the person back to the pending step. Commands (approva, copia, ho inviato, conferma piano) are
    /// routed by the engine's <c>say</c>.
    /// </summary>
    public async Task<CaseResult> AskAsync(TenantId tenantId, string? callerIdentity, Guid caseId, string text, int? version, string? lang = null, CancellationToken ct = default)
    {
        var q = (text ?? string.Empty).Trim();
        if (q.Length == 0)
        {
            return CaseResult.Bad("Write a question.");
        }

        CaseQuestionAnswer? answer = null;
        if (!CommandRegex().IsMatch(q))
        {
            using var scope = tenantContext.BeginScope(tenantId);
            var contractId = await db.RenewalCases.Where(x => x.Id == new EntityId(caseId)).Select(x => (Guid?)x.ContractId.Value).FirstOrDefaultAsync(ct).ConfigureAwait(false);
            if (contractId is null)
            {
                return CaseResult.NotFound("No case with this id.");
            }

            answer = await answerer.AnswerInCaseAsync(tenantId, callerIdentity, caseId, new EntityId(contractId.Value), q, lang, ct).ConfigureAwait(false);
        }

        return await RunAsync(tenantId, callerIdentity, caseId, version, ct, (c, ctx, _) =>
        {
            var list = new List<(string, CaseActionPayload)> { ("say", new CaseActionPayload { Text = CaseText.Of(q) }) };
            if (answer is not null)
            {
                list.Add(("answer", new CaseActionPayload
                {
                    Text = string.IsNullOrWhiteSpace(answer.Source)
                        ? CaseText.Of(answer.Text)
                        : CaseText.Message("{text}\n(Source: {src})", ("text", answer.Text), ("src", answer.Source)),
                    Resume = CaseText.Message("Back to the pending step — {next}", ("next", RenewalCaseEngine.View(c, ctx).Hdr.Next)),
                }));
            }

            return list;
        }).ConfigureAwait(false);
    }

    // The model's reading of the supplier reply of this request: the case uses it, and so does the market benchmark.
    private CaseReplyAnalysis? replyReading;

    /// <summary>
    /// The invisible flow: what the supplier answered feeds Raffa's shared market base. Anonymised by the sink,
    /// audited there, and never on the user's path: any failure is swallowed.
    /// </summary>
    private async Task ContributeToMarketAsync(TenantId tenantId, string actor, Guid caseId, CaseContractFacts cf, RenewalCase after, CancellationToken ct)
    {
        var reading = replyReading;
        replyReading = null;
        if (marketSink is null || reading is null || (reading.Prices is null && reading.Benchmark is null) || string.IsNullOrWhiteSpace(cf.Supplier))
        {
            return;
        }

        try
        {
            var (currency, country) = await memberDirectory.MarketProfileAsync(tenantId, ct).ConfigureAwait(false);
            var main = cf.Products.OrderByDescending(p => p.Qty * p.Price).FirstOrDefault();
            var b = reading.Benchmark;
            var replyNo = after.Reps.Count.ToString(System.Globalization.CultureInfo.InvariantCulture);
            // One observation per priced term; a reply with terms but no price still records those terms.
            var terms = reading.Prices is { Count: > 0 } priced ? priced.Select(kv => (Term: (int?)kv.Key, Price: (decimal?)(decimal)kv.Value)).ToList() : [(Term: b?.TermMonths, Price: (decimal?)null)];
            foreach (var (term, price) in terms)
            {
                await marketSink.RecordAsync(
                        tenantId, actor, $"{caseId:N}:{replyNo}",
                        new Raffa.SharedKernel.Market.SupplierReplyObservation(
                            cf.Supplier, string.IsNullOrWhiteSpace(cf.Category) ? null : cf.Category, country, currency,
                            main?.Name, string.IsNullOrWhiteSpace(main?.Sku) ? null : main!.Sku, main?.Unit, price,
                            term ?? b?.TermMonths, b?.DiscountPct, b?.UpliftCapPct, b?.NoticeDays, b?.PaymentTerms, clock.UtcNow),
                        ct)
                    .ConfigureAwait(false);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Invisible by design: the case never fails because of the benchmark.
            _ = ex;
        }
    }

    // A document the user uploaded for the next addDoc / altOffer step (its name and text), read by the model.
    private (string File, string Text)? pendingDocument;

    /// <summary>
    /// <c>POST /api/renewals/{caseId}/documents</c>: a contract draft, the signed contract or an alternative
    /// supplier's offer, uploaded as a file. The model reads its text (draft vs agreed, signed vs last draft,
    /// offer vs requirements) before the engine records it; without a reading the deterministic one stands.
    /// </summary>
    public Task<CaseResult> ActWithDocumentAsync(
        TenantId tenantId, string? callerIdentity, Guid caseId, string type, string fileName, string text, string? vendor, int? version, CancellationToken ct = default)
    {
        if (type is not ("addDoc" or "altOffer"))
        {
            return Task.FromResult(CaseResult.Bad("Only a contract document or an offer can be uploaded to a case."));
        }

        pendingDocument = string.IsNullOrWhiteSpace(text) ? null : (fileName, text);
        var payload = new CaseActionPayload { File = fileName, Vendor = type == "altOffer" ? vendor : null };
        return ActAsync(tenantId, callerIdentity, caseId, type, payload, version, ct);
    }

    private async Task<(CaseContext Ctx, Action<RenewalCase>? After)> ReadBeforeStepAsync(
        TenantId tenantId, RenewalCase c, CaseContractFacts cf, string type, CaseActionPayload p, CaseContext ctx, CancellationToken ct)
    {
        if (aiReader is null || scriptServices is not ProductionCaseScriptServices)
        {
            return (ctx, null);
        }

        var lang = Raffa.SharedKernel.Language.ResponseLanguage.Current == "en" ? "en" : "it";
        try
        {
            switch (type)
            {
                case "reply":
                {
                    var pasted = (p.Text?.Render() is { Length: > 0 } pt ? pt : c.Ui.ReplyText ?? string.Empty).Trim();
                    if (pasted.Length == 0)
                    {
                        break;
                    }

                    var reading = await aiReader.ReadReplyAsync(tenantId, c, cf, pasted, c.Round + 1, lang, ct).ConfigureAwait(false);
                    if (reading is not null)
                    {
                        replyReading = reading;
                    }

                    return reading is null ? (ctx, null) : (ctx with { Services = new ReadCaseScriptServices(scriptServices, reply: reading) }, null);
                }

                case "startAlt" when c.Alt is null:
                {
                    var features = await aiReader.ReadFeaturesAsync(tenantId, cf, lang, ct).ConfigureAwait(false);
                    return features is null ? (ctx, null) : (ctx with { Services = new ReadCaseScriptServices(scriptServices, features: features) }, null);
                }

                case "addDoc" when pendingDocument is { } doc && c.Agreed is not null:
                {
                    if (c.Phase is CasePhases.SignWait or CasePhases.SignConfirm or CasePhases.SignDeclared)
                    {
                        var signed = await aiReader.ReadSignedAsync(tenantId, c, cf, doc.File, doc.Text, lang, ct).ConfigureAwait(false);
                        return signed is null
                            ? (ctx, null)
                            : (ctx, next =>
                            {
                                if (next.Signed is { } s)
                                {
                                    s.CheckedPoints = signed.Points;
                                    s.Differences = signed.Differences;
                                }
                            });
                    }

                    var rows = await aiReader.ReadDraftAsync(tenantId, c, cf, doc.File, doc.Text, lang, ct).ConfigureAwait(false);
                    return rows is null ? (ctx, null) : (ctx with { Services = new ReadCaseScriptServices(scriptServices, draftRows: rows) }, null);
                }

                case "altOffer" when pendingDocument is { } doc && !string.IsNullOrWhiteSpace(p.Vendor):
                {
                    var by = c.Owner;
                    var offer = await aiReader.ReadOfferAsync(tenantId, c, cf, p.Vendor!, doc.Text, by, lang, ct).ConfigureAwait(false);
                    return offer is null ? (ctx, null) : (ctx with { Services = new ReadCaseScriptServices(scriptServices, offer: offer) }, null);
                }
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException && logger is not null)
        {
            Microsoft.Extensions.Logging.LoggerExtensions.LogWarning(logger, ex, "Case reading failed for action {Type}; the deterministic reading stands.", type);
        }
        catch (Exception ex) when (ex is not OperationCanceledException && logger is null)
        {
            _ = ex;
        }

        return (ctx, null);
    }

    private async Task<CaseResult> RunAsync(
        TenantId tenantId,
        string? callerIdentity,
        Guid caseId,
        int? version,
        CancellationToken ct,
        Func<RenewalCase, CaseContext, CaseCaller, IReadOnlyList<(string Type, CaseActionPayload Payload)>> plan)
    {
        using var scope = tenantContext.BeginScope(tenantId);
        var record = await db.RenewalCases.FirstOrDefaultAsync(x => x.Id == new EntityId(caseId), ct).ConfigureAwait(false);
        if (record is null)
        {
            return CaseResult.NotFound("No case with this id.");
        }

        var members = await memberDirectory.ListAsync(tenantId, ct).ConfigureAwait(false);
        company ??= await memberDirectory.CompanyNameAsync(tenantId, ct).ConfigureAwait(false);
        var caller = CaseCaller.From(members, callerIdentity);
        var cf = await FactsAsync(tenantId, record, ct).ConfigureAwait(false);
        if (cf is null)
        {
            return CaseResult.NotFound("The contract of this case is no longer available.");
        }

        var before = RenewalCaseJson.Deserialize(record.State);
        if (version is { } v && v != record.Version)
        {
            return new CaseResult(CaseResultKind.Conflict, Envelope(record, before, cf, caller.Name), "The case changed since you loaded it. Your text is kept; review the current version.");
        }

        var ctx = Ctx(cf, caller.Name);
        var c = before;
        var steps = plan(before, ctx, caller);
        var events = new List<(string Type, CaseActionPayload P, int? Eid)>();
        foreach (var (type, p) in steps)
        {
            if (type == "reply")
            {
                var pasted = (p.Text?.Render() is { Length: > 0 } pt ? pt : c.Ui.ReplyText ?? string.Empty).Trim();
                if (pasted.Length > 0)
                {
                    var hash = Hash(pasted);
                    var dup = await db.CaseVendorMessages.Where(x => x.CaseId == record.Id && x.ContentHash == hash)
                        .Select(x => new { x.Id, x.ReceivedAt }).FirstOrDefaultAsync(ct).ConfigureAwait(false);
                    if (dup is not null)
                    {
                        var shown = c.DeepClone();
                        shown.Ui.Warn = CaseText.Message("This reply is already in the case (added on {date}) — I did not add it twice.", ("date", CaseFormat.Date(DateOnly.FromDateTime(dup.ReceivedAt.UtcDateTime))));
                        return new CaseResult(CaseResultKind.Duplicate, Envelope(record, shown, cf, caller.Name), "The same supplier reply is already in this case.", dup.Id.Value);
                    }
                }
            }

            // Production: the model reads the supplier reply (and the functions an alternative must cover)
            // before the pure engine runs; any failure leaves the deterministic reading in place.
            var (stepCtx, afterStep) = await ReadBeforeStepAsync(tenantId, c, cf, type, p, ctx, ct).ConfigureAwait(false);
            var next = RenewalCaseEngine.Act(c, type, p, stepCtx);
            afterStep?.Invoke(next);
            if (RenewalCaseEngine.IsPermissionDenial(next))
            {
                return new CaseResult(CaseResultKind.Forbidden, Envelope(record, next, cf, caller.Name), next.Ui.Warn?.Render());
            }

            if (type == "reply" && next.Reps.Count > c.Reps.Count)
            {
                var rep = next.Reps[^1];
                db.CaseVendorMessages.Add(new CaseVendorMessageRecord
                {
                    TenantId = tenantId,
                    CaseId = record.Id,
                    Direction = "in",
                    ReceivedAt = clock.UtcNow,
                    PastedText = rep.Text,
                    ContentHash = Hash(rep.Text),
                    ThreadKey = next.Thread,
                    ParsedJson = JsonSerializer.Serialize(rep.Rows, RenewalCaseJson.Options),
                    AnalysedByModel = Demo ? "fixture-script" : replyReading is not null ? "case-reply-reader" : "not-available",
                    PromptVersion = replyReading is not null ? "case-reply-v2" : null,
                    InputHash = Hash(rep.Text),
                });
            }

            if (!CaseActions.Passive.Contains(type) || type == "sampleReply")
            {
                events.Add((type, p, p.Eid));
            }

            c = next;
        }

        var clip = c.Clip;
        var tick = c.Tick;
        foreach (var (type, p, eid) in events.Where(e => !CaseActions.Passive.Contains(e.Type)))
        {
            var invalidated = type == "saveEdit" && eid is { } i && i < before.Emails.Count && before.Emails[i].State != EmailStates.Draft;
            await AppendEventAsync(tenantId, record, caller.Name, invalidated ? "email_edited_invalidated" : type, p, eid, ct).ConfigureAwait(false);
        }

        try
        {
            await SaveStateAsync(record, c, cf, members, ct).ConfigureAwait(false);
        }
        catch (DbUpdateConcurrencyException)
        {
            db.ChangeTracker.Clear();
            var fresh = await db.RenewalCases.AsNoTracking().FirstAsync(x => x.Id == record.Id, ct).ConfigureAwait(false);
            return new CaseResult(CaseResultKind.Conflict, Envelope(fresh, RenewalCaseJson.Deserialize(fresh.State), cf, caller.Name), "The case changed while you were working on it.");
        }

        if (c.Outcome is not null && before.Outcome is null)
        {
            await WriteOutcomeAsync(tenantId, record, c, cf, ct).ConfigureAwait(false);
        }

        await ContributeToMarketAsync(tenantId, caller.Name, record.Id.Value, cf, c, ct).ConfigureAwait(false);
        var env = Envelope(record, c, cf, caller.Name) with { Clip = clip, Tick = tick };
        return CaseResult.Ok(env);
    }

    /* ---------------- persistence helpers ---------------- */

    // The workspace's company name for this request (loaded beside the members), signing the case emails.
    private string? company;

    private CaseContext Ctx(CaseContractFacts cf, string viewer) =>
        new(string.IsNullOrEmpty(cf.Company) && !string.IsNullOrWhiteSpace(company) ? cf with { Company = company! } : cf, viewer, Today, scriptServices);

    internal static string? Approver(IReadOnlyList<CaseMember> members) =>
        members.Where(m => m.IsAdmin).OrderBy(m => m.Name, StringComparer.Ordinal).Select(m => m.Name).FirstOrDefault();

    private Task<RenewalCaseRecord?> OpenRecordAsync(TenantId tenantId, Guid contractId, CancellationToken ct) =>
        db.RenewalCases.FirstOrDefaultAsync(x => x.TenantId == tenantId && x.ContractId == new EntityId(contractId) && x.ClosedAt == null, ct);

    private async Task<CaseContractFacts?> FactsAsync(TenantId tenantId, RenewalCaseRecord record, CancellationToken ct)
    {
        var facts = await factsReader.GetAsync(tenantId, record.ContractId, ct).ConfigureAwait(false);
        return facts is null ? null : CaseFactsMapper.ToCaseFacts(facts, Today);
    }

    /// <summary>Creates the record of a new case (also used by the fixture seeder).</summary>
    internal async Task<RenewalCaseRecord> InsertAsync(TenantId tenantId, Guid contractId, RenewalCase c, CaseContractFacts cf, IReadOnlyList<CaseMember> members, string actor, CancellationToken ct)
    {
        var now = clock.UtcNow;
        var record = new RenewalCaseRecord
        {
            TenantId = tenantId,
            ContractId = new EntityId(contractId),
            Phase = PhaseKeys[0],
            Step = c.Phase,
            Status = "open",
            Branch = "renew",
            State = "{}",
            Version = 0,
            NoticeDeadline = cf.Cancel,
            ContractEnd = cf.End,
            OpenedAt = now,
            UpdatedAt = now,
        };
        db.RenewalCases.Add(record);
        await AppendEventAsync(tenantId, record, actor, "case_opened", CaseActionPayload.None, null, ct).ConfigureAwait(false);
        try
        {
            await SaveStateAsync(record, c, cf, members, ct).ConfigureAwait(false);
            return record;
        }
        catch (DbUpdateException)
        {
            // Two people opened the same case at once: the unique open-case index keeps one.
            db.ChangeTracker.Clear();
            return await OpenRecordAsync(tenantId, contractId, ct).ConfigureAwait(false)
                   ?? throw new InvalidOperationException("The case could not be opened.");
        }
    }

    /// <summary>Writes the state, the snapshot columns and the projections, and bumps the version.</summary>
    internal async Task SaveStateAsync(RenewalCaseRecord record, RenewalCase c, CaseContractFacts cf, IReadOnlyList<CaseMember> members, CancellationToken ct)
    {
        var clean = Clean(c);
        var now = clock.UtcNow;
        var stage = RenewalCaseEngine.Stage(clean);
        record.State = RenewalCaseJson.Serialize(clean);
        record.Version++;
        record.UpdatedAt = now;
        record.Step = clean.Phase;
        record.Phase = PhaseKeys[Math.Clamp(stage, 0, PhaseKeys.Length - 1)];
        record.Branch = clean.Alt is not null ? "alternatives" : clean.Notice is not null ? "notice" : "renew";
        record.Status = clean.Outcome is not null ? "closed"
            : clean.Phase is CasePhases.Waiting or CasePhases.WaitingRem or CasePhases.DocWait ? "waiting_supplier"
            : clean.Emails.Any(e => e.State == EmailStates.ToApprove) || clean.Phase == CasePhases.SignConfirm ? "waiting_internal"
            : "open";
        record.OwnerUserId = members.FirstOrDefault(m => m.Name == clean.Owner)?.UserId;
        record.ApproverUserId = members.FirstOrDefault(m => m.Name == clean.Approver)?.UserId;
        if (clean.Outcome is { } o && record.ClosedAt is null)
        {
            record.ClosedAt = now;
            record.OutcomeKind = o.Kind;
            record.OutcomeJson = JsonSerializer.Serialize(o, RenewalCaseJson.Options);
        }

        await ProjectEmailsAsync(record, clean, now, ct).ConfigureAwait(false);
        await ProjectDelegationsAsync(record, clean, members, now, ct).ConfigureAwait(false);
        await ProjectRemindersAsync(record, clean, members, now, ct).ConfigureAwait(false);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        await UpsertObjectiveAsync(record.TenantId, record, clean, ct).ConfigureAwait(false);
    }

    private static RenewalCase Clean(RenewalCase c)
    {
        if (c.Clip is null && !c.Tick)
        {
            return c;
        }

        var x = c.DeepClone();
        x.Clip = null;
        x.Tick = false;
        return x;
    }

    private async Task ProjectEmailsAsync(RenewalCaseRecord record, RenewalCase c, DateTimeOffset now, CancellationToken ct)
    {
        var rows = await db.CaseEmails.Where(x => x.CaseId == record.Id).ToListAsync(ct).ConfigureAwait(false);
        for (var i = 0; i < c.Emails.Count; i++)
        {
            var e = c.Emails[i];
            var row = rows.FirstOrDefault(x => x.EmailIndex == i);
            if (row is null)
            {
                row = new CaseEmailRecord
                {
                    TenantId = record.TenantId,
                    CaseId = record.Id,
                    EmailIndex = i,
                    Kind = e.Kind,
                    State = e.State,
                    Subject = e.Subject,
                    Body = e.Body,
                    CreatedAt = now,
                    UpdatedAt = now,
                };
                db.CaseEmails.Add(row);
            }

            if (row.State != e.State || row.Body != e.Body || row.ToAddress != NullIfEmpty(e.To) || row.Version != e.Ver)
            {
                row.UpdatedAt = now;
            }

            if (e.State == EmailStates.Approved && row.State != EmailStates.Approved)
            {
                row.ApprovedAt = now;
            }

            if (e.State == EmailStates.Copied && row.CopiedAt is null)
            {
                row.CopiedAt = now;
            }

            row.Kind = e.Kind;
            row.Version = e.Ver;
            row.State = e.State;
            row.ToAddress = NullIfEmpty(e.To);
            row.Subject = e.Subject;
            row.Body = e.Body;
            row.AttachmentsJson = JsonSerializer.Serialize(e.Attach);
            row.ApprovedVersion = e.ApprovedVer;
            row.ApprovedBy = e.ApprovedBy;
            if (e.ApprovedVer is null)
            {
                row.ApprovedAt = null;
            }

            if (e.Sent is { } s)
            {
                row.SentBy = s.By;
                row.SentAt ??= now;
                row.SentBody = s.Text;
            }
        }
    }

    private async Task ProjectDelegationsAsync(RenewalCaseRecord record, RenewalCase c, IReadOnlyList<CaseMember> members, DateTimeOffset now, CancellationToken ct)
    {
        var rows = await db.CaseDelegations.Where(x => x.CaseId == record.Id && x.RevokedAt == null).ToListAsync(ct).ConfigureAwait(false);
        foreach (var (name, d) in c.Delegates)
        {
            var row = rows.FirstOrDefault(x => x.UserName == name);
            if (row is null)
            {
                db.CaseDelegations.Add(new CaseDelegationRecord
                {
                    TenantId = record.TenantId,
                    CaseId = record.Id,
                    UserId = members.FirstOrDefault(m => m.Name == name)?.UserId,
                    UserName = name,
                    CanApprove = true,
                    CanClose = d.Close,
                    GrantedBy = c.Approver,
                    GrantedAt = now,
                });
            }
            else
            {
                row.CanClose = d.Close;
            }
        }

        foreach (var row in rows.Where(r => !c.Delegates.ContainsKey(r.UserName)))
        {
            row.RevokedAt = now;
        }
    }

    private async Task ProjectRemindersAsync(RenewalCaseRecord record, RenewalCase c, IReadOnlyList<CaseMember> members, DateTimeOffset now, CancellationToken ct)
    {
        var caseGuid = record.Id.Value;
        var rows = await db.UserReminders.Where(x => x.CaseId == caseGuid).ToListAsync(ct).ConfigureAwait(false);
        for (var i = 0; i < c.Rems.Count; i++)
        {
            var r = c.Rems[i];
            var row = rows.FirstOrDefault(x => x.ReminderIndex == i);
            if (row is null)
            {
                row = new UserReminderRecord
                {
                    TenantId = record.TenantId,
                    CaseId = caseGuid,
                    ReminderIndex = i,
                    UserName = c.Owner,
                    Kind = c.Agreed is null ? "check_reply" : "check_contract",
                    DueDate = r.Date,
                    State = r.State,
                    CreatedAt = now,
                };
                db.UserReminders.Add(row);
            }

            row.UserName = c.Owner;
            row.UserId = members.FirstOrDefault(m => m.Name == c.Owner)?.UserId;
            row.DueDate = r.Date;
            row.State = r.State;
        }
    }

    private async Task AppendEventAsync(TenantId tenantId, RenewalCaseRecord record, string actor, string action, CaseActionPayload p, int? eid, CancellationToken ct)
    {
        var type = EventTypes.TryGetValue(action, out var t) ? t : action;
        var now = clock.UtcNow;
        var payload = JsonSerializer.Serialize(new { action, payload = p }, RenewalCaseJson.Options);
        db.CaseEvents.Add(new CaseEventRecord
        {
            TenantId = tenantId,
            CaseId = record.Id,
            At = now,
            Actor = actor,
            Type = type,
            PayloadJson = payload,
            EmailIndex = eid,
        });
        await auditWriter.WriteAsync(new AuditEntry(tenantId, actor, "renewal_case." + type, "renewal_case", record.Id.Value.ToString(), now, action), ct).ConfigureAwait(false);
    }

    private async Task WriteOutcomeAsync(TenantId tenantId, RenewalCaseRecord record, RenewalCase c, CaseContractFacts cf, CancellationToken ct)
    {
        var o = c.Outcome!;
        if (o.Kind != "renewed")
        {
            return;
        }

        // BACKEND-SPEC §2.9: the renewed terms become the contract's current version (Contratti,
        // Rinnovi, Oggi and Risultati read the new dates through IContractFactsReader).
        await termWriter.WriteCurrentAsync(tenantId, new ContractTermVersionInput(
            record.ContractId.Value,
            null,
            o.Start ?? cf.End.AddDays(1),
            o.End,
            cf.Notice,
            o.Spend is { } s ? (decimal)s : null,
            "CHF",
            cf.Auto,
            null,
            record.Id.Value), ct).ConfigureAwait(false);

        // A saving counts only with the signed contract: the price result (never an estimate).
        if (o.Delta is { } delta && delta > 0)
        {
            var p = cf.Products.FirstOrDefault();
            await savingRecorder.RecordAsync(tenantId, new SavingResultRecord(
                record.ContractId.Value,
                record.Id.Value,
                cf.Supplier,
                CaseFactsMapper.ShortName(cf.Name),
                null,
                "price",
                (decimal)delta,
                "CHF",
                o.Price?.Render() ?? string.Empty,
                "previous contract price, same quantities",
                p is null ? null : (decimal)p.Price,
                c.Agreed is { } ag ? (decimal)ag.Price : null,
                p is null ? null : (decimal)p.Qty,
                "(old − new unit price) × contracted quantity",
                o.Start,
                o.End,
                o.Start is { } st ? "new term from " + CaseFormat.Date(st) : string.Empty,
                null,
                o.File ?? string.Empty,
                c.Owner,
                o.When ?? Today,
                c.Owner), ct).ConfigureAwait(false);
        }
    }

    /// <summary>Keeps the case objective (Risultati "Obiettivi nelle pratiche") in step with the case.</summary>
    private async Task UpsertObjectiveAsync(TenantId tenantId, RenewalCaseRecord record, RenewalCase c, CancellationToken ct)
    {
        var kind = c.Goals.Target;
        decimal? pct = kind switch { "p5" => 5m, "p10" => 10m, "budget" => 10m, _ => null };
        await objectiveWriter.UpsertAsync(tenantId, new CaseObjectiveInfo(
            record.Id.Value, record.ContractId.Value, c.Owner, kind, pct, null, c.Goals.TargetText, c.Outcome?.Kind), ct).ConfigureAwait(false);
    }

    private CaseEnvelope Envelope(RenewalCaseRecord record, RenewalCase c, CaseContractFacts cf, string viewer)
    {
        var view = RenewalCaseEngine.View(c, Ctx(cf, viewer));
        return new CaseEnvelope(
            record.Id.Value,
            record.ContractId.Value,
            record.Version,
            view,
            new CaseCapabilities(CaseRules.CanWork(c, viewer), CaseRules.CanApprove(c, viewer), CaseRules.CanClose(c, viewer)),
            null,
            false,
            record.ClosedAt is not null,
            Demo);
    }

    private static string? NullIfEmpty(string? s) => string.IsNullOrEmpty(s) ? null : s;

    internal static string Hash(string text)
    {
        var normalized = string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(normalized)));
    }

    [System.Text.RegularExpressions.GeneratedRegex("^(approva|copia|ho inviato|conferma piano|approve|copy|i sent|confirm plan)", System.Text.RegularExpressions.RegexOptions.IgnoreCase)]
    private static partial System.Text.RegularExpressions.Regex CommandRegex();
}
