using System.Globalization;
using System.Net.Mail;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Raffa.Renewals.Application.CaseWork;
using Raffa.Renewals.Application.Cases;
using Raffa.Renewals.Application.Notifications;
using Raffa.Renewals.Domain.Cases;
using Raffa.Renewals.Infrastructure;
using Raffa.SharedKernel;
using Raffa.SharedKernel.Contracts;
using Raffa.SharedKernel.Storage;
using Raffa.SharedKernel.Tenancy;

namespace Raffa.Renewals.Application.Outreach;

/// <summary>An email that reached Raffa's inbound domain (already read by the host).</summary>
public sealed record InboundMail(
    string Provider,
    string ProviderMessageId,
    IReadOnlyList<string> Recipients,
    string From,
    string Subject,
    DateTimeOffset ReceivedAt,
    string Preview,
    IReadOnlyList<string> AttachmentNames,
    byte[] Raw);

/// <summary>matched | duplicate | unmatched.</summary>
public sealed record InboundOutcome(string Result, Guid? CaseId = null);

public sealed record InboundItem(Guid Id, string From, string Subject, DateTimeOffset ReceivedAt, string Preview, IReadOnlyList<string> Attachments, string State, string? DecidedBy, DateTimeOffset? DecidedAt);

/// <summary><c>GET /api/renewals/{id}/mail</c>: how the case talks to the supplier.</summary>
public sealed record CaseMailStatus(string? CaseAddress, MailboxInfo? Mailbox, bool MailboxAvailable, IReadOnlyList<InboundItem> Inbound);

/// <summary>
/// The case's mail (plan F2/F3): sends an approved email from the caller's company mailbox with the case
/// address in CC and Reply-To, receives what reaches the case address, and lets the owner confirm an
/// incoming email belongs to the case (registered as the supplier's reply, with its attachments) or dismiss
/// it. An incoming email never moves the case by itself; the owner and the case delegates are notified.
/// </summary>
public sealed partial class CaseMailService(
    RenewalsDbContext db,
    ITenantContext tenantContext,
    IClock clock,
    CaseMailOptions options,
    MailboxService mailboxes,
    RenewalCaseService cases,
    ICaseMemberDirectory members,
    IContractFactsReader factsReader,
    NotificationService notifications,
    IDocumentStorage storage,
    IContractDocumentFiles? contractFiles = null,
    Microsoft.Extensions.Logging.ILogger<CaseMailService>? logger = null)
{
    private const string Alphabet = "abcdefghijklmnopqrstuvwxyz0123456789";

    public async Task<CaseMailStatus?> StatusAsync(TenantId tenantId, string identity, Guid caseId, CancellationToken ct = default)
    {
        using var scope = tenantContext.BeginScope(tenantId);
        var record = await db.RenewalCases.AsNoTracking().FirstOrDefaultAsync(x => x.Id == new EntityId(caseId), ct).ConfigureAwait(false);
        if (record is null)
        {
            return null;
        }

        var address = await AddressAsync(tenantId, record.Id, create: record.ClosedAt is null, ct).ConfigureAwait(false);
        var mailbox = await mailboxes.ConnectionAsync(tenantId, identity, ct).ConfigureAwait(false);
        var inbound = await db.CaseInboundMails.AsNoTracking().Where(x => x.CaseId == record.Id)
            .OrderByDescending(x => x.ReceivedAt).Take(50).ToListAsync(ct).ConfigureAwait(false);
        return new CaseMailStatus(
            address,
            mailbox is null ? null : MailboxService.Info(mailbox),
            mailboxes.Available,
            inbound.Select(Item).ToList());
    }

    /// <summary>The case address (<c>r-&lt;token&gt;@&lt;inbound domain&gt;</c>), created on first use; null when inbound mail is off.</summary>
    internal async Task<string?> AddressAsync(TenantId tenantId, EntityId caseId, bool create, CancellationToken ct)
    {
        if (!options.InboundEnabled)
        {
            return null;
        }

        using var scope = tenantContext.BeginScope(tenantId);
        var row = await db.CaseMailAddresses.AsNoTracking()
            .FirstOrDefaultAsync(x => x.TenantId == tenantId && x.CaseId == caseId, ct).ConfigureAwait(false);
        if (row is null && create)
        {
            row = new CaseMailAddressRecord
            {
                TenantId = tenantId,
                CaseId = caseId,
                Token = RandomNumberGenerator.GetString(Alphabet, 12),
                CreatedAt = clock.UtcNow,
            };
            db.CaseMailAddresses.Add(row);
            try
            {
                await db.SaveChangesAsync(ct).ConfigureAwait(false);
            }
            catch (DbUpdateException)
            {
                db.ChangeTracker.Clear();
                row = await db.CaseMailAddresses.AsNoTracking()
                    .FirstOrDefaultAsync(x => x.TenantId == tenantId && x.CaseId == caseId, ct).ConfigureAwait(false);
            }
        }

        return row is null ? null : Address(row.Token);
    }

    private string Address(string token) => "r-" + token + "@" + options.InboundDomain!.Trim().ToLowerInvariant();

    /// <summary>
    /// <c>POST /api/renewals/{id}/emails/{eid}/send</c>: sends the approved version of the email from the
    /// caller's connected mailbox (To = the email's recipient, CC and Reply-To = the case address, the
    /// contract documents the email names attached), then records it as sent from that mailbox.
    /// </summary>
    public async Task<CaseResult> SendEmailAsync(TenantId tenantId, string identity, Guid caseId, int eid, int? version, CancellationToken ct = default)
    {
        RenewalCaseRecord? record;
        using (tenantContext.BeginScope(tenantId))
        {
            record = await db.RenewalCases.AsNoTracking().FirstOrDefaultAsync(x => x.Id == new EntityId(caseId), ct).ConfigureAwait(false);
        }

        if (record is null)
        {
            return CaseResult.NotFound("No case with this id.");
        }

        if (version is { } v && v != record.Version)
        {
            var current = await cases.GetAsync(tenantId, identity, caseId, false, ct).ConfigureAwait(false);
            return new CaseResult(CaseResultKind.Conflict, current.Envelope, "The case changed since you loaded it.");
        }

        var c = RenewalCaseJson.Deserialize(record.State);
        var email = c.Emails.FirstOrDefault(x => x.Id == eid);
        if (email is null || email.State is not (EmailStates.Approved or EmailStates.Copied))
        {
            return Refuse("Only an approved email can be sent.", "not_approved");
        }

        var to = Recipients(email.To);
        if (to.Count == 0)
        {
            return Refuse("Add a valid recipient address before sending.", "no_recipient");
        }

        var people = await members.ListAsync(tenantId, ct).ConfigureAwait(false);
        var caller = CaseCaller.From(people, identity);
        if (!CaseRules.CanWork(c, caller.Name))
        {
            var current = await cases.GetAsync(tenantId, identity, caseId, false, ct).ConfigureAwait(false);
            return new CaseResult(CaseResultKind.Forbidden, current.Envelope, "Only the owner, the approver or a delegate can send this email.");
        }

        var connection = await mailboxes.ConnectionAsync(tenantId, identity, ct).ConfigureAwait(false);
        if (connection is null)
        {
            return Refuse("Connect your company mailbox to send from Raffa.", "no_mailbox");
        }

        var caseAddress = await AddressAsync(tenantId, record.Id, create: true, ct).ConfigureAwait(false);
        var attachments = new List<CaseUpload>();
        foreach (var name in email.Attach.Where(a => !string.IsNullOrWhiteSpace(a)).Distinct(StringComparer.Ordinal))
        {
            if (contractFiles is not null
                && await contractFiles.LoadAsync(tenantId, record.ContractId.Value, name, ct).ConfigureAwait(false) is { } file)
            {
                attachments.Add(new CaseUpload(file.FileName, file.MimeType, file.Content));
            }
        }

        var mail = new OutgoingMail(
            connection.Address,
            string.IsNullOrWhiteSpace(connection.DisplayName) ? caller.Name : connection.DisplayName,
            to,
            caseAddress is null ? [] : [caseAddress],
            caseAddress is null ? [] : [connection.Address, caseAddress],
            email.Subject,
            email.Body,
            attachments);
        try
        {
            await mailboxes.SendAsync(tenantId, connection, mail, ct).ConfigureAwait(false);
        }
        catch (MailboxSendException ex)
        {
            if (logger is not null)
            {
                Microsoft.Extensions.Logging.LoggerExtensions.LogWarning(logger, "Case email not sent from the connected {Provider} mailbox (reconnect: {Reconnect})", connection.Provider, ex.Reconnect);
            }

            return Refuse(
                ex.Reconnect ? "Your mailbox connection has expired. Connect it again, then send." : "Your mailbox refused the email. Nothing was sent; try again later.",
                ex.Reconnect ? "mailbox_reconnect" : "mailbox_refused");
        }

        var now = clock.UtcNow;
        var at = CaseFormat.Date(DateOnly.FromDateTime(now.UtcDateTime)) + " · " + now.UtcDateTime.ToString("HH:mm", CultureInfo.InvariantCulture);
        return await cases.ActManyAsync(tenantId, identity, caseId, null, _ =>
        [
            ("sentOpen", new CaseActionPayload { Eid = eid }),
            ("ui", new CaseActionPayload { K = "sentDate", V = at }),
            ("ui", new CaseActionPayload { K = "sentVia", V = connection.Address }),
            ("sentOk", new CaseActionPayload { Eid = eid }),
        ], ct).ConfigureAwait(false);
    }

    private static CaseResult Refuse(string error, string code) => new(CaseResultKind.BadRequest, Error: error, Code: code);

    internal static List<string> Recipients(string to) =>
        to.Split([',', ';', ' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(a => MailAddress.TryCreate(a, out _))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

    /// <summary>
    /// The inbound webhook: an email that reached the inbound domain. Matched to a case by the case address
    /// among its recipients, stored (raw message in object storage) as pending, and notified to the owner and
    /// the delegates. Unknown addresses are dropped; the same provider message twice is stored once.
    /// </summary>
    public async Task<InboundOutcome> ReceiveAsync(InboundMail mail, CancellationToken ct = default)
    {
        if (!options.InboundEnabled)
        {
            return new InboundOutcome("unmatched");
        }

        var tokens = mail.Recipients.Select(TokenOf).Where(t => t is not null).Select(t => t!).Distinct().ToList();
        if (tokens.Count == 0)
        {
            return new InboundOutcome("unmatched");
        }

        // The routing table has no row-level security: it is read before any workspace is known.
        var route = await db.CaseMailAddresses.AsNoTracking()
            .FirstOrDefaultAsync(x => tokens.Contains(x.Token) && x.DisabledAt == null, ct).ConfigureAwait(false);
        if (route is null)
        {
            return new InboundOutcome("unmatched");
        }

        var tenantId = route.TenantId;
        using var scope = tenantContext.BeginScope(tenantId);
        var record = await db.RenewalCases.AsNoTracking().FirstOrDefaultAsync(x => x.Id == route.CaseId, ct).ConfigureAwait(false);
        if (record is null)
        {
            return new InboundOutcome("unmatched");
        }

        if (await db.CaseInboundMails.AnyAsync(x => x.Provider == mail.Provider && x.ProviderMessageId == mail.ProviderMessageId, ct).ConfigureAwait(false))
        {
            return new InboundOutcome("duplicate", record.Id.Value);
        }

        var rawId = EntityId.New();
        string path;
        using (var content = new MemoryStream(mail.Raw, writable: false))
        {
            path = await storage.SaveAsync(tenantId, rawId, 1, "message.eml", content, ct).ConfigureAwait(false);
        }

        db.CaseInboundMails.Add(new CaseInboundMailRecord
        {
            TenantId = tenantId,
            CaseId = record.Id,
            Provider = mail.Provider,
            ProviderMessageId = Cut(mail.ProviderMessageId, 200),
            FromAddress = Cut(mail.From, 400),
            Subject = Cut(mail.Subject, 500),
            ReceivedAt = mail.ReceivedAt,
            Preview = Cut(mail.Preview, 20000),
            AttachmentNamesJson = mail.AttachmentNames.Count == 0 ? null : JsonSerializer.Serialize(mail.AttachmentNames),
            RawStoragePath = path,
            State = "pending",
        });
        db.CaseEvents.Add(new CaseEventRecord
        {
            TenantId = tenantId,
            CaseId = record.Id,
            At = clock.UtcNow,
            Actor = "supplier",
            Type = "inbound_received",
        });
        try
        {
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
        }
        catch (DbUpdateException)
        {
            // The provider delivered the same message twice at once: the unique index kept one.
            db.ChangeTracker.Clear();
            await storage.DeleteAsync(tenantId, path, CancellationToken.None).ConfigureAwait(false);
            return new InboundOutcome("duplicate", record.Id.Value);
        }

        await NotifyAsync(tenantId, record, ct).ConfigureAwait(false);
        return new InboundOutcome("matched", record.Id.Value);
    }

    private async Task NotifyAsync(TenantId tenantId, RenewalCaseRecord record, CancellationToken ct)
    {
        try
        {
            var people = await members.ListAsync(tenantId, ct).ConfigureAwait(false);
            var delegates = await db.CaseDelegations.AsNoTracking()
                .Where(d => d.CaseId == record.Id && d.RevokedAt == null && d.UserId != null)
                .Select(d => d.UserId!.Value).ToListAsync(ct).ConfigureAwait(false);
            var ids = new HashSet<Guid>(delegates);
            if (record.OwnerUserId is { } owner)
            {
                ids.Add(owner);
            }

            var recipients = people.Where(m => ids.Contains(m.UserId) && m.ExternalSubjectId is not null).Select(m => m.ExternalSubjectId!).ToList();
            if (recipients.Count == 0)
            {
                recipients = people.Where(m => m.IsAdmin && m.ExternalSubjectId is not null).Select(m => m.ExternalSubjectId!).ToList();
            }

            var facts = await factsReader.GetAsync(tenantId, record.ContractId, ct).ConfigureAwait(false);
            var supplier = string.IsNullOrWhiteSpace(facts?.Supplier) ? "the supplier" : facts!.Supplier;
            var contract = string.IsNullOrWhiteSpace(facts?.Name) ? "the renewal" : CaseFactsMapper.ShortName(facts!.Name);
            await notifications.PublishAsync(tenantId, recipients, new NotificationMessage(
                "supplier_message",
                "New email from " + supplier,
                contract + ": confirm it belongs to the negotiation to add it to the case.",
                "/renewals/" + record.Id.Value,
                record.Id.Value), ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            if (logger is not null)
            {
                Microsoft.Extensions.Logging.LoggerExtensions.LogWarning(logger, ex, "The notification for an inbound case email could not be published.");
            }
        }
    }

    /// <summary>
    /// <c>POST /api/renewals/{id}/inbound/{mid}/accept</c>: the owner confirms the email belongs to the case;
    /// it is registered as the supplier's reply (sender, date, new text) with its attachments.
    /// <paramref name="read"/> reads the raw message (the host's email reader).
    /// </summary>
    public async Task<CaseResult> AcceptInboundAsync(
        TenantId tenantId, string identity, Guid caseId, Guid inboundId, int? version, Func<byte[], CaseEmailUpload?> read, CancellationToken ct = default)
    {
        CaseInboundMailRecord? row;
        using (tenantContext.BeginScope(tenantId))
        {
            row = await db.CaseInboundMails.FirstOrDefaultAsync(x => x.Id == new EntityId(inboundId) && x.CaseId == new EntityId(caseId), ct).ConfigureAwait(false);
        }

        if (row is null)
        {
            return CaseResult.NotFound("No such email in this case.");
        }

        if (row.State != "pending")
        {
            return Refuse("This email was already handled.", "already_handled");
        }

        var raw = await storage.LoadAsync(tenantId, row.RawStoragePath, ct).ConfigureAwait(false);
        var email = (raw is null ? null : read(raw))
                    ?? new CaseEmailUpload(new CaseUpload("message.eml", "message/rfc822", raw ?? []), "From: " + row.FromAddress + "\nSubject: " + row.Subject + "\n\n" + row.Preview, []);
        var result = await cases.ReplyWithEmailAsync(tenantId, identity, caseId, email, version, ct).ConfigureAwait(false);
        if (result.Kind is CaseResultKind.Ok or CaseResultKind.Duplicate)
        {
            using var scope = tenantContext.BeginScope(tenantId);
            var people = await members.ListAsync(tenantId, ct).ConfigureAwait(false);
            row.State = "accepted";
            row.DecidedBy = CaseCaller.From(people, identity).Name;
            row.DecidedAt = clock.UtcNow;
            var state = await db.RenewalCases.AsNoTracking().Where(x => x.Id == row.CaseId).Select(x => x.State).FirstAsync(ct).ConfigureAwait(false);
            row.ReplyIndex = RenewalCaseJson.Deserialize(state).Reps.Count - 1;
            db.CaseInboundMails.Update(row);
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
        }

        return result;
    }

    /// <summary><c>POST /api/renewals/{id}/inbound/{mid}/dismiss</c>: the email does not belong to this case.</summary>
    public async Task<bool> DismissInboundAsync(TenantId tenantId, string identity, Guid caseId, Guid inboundId, CancellationToken ct = default)
    {
        using var scope = tenantContext.BeginScope(tenantId);
        var row = await db.CaseInboundMails.FirstOrDefaultAsync(x => x.Id == new EntityId(inboundId) && x.CaseId == new EntityId(caseId), ct).ConfigureAwait(false);
        if (row is null || row.State != "pending")
        {
            return false;
        }

        var people = await members.ListAsync(tenantId, ct).ConfigureAwait(false);
        row.State = "dismissed";
        row.DecidedBy = CaseCaller.From(people, identity).Name;
        row.DecidedAt = clock.UtcNow;
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return true;
    }

    /// <summary>The token of a case address on the inbound domain (<c>r-&lt;token&gt;@domain</c>, any display name), or null.</summary>
    internal string? TokenOf(string recipient)
    {
        var m = AddressPattern().Match(recipient);
        if (!m.Success)
        {
            return null;
        }

        return string.Equals(m.Groups[2].Value, options.InboundDomain?.Trim(), StringComparison.OrdinalIgnoreCase) ? m.Groups[1].Value.ToLowerInvariant() : null;
    }

    private static InboundItem Item(CaseInboundMailRecord x) => new(
        x.Id.Value, x.FromAddress, x.Subject, x.ReceivedAt, x.Preview,
        x.AttachmentNamesJson is { } j ? JsonSerializer.Deserialize<List<string>>(j) ?? [] : [],
        x.State, x.DecidedBy, x.DecidedAt);

    private static string Cut(string s, int max) => s.Length <= max ? s : s[..max];

    [GeneratedRegex(@"\br-([a-z0-9]{6,40})@([a-z0-9.\-]+)", RegexOptions.IgnoreCase)]
    private static partial Regex AddressPattern();
}
