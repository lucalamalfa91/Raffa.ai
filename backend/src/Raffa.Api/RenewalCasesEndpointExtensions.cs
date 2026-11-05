using Raffa.Api.Infrastructure;
using Raffa.Chat.Application.Conversations;
using Raffa.Renewals.Application.CaseWork;
using Raffa.Renewals.Application.Cases;
using Raffa.SharedKernel;
using Raffa.SharedKernel.Text;

namespace Raffa.Api;

/// <summary>
/// AskRaffa 4.0 renewal case, Today feed and reminders (BACKEND-SPEC §4 Today/Renewals/Emails/Vendor/
/// Reminders; slices 2+3). Every case response is <see cref="CaseEnvelope"/>: the engine's view, the
/// caller's capabilities and the version to quote on the next mutation. Errors keep the current case:
/// 409 on a stale version (the client keeps the user's unsaved text), 403 when the caller lacks the right
/// (approve/close by a non-approver — the view carries the reason), 409 <c>duplicate_reply</c> when the
/// same supplier reply is pasted twice.
/// </summary>
public static class RenewalCasesEndpointExtensions
{
    /// <param name="ConversationId">The chat the renewal was started from: its levers travel into the case and the case continues that conversation.</param>
    public sealed record OpenCaseRequest(Guid ContractId, string? Branch, Guid? OwnerUserId, Guid? ConversationId = null);

    public sealed record CaseActionRequest(string Type, CaseActionPayload? Payload, int? Version);

    public sealed record CaseAskRequest(string Text, int? Version, string? Lang);

    public sealed record CaseVersionRequest(int? Version);

    public sealed record EmailPatchRequest(string? To, string? Body, int? Version);

    public sealed record EmailSentRequest(string? SentBody, int? Version);

    public sealed record VendorMessageRequest(string Text, int? Version);

    public sealed record DelegationRequest(bool? CanClose, int? Version);

    public sealed record TransferRequest(Guid OwnerUserId, int? Version);

    public sealed record ReminderRequest(Guid? CaseId, DateOnly DueDate);

    public sealed record ReminderPatchRequest(string State);

    public sealed record CaseErrorBody(string Code, string? Error, CaseEnvelope? Current, Guid? ExistingMessageId);

    private static readonly string[] CloseActions = ["sigOk", "validate", "nOut", "altNew"];

    public static IEndpointRouteBuilder MapRenewalCaseEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/api/renewals", OpenAsync);
        endpoints.MapGet("/api/renewals/{caseId:guid}", GetAsync);
        endpoints.MapPost("/api/renewals/{caseId:guid}/actions", ActAsync);
        endpoints.MapPost("/api/renewals/{caseId:guid}/ask", AskAsync);
        endpoints.MapPost("/api/renewals/{caseId:guid}/documents", DocumentAsync);
        endpoints.MapGet("/api/renewals/{caseId:guid}/timeline", TimelineAsync);
        endpoints.MapPatch("/api/renewals/{caseId:guid}/emails/{eid:int}", PatchEmailAsync);
        endpoints.MapPost("/api/renewals/{caseId:guid}/emails/{eid:int}/submit", (Guid caseId, int eid, CaseVersionRequest body, HttpRequest r, RenewalCaseService s, ICallerContext cc, CancellationToken ct) =>
            OneAsync(caseId, "requestAppr", new CaseActionPayload { Eid = eid }, body.Version, r, s, cc, ct));
        endpoints.MapPost("/api/renewals/{caseId:guid}/emails/{eid:int}/approve", (Guid caseId, int eid, CaseVersionRequest body, HttpRequest r, RenewalCaseService s, ICallerContext cc, CancellationToken ct) =>
            OneAsync(caseId, "approve", new CaseActionPayload { Eid = eid }, body.Version, r, s, cc, ct));
        endpoints.MapPost("/api/renewals/{caseId:guid}/emails/{eid:int}/copy", (Guid caseId, int eid, CaseVersionRequest body, HttpRequest r, RenewalCaseService s, ICallerContext cc, CancellationToken ct) =>
            OneAsync(caseId, "copy", new CaseActionPayload { Eid = eid }, body.Version, r, s, cc, ct));
        endpoints.MapPost("/api/renewals/{caseId:guid}/emails/{eid:int}/sent", SentAsync);
        endpoints.MapPut("/api/renewals/{caseId:guid}/delegations/{userId:guid}", PutDelegationAsync);
        endpoints.MapDelete("/api/renewals/{caseId:guid}/delegations/{userId:guid}", DeleteDelegationAsync);
        endpoints.MapPost("/api/renewals/{caseId:guid}/transfer", TransferAsync);
        endpoints.MapPost("/api/renewals/{caseId:guid}/close", CloseAsync);
        endpoints.MapPost("/api/renewals/{caseId:guid}/vendor-messages", PostVendorMessageAsync);
        endpoints.MapGet("/api/renewals/{caseId:guid}/vendor-messages", GetVendorMessagesAsync);

        endpoints.MapGet("/api/today", TodayAsync);
        endpoints.MapPost("/api/today/{key}/snooze", (string key, HttpRequest r, RenewalCaseService s, ICallerContext cc, CancellationToken ct) => SnoozeAsync(key, true, r, s, cc, ct));
        endpoints.MapDelete("/api/today/{key}/snooze", (string key, HttpRequest r, RenewalCaseService s, ICallerContext cc, CancellationToken ct) => SnoozeAsync(key, false, r, s, cc, ct));

        endpoints.MapGet("/api/reminders", GetRemindersAsync);
        endpoints.MapPost("/api/reminders", PostReminderAsync);
        endpoints.MapPatch("/api/reminders/{id:guid}", PatchReminderAsync);
        return endpoints;
    }

    /// <summary><c>GET /api/renewals?view=v4</c>: called by <see cref="RenewalsEndpointExtensions"/>.</summary>
    internal static async Task<IResult> BoardAsync(HttpRequest request, RenewalCaseService service, ICallerContext callerContext, CancellationToken ct)
    {
        var caller = await callerContext.ResolveTenantAsync(request, ct).ConfigureAwait(false);
        if (caller.Failure is not null)
        {
            return caller.Failure;
        }

        using var _ = caller.Scope;
        return Results.Ok(await service.BoardAsync(caller.TenantId, caller.Identity, ct).ConfigureAwait(false));
    }

    private static IResult Map(CaseResult r) => r.Kind switch
    {
        CaseResultKind.Ok => Results.Ok(r.Envelope),
        CaseResultKind.NotFound => Results.NotFound(new CaseErrorBody("not_found", r.Error, null, null)),
        CaseResultKind.BadRequest => Results.BadRequest(new CaseErrorBody("bad_request", r.Error, null, null)),
        CaseResultKind.Conflict => Results.Conflict(new CaseErrorBody("stale_version", r.Error, r.Envelope, null)),
        CaseResultKind.Duplicate => Results.Conflict(new CaseErrorBody("duplicate_reply", r.Error, r.Envelope, r.ExistingMessageId)),
        CaseResultKind.Forbidden => Results.Json(new CaseErrorBody("forbidden", r.Error, r.Envelope, null), statusCode: StatusCodes.Status403Forbidden),
        _ => Results.StatusCode(StatusCodes.Status500InternalServerError),
    };

    private static async Task<IResult> WithCaller(HttpRequest request, ICallerContext callerContext, CancellationToken ct, Func<CallerTenantResult, Task<IResult>> run)
    {
        var caller = await callerContext.ResolveTenantAsync(request, ct).ConfigureAwait(false);
        if (caller.Failure is not null)
        {
            return caller.Failure;
        }

        using var _ = caller.Scope;
        return await run(caller).ConfigureAwait(false);
    }

    private static Task<IResult> OpenAsync(OpenCaseRequest body, HttpRequest request, RenewalCaseService service, ConversationService conversations, ICallerContext callerContext, CancellationToken ct) =>
        WithCaller(request, callerContext, ct, async caller =>
        {
            if (body.ContractId == Guid.Empty)
            {
                return Results.BadRequest(new CaseErrorBody("bad_request", "contractId is required.", null, null));
            }

            if (body.Branch is not (null or "renew" or "notice"))
            {
                return Results.BadRequest(new CaseErrorBody("bad_request", "branch must be renew or notice.", null, null));
            }

            // Started from a chat: what was worked out there (the levers) goes into the case, read from the stored conversation.
            ConversationDetailResult? chat = null;
            if (body.ConversationId is { } chatId && chatId != Guid.Empty && !string.IsNullOrWhiteSpace(caller.Identity))
            {
                chat = await conversations.GetAsync(caller.TenantId, caller.Identity!, new EntityId(chatId), ct).ConfigureAwait(false);
            }

            var result = await service.OpenAsync(
                caller.TenantId, caller.Identity, body.ContractId, body.Branch == "renew" ? null : body.Branch, body.OwnerUserId,
                chat is null ? null : ChatBrief.From(chat), ct).ConfigureAwait(false);
            if (chat is not null && result.Envelope is { } opened)
            {
                // The case composer continues this conversation, so it never asks again what the chat already settled.
                await conversations.AttachToCaseAsync(caller.TenantId, caller.Identity!, chat.ConversationId, new EntityId(opened.CaseId), new EntityId(body.ContractId), ct).ConfigureAwait(false);
            }

            return Map(result);
        });

    private static Task<IResult> GetAsync(Guid caseId, bool? visit, HttpRequest request, RenewalCaseService service, ICallerContext callerContext, CancellationToken ct) =>
        WithCaller(request, callerContext, ct, async caller => Map(await service.GetAsync(caller.TenantId, caller.Identity, caseId, visit == true, ct).ConfigureAwait(false)));

    private static Task<IResult> ActAsync(Guid caseId, CaseActionRequest body, HttpRequest request, RenewalCaseService service, ICallerContext callerContext, CancellationToken ct) =>
        WithCaller(request, callerContext, ct, async caller => Map(await service.ActAsync(caller.TenantId, caller.Identity, caseId, body.Type, body.Payload, body.Version, ct).ConfigureAwait(false)));

    private static Task<IResult> OneAsync(Guid caseId, string type, CaseActionPayload payload, int? version, HttpRequest request, RenewalCaseService service, ICallerContext callerContext, CancellationToken ct) =>
        WithCaller(request, callerContext, ct, async caller => Map(await service.ActAsync(caller.TenantId, caller.Identity, caseId, type, payload, version, ct).ConfigureAwait(false)));

    private static Task<IResult> AskAsync(Guid caseId, CaseAskRequest body, HttpRequest request, RenewalCaseService service, ICallerContext callerContext, CancellationToken ct) =>
        WithCaller(request, callerContext, ct, async caller => Map(await service.AskAsync(caller.TenantId, caller.Identity, caseId, body.Text, body.Version, body.Lang, ct).ConfigureAwait(false)));

    private const long MaxCaseDocumentBytes = 20L * 1024 * 1024;

    /// <summary>A file added to the case (multipart: <c>file</c>, <c>type</c> = addDoc | altOffer, optional
    /// <c>vendor</c>, <c>version</c>, <c>lang</c>): a contract draft, the signed contract or an alternative
    /// supplier's offer. Raffa reads its text; the case records the reading.</summary>
    private static Task<IResult> DocumentAsync(
        Guid caseId, HttpRequest request, RenewalCaseService service, Raffa.AiFlows.Shared.Parsing.HybridDocumentParsingService parser,
        Raffa.Documents.Contracts.Application.Admission.DocumentAdmissionOptions options, ICallerContext callerContext, CancellationToken ct) =>
        WithCaller(request, callerContext, ct, async caller =>
        {
            var (form, file, refusal) = await DocumentsEndpointExtensions.ReadUploadFormAsync(request, options, ct).ConfigureAwait(false);
            if (refusal is not null)
            {
                return refusal;
            }

            if (file!.Length > MaxCaseDocumentBytes)
            {
                return Results.Json(new CaseErrorBody("too_large", "Raffa reads documents up to 20 MB.", null, null), statusCode: StatusCodes.Status413PayloadTooLarge);
            }

            var type = form!["type"].ToString();
            if (type is not ("addDoc" or "altOffer"))
            {
                return Results.BadRequest(new CaseErrorBody("bad_request", "type must be addDoc or altOffer.", null, null));
            }

            var fileName = Path.GetFileName(file.FileName);
            var text = string.Empty;
            var (bytes, format, bytesRefusal) = await DocumentsEndpointExtensions.ReadUploadBytesAsync(file, options, ct).ConfigureAwait(false);
            if (bytesRefusal is null)
            {
                var parsed = await parser.ParseAsync(fileName, format!.MimeType, bytes!, ct).ConfigureAwait(false);
                if (parsed.IsSuccess)
                {
                    text = string.Join("\n\n", parsed.Value.Select(p => p.Text));
                }
            }
            else if (Path.GetExtension(fileName).ToLowerInvariant() is ".txt" or ".md")
            {
                await using var stream = file.OpenReadStream();
                using var reader = new StreamReader(stream);
                text = await reader.ReadToEndAsync(ct).ConfigureAwait(false);
            }

            // An unreadable file is still recorded (by name); Raffa then says it did not read it, never guesses.
            int? version = int.TryParse(form["version"].ToString(), out var v) ? v : null;
            using var language = Raffa.SharedKernel.Language.ResponseLanguage.Use(form["lang"].ToString() == "en" ? "en" : "it");
            return Map(await service.ActWithDocumentAsync(
                    caller.TenantId, caller.Identity, caseId, type, fileName, text, form["vendor"].ToString() is { Length: > 0 } vendor ? vendor : null, version, ct)
                .ConfigureAwait(false));
        });

    private static Task<IResult> TimelineAsync(Guid caseId, HttpRequest request, RenewalCaseService service, ICallerContext callerContext, CancellationToken ct) =>
        WithCaller(request, callerContext, ct, async caller =>
        {
            var events = await service.TimelineAsync(caller.TenantId, caseId, ct).ConfigureAwait(false);
            return events is null
                ? Results.NotFound()
                : Results.Ok(events.Select(e => new { id = e.Id.Value, at = e.At, actor = e.Actor, type = e.Type, emailIndex = e.EmailIndex }));
        });

    /// <summary>
    /// Recipient and/or body edit. A body edit of an approved email makes a new version, back to draft,
    /// and invalidates the approval (event <c>email_edited_invalidated</c>). Version required: concurrent
    /// edits get 409 with the current case, never a silent overwrite.
    /// </summary>
    private static Task<IResult> PatchEmailAsync(Guid caseId, int eid, EmailPatchRequest body, HttpRequest request, RenewalCaseService service, ICallerContext callerContext, CancellationToken ct) =>
        WithCaller(request, callerContext, ct, async caller =>
        {
            if (body.Version is null)
            {
                return Results.BadRequest(new CaseErrorBody("bad_request", "version is required.", null, null));
            }

            return Map(await service.ActManyAsync(caller.TenantId, caller.Identity, caseId, body.Version, _ =>
            {
                var steps = new List<(string, CaseActionPayload)>();
                if (body.To is not null)
                {
                    steps.Add(("ui", new CaseActionPayload { K = "to:" + eid, V = body.To.Trim() }));
                }

                if (body.Body is not null)
                {
                    steps.Add(("edit", new CaseActionPayload { Eid = eid }));
                    steps.Add(("ui", new CaseActionPayload { K = "editBody", V = body.Body }));
                    steps.Add(("saveEdit", new CaseActionPayload { Eid = eid }));
                }

                return steps;
            }, ct).ConfigureAwait(false));
        });

    /// <summary>The user confirms they sent it (copy never does); the text actually sent may differ and is recorded.</summary>
    private static Task<IResult> SentAsync(Guid caseId, int eid, EmailSentRequest body, HttpRequest request, RenewalCaseService service, ICallerContext callerContext, CancellationToken ct) =>
        WithCaller(request, callerContext, ct, async caller => Map(await service.ActManyAsync(caller.TenantId, caller.Identity, caseId, body.Version, _ =>
        {
            var steps = new List<(string, CaseActionPayload)> { ("sentOpen", new CaseActionPayload { Eid = eid }) };
            if (!string.IsNullOrWhiteSpace(body.SentBody))
            {
                steps.Add(("ui", new CaseActionPayload { K = "sentEdited", V = true }));
                steps.Add(("ui", new CaseActionPayload { K = "sentText", V = body.SentBody }));
            }

            steps.Add(("sentOk", new CaseActionPayload { Eid = eid }));
            return steps;
        }, ct).ConfigureAwait(false)));

    private static Task<IResult> PutDelegationAsync(Guid caseId, Guid userId, DelegationRequest body, HttpRequest request, RenewalCaseService service, ICaseMemberDirectory members, ICallerContext callerContext, CancellationToken ct) =>
        WithCaller(request, callerContext, ct, async caller =>
        {
            var m = (await members.ListAsync(caller.TenantId, ct).ConfigureAwait(false)).FirstOrDefault(x => x.UserId == userId);
            if (m is null)
            {
                return Results.NotFound(new CaseErrorBody("not_found", "Not a member of this workspace.", null, null));
            }

            return Map(await service.ActManyAsync(caller.TenantId, caller.Identity, caseId, body.Version, c =>
            {
                var steps = new List<(string, CaseActionPayload)>();
                if (!c.Delegates.ContainsKey(m.Name))
                {
                    steps.Add(("delegate", new CaseActionPayload { Who = m.Name, On = true }));
                }

                var hasClose = c.Delegates.TryGetValue(m.Name, out var d) && d.Close;
                if (body.CanClose is { } want && want != hasClose)
                {
                    steps.Add(("delegateClose", new CaseActionPayload { Who = m.Name }));
                }

                return steps;
            }, ct).ConfigureAwait(false));
        });

    private static Task<IResult> DeleteDelegationAsync(Guid caseId, Guid userId, int? version, HttpRequest request, RenewalCaseService service, ICaseMemberDirectory members, ICallerContext callerContext, CancellationToken ct) =>
        WithCaller(request, callerContext, ct, async caller =>
        {
            var m = (await members.ListAsync(caller.TenantId, ct).ConfigureAwait(false)).FirstOrDefault(x => x.UserId == userId);
            if (m is null)
            {
                return Results.NotFound(new CaseErrorBody("not_found", "Not a member of this workspace.", null, null));
            }

            return Map(await service.ActAsync(caller.TenantId, caller.Identity, caseId, "delegate", new CaseActionPayload { Who = m.Name, On = false }, version, ct).ConfigureAwait(false));
        });

    private static Task<IResult> TransferAsync(Guid caseId, TransferRequest body, HttpRequest request, RenewalCaseService service, ICaseMemberDirectory members, ICallerContext callerContext, CancellationToken ct) =>
        WithCaller(request, callerContext, ct, async caller =>
        {
            var m = (await members.ListAsync(caller.TenantId, ct).ConfigureAwait(false)).FirstOrDefault(x => x.UserId == body.OwnerUserId);
            if (m is null)
            {
                return Results.BadRequest(new CaseErrorBody("bad_request", "The new owner must be a member of this workspace.", null, null));
            }

            return Map(await service.ActAsync(caller.TenantId, caller.Identity, caseId, "transfer", new CaseActionPayload { Who = m.Name }, body.Version, ct).ConfigureAwait(false));
        });

    /// <summary>Closes the step that ends the case (signed contract confirmed, fields validated, notice outcome) — approver or a delegate with closing rights only.</summary>
    private static Task<IResult> CloseAsync(Guid caseId, CaseVersionRequest body, HttpRequest request, RenewalCaseService service, ICallerContext callerContext, CancellationToken ct) =>
        WithCaller(request, callerContext, ct, async caller =>
        {
            var current = await service.GetAsync(caller.TenantId, caller.Identity, caseId, false, ct).ConfigureAwait(false);
            if (current.Kind != CaseResultKind.Ok)
            {
                return Map(current);
            }

            var page = current.Envelope!.View.Page.Current;
            var close = page.Buttons.Concat(page.More).FirstOrDefault(b => CloseActions.Contains(b.Act));
            if (close is null)
            {
                return Results.BadRequest(new CaseErrorBody("not_closable", "This case has no step that can close it now.", current.Envelope, null));
            }

            return Map(await service.ActAsync(caller.TenantId, caller.Identity, caseId, close.Act, close.P, body.Version, ct).ConfigureAwait(false));
        });

    private static Task<IResult> PostVendorMessageAsync(Guid caseId, VendorMessageRequest body, HttpRequest request, RenewalCaseService service, ICallerContext callerContext, CancellationToken ct) =>
        WithCaller(request, callerContext, ct, async caller =>
        {
            if (string.IsNullOrWhiteSpace(body.Text))
            {
                return Results.BadRequest(new CaseErrorBody("bad_request", "Paste the supplier reply.", null, null));
            }

            return Map(await service.ActAsync(caller.TenantId, caller.Identity, caseId, "reply", new CaseActionPayload { Text = CaseText.Of(body.Text) }, body.Version, ct).ConfigureAwait(false));
        });

    private static Task<IResult> GetVendorMessagesAsync(Guid caseId, HttpRequest request, RenewalCaseService service, ICallerContext callerContext, CancellationToken ct) =>
        WithCaller(request, callerContext, ct, async caller =>
        {
            var list = await service.VendorMessagesAsync(caller.TenantId, caseId, ct).ConfigureAwait(false);
            return list is null
                ? Results.NotFound()
                : Results.Ok(list.Select(x => new { id = x.Id.Value, direction = x.Direction, receivedAt = x.ReceivedAt, text = x.PastedText, contentHash = x.ContentHash, analysedBy = x.AnalysedByModel }));
        });

    private static Task<IResult> TodayAsync(HttpRequest request, RenewalCaseService service, ICallerContext callerContext, CancellationToken ct) =>
        WithCaller(request, callerContext, ct, async caller => Results.Ok(await service.TodayAsync(caller.TenantId, caller.Identity, ct).ConfigureAwait(false)));

    private static Task<IResult> SnoozeAsync(string key, bool on, HttpRequest request, RenewalCaseService service, ICallerContext callerContext, CancellationToken ct) =>
        WithCaller(request, callerContext, ct, async caller =>
            await service.SnoozeAsync(caller.TenantId, caller.Identity, key, on, ct).ConfigureAwait(false)
                ? Results.Ok(await service.TodayAsync(caller.TenantId, caller.Identity, ct).ConfigureAwait(false))
                : Results.BadRequest(new CaseErrorBody("bad_request", "Unknown item or caller.", null, null)));

    private static Task<IResult> GetRemindersAsync(HttpRequest request, RenewalCaseService service, ICallerContext callerContext, CancellationToken ct) =>
        WithCaller(request, callerContext, ct, async caller => Results.Ok(await service.RemindersAsync(caller.TenantId, caller.Identity, ct).ConfigureAwait(false)));

    private static Task<IResult> PostReminderAsync(ReminderRequest body, HttpRequest request, RenewalCaseService service, ICallerContext callerContext, CancellationToken ct) =>
        WithCaller(request, callerContext, ct, async caller =>
        {
            var r = await service.AddReminderAsync(caller.TenantId, caller.Identity, body.CaseId, body.DueDate, ct).ConfigureAwait(false);
            return r is null ? Results.BadRequest(new CaseErrorBody("bad_request", "Unknown caller.", null, null)) : Results.Ok(r);
        });

    private static Task<IResult> PatchReminderAsync(Guid id, ReminderPatchRequest body, HttpRequest request, RenewalCaseService service, ICallerContext callerContext, CancellationToken ct) =>
        WithCaller(request, callerContext, ct, async caller =>
        {
            var r = await service.SetReminderStateAsync(caller.TenantId, caller.Identity, id, body.State, ct).ConfigureAwait(false);
            return r is null ? Results.NotFound() : Results.Ok(r);
        });
}
