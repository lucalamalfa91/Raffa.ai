using Raffa.Api.Infrastructure;
using Raffa.Renewals.Application.CaseWork;
using Raffa.Renewals.Application.Notifications;
using Raffa.Renewals.Application.Outreach;
using static Raffa.Api.RenewalCasesEndpointExtensions;

namespace Raffa.Api;

/// <summary>
/// Renewal plan F2/F3/F3b: company mailboxes (connect, disconnect, send a case email from it), a case's mail
/// (its address, the emails that reached it, accept / dismiss) and notifications (the bell, the channels).
/// Every route but the OAuth callback and the Resend webhook works on the signed-in caller and their workspace.
/// </summary>
public static class OutreachEndpointExtensions
{
    public sealed record ConnectMailboxRequest(string Provider, string? LoginHint);

    public sealed record SendEmailRequest(int? Version);

    public sealed record InboundDecisionRequest(int? Version, string? Lang);

    public sealed record MarkReadRequest(Guid? Id);

    public sealed record AddChannelRequest(string Kind, string Target, string? Label);

    public sealed record UpdateChannelRequest(bool Enabled);

    public static IEndpointRouteBuilder MapOutreachEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/mailbox", (HttpRequest r, MailboxService s, ICallerContext cc, CancellationToken ct) =>
            WithCaller(r, cc, ct, async caller => Results.Ok(await s.StatusAsync(caller.TenantId, caller.Identity!, ct).ConfigureAwait(false))));

        endpoints.MapPost("/api/mailbox/connect", (ConnectMailboxRequest body, HttpRequest r, MailboxService s, ICallerContext cc, CancellationToken ct) =>
            WithCaller(r, cc, ct, caller =>
                Task.FromResult(s.BeginConnect(caller.TenantId, caller.Identity!, body.Provider, body.LoginHint) is { } url
                    ? Results.Ok(new { url })
                    : Results.BadRequest(new CaseErrorBody("unavailable", "This mailbox provider is not available.", null, null)))));

        // The provider redirects the browser here (no bearer token): the signed state carries who and where.
        endpoints.MapGet("/api/mailbox/callback/{provider}", async (string provider, string? code, string? state, string? error, MailboxService s, CancellationToken ct) =>
        {
            var outcome = await s.CompleteConnectAsync(provider, state, code, error, ct).ConfigureAwait(false);
            return Results.Redirect(outcome.RedirectUrl);
        });

        endpoints.MapDelete("/api/mailbox", (HttpRequest r, MailboxService s, ICallerContext cc, CancellationToken ct) =>
            WithCaller(r, cc, ct, async caller =>
                await s.DisconnectAsync(caller.TenantId, caller.Identity!, ct).ConfigureAwait(false) ? Results.NoContent() : Results.NotFound()));

        endpoints.MapGet("/api/renewals/{caseId:guid}/mail", (Guid caseId, HttpRequest r, CaseMailService s, ICallerContext cc, CancellationToken ct) =>
            WithCaller(r, cc, ct, async caller =>
                await s.StatusAsync(caller.TenantId, caller.Identity!, caseId, ct).ConfigureAwait(false) is { } status ? Results.Ok(status) : Results.NotFound()));

        endpoints.MapPost("/api/renewals/{caseId:guid}/emails/{eid:int}/send", (Guid caseId, int eid, SendEmailRequest body, HttpRequest r, CaseMailService s, ICallerContext cc, CancellationToken ct) =>
            WithCaller(r, cc, ct, async caller => Map(await s.SendEmailAsync(caller.TenantId, caller.Identity!, caseId, eid, body.Version, ct).ConfigureAwait(false))));

        endpoints.MapPost("/api/renewals/{caseId:guid}/inbound/{mid:guid}/accept", (Guid caseId, Guid mid, InboundDecisionRequest body, HttpRequest r, CaseMailService s, ICallerContext cc, CancellationToken ct) =>
            WithCaller(r, cc, ct, async caller =>
            {
                using var language = Raffa.SharedKernel.Language.ResponseLanguage.Use(body.Lang == "en" ? "en" : "it");
                return Map(await s.AcceptInboundAsync(caller.TenantId, caller.Identity!, caseId, mid, body.Version,
                    raw => CaseEmailFileReader.Read("message.eml", raw, 20L * 1024 * 1024), ct).ConfigureAwait(false));
            }));

        endpoints.MapPost("/api/renewals/{caseId:guid}/inbound/{mid:guid}/dismiss", (Guid caseId, Guid mid, HttpRequest r, CaseMailService s, ICallerContext cc, CancellationToken ct) =>
            WithCaller(r, cc, ct, async caller =>
                await s.DismissInboundAsync(caller.TenantId, caller.Identity!, caseId, mid, ct).ConfigureAwait(false) ? Results.NoContent() : Results.NotFound()));

        endpoints.MapGet("/api/notifications", (int? limit, HttpRequest r, NotificationService s, ICallerContext cc, CancellationToken ct) =>
            WithCaller(r, cc, ct, async caller => Results.Ok(await s.FeedAsync(caller.TenantId, caller.Identity!, limit ?? 30, ct).ConfigureAwait(false))));

        endpoints.MapPost("/api/notifications/read", (MarkReadRequest body, HttpRequest r, NotificationService s, ICallerContext cc, CancellationToken ct) =>
            WithCaller(r, cc, ct, async caller =>
            {
                await s.MarkReadAsync(caller.TenantId, caller.Identity!, body.Id, ct).ConfigureAwait(false);
                return Results.NoContent();
            }));

        endpoints.MapGet("/api/notifications/settings", (HttpRequest r, NotificationService s, ICallerContext cc, CancellationToken ct) =>
            WithCaller(r, cc, ct, async caller => Results.Ok(await s.SettingsAsync(caller.TenantId, caller.Identity!, ct).ConfigureAwait(false))));

        endpoints.MapPost("/api/notifications/channels", (AddChannelRequest body, HttpRequest r, NotificationService s, ICallerContext cc, CancellationToken ct) =>
            WithCaller(r, cc, ct, async caller =>
            {
                var (channel, error) = await s.AddChannelAsync(caller.TenantId, caller.Identity!, body.Kind, body.Target ?? string.Empty, body.Label, ct).ConfigureAwait(false);
                return channel is null ? Results.BadRequest(new CaseErrorBody("bad_channel", error, null, null)) : Results.Ok(channel);
            }));

        endpoints.MapPut("/api/notifications/channels/{id:guid}", (Guid id, UpdateChannelRequest body, HttpRequest r, NotificationService s, ICallerContext cc, CancellationToken ct) =>
            WithCaller(r, cc, ct, async caller =>
                await s.UpdateChannelAsync(caller.TenantId, caller.Identity!, id, body.Enabled, ct).ConfigureAwait(false) ? Results.NoContent() : Results.NotFound()));

        endpoints.MapDelete("/api/notifications/channels/{id:guid}", (Guid id, HttpRequest r, NotificationService s, ICallerContext cc, CancellationToken ct) =>
            WithCaller(r, cc, ct, async caller =>
                await s.RemoveChannelAsync(caller.TenantId, caller.Identity!, id, ct).ConfigureAwait(false) ? Results.NoContent() : Results.NotFound()));

        endpoints.MapPost("/api/notifications/test", (HttpRequest r, NotificationService s, ICallerContext cc, CancellationToken ct) =>
            WithCaller(r, cc, ct, async caller =>
            {
                await s.TestAsync(caller.TenantId, caller.Identity!, ct).ConfigureAwait(false);
                return Results.NoContent();
            }));

        endpoints.MapResendWebhook();
        return endpoints;
    }
}
