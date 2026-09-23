using Raffa.Chat.Domain.Conversations;
using Raffa.Chat.Application.Interview;
using Raffa.Chat.Infrastructure;
using Raffa.SharedKernel;
using Raffa.SharedKernel.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace Raffa.Chat.Application.Conversations;

/// <summary>
/// Implements task E13/F05/US01/T01 (story us-01-conversations, AC-1/AC-4 backing; the HTTP
/// surface itself is task T02): create, list-recent, get-with-messages and append-message over
/// <see cref="ChatDbContext"/>'s two tables (ADR-024 "Conversations (D5)").
///
/// <para>
/// <b>Tenant + user scoping (R-CONV-01 "keyed by tenant + user"; AC-1 "another user of the same
/// workspace gets 404/403")</b>: every method opens its own <see cref="ITenantContext.BeginScope"/>
/// (same "own the scope for the duration of the call" convention
/// <c>Raffa.Documents.Contracts.Application.DocumentUploadService"/>/<c>DocumentQueryService</c>
/// already use) so the RLS backstop is live regardless of what the caller already entered, and
/// every query additionally filters by <see cref="Conversation.UserId"/> in code — RLS itself has
/// no per-user predicate (see <see cref="Conversation.UserId"/>'s own doc comment), so the
/// application-level filter here is the *only* thing standing between one workspace member and
/// another's conversation, not a redundant belt-and-suspenders layer the way the tenant filter is.
/// </para>
///
/// <para>
/// <b>No engine here</b>: this service never calls <c>Raffa.AiGateway</c> or a retrieval
/// pipeline — it only persists whatever the caller (a later Ask-engine task) already computed for
/// a turn (see <see cref="AppendConversationMessageRequest"/>'s own doc comment).
/// </para>
///
/// <para>
/// <b>Audit (ADR-011 "audit of access and corrections")</b>: writes <c>conversation.created</c>
/// on <see cref="CreateAsync"/> and <c>conversation.message.appended</c> on
/// <see cref="AppendMessageAsync"/> — the same two events story us-01-conversations names in its
/// own coding objective. <see cref="ListRecentAsync"/>/<see cref="GetAsync"/> write nothing, the
/// same "reads are not audited, only mutations are" convention
/// <c>DocumentQueryService</c>/<c>Contract360QueryService</c>/<c>PortfolioQueryService</c> already
/// establish elsewhere in this codebase.
/// </para>
/// </summary>
public sealed class ConversationService(
    ChatDbContext dbContext, ITenantContext tenantContext, IClock clock, IAuditWriter auditWriter)
{
    /// <summary>R-CONV-01: "A conversation has a title (first question, &lt;= 48 chars)".</summary>
    public const int TitleMaxLength = 48;

    /// <summary>R-CONV-02: "The rail shows the user's last 5 conversations". A later task's own
    /// `GET /api/conversations` may still pass a different <c>limit</c> to
    /// <see cref="ListRecentAsync"/> — this is only the default when none is supplied.</summary>
    public const int DefaultRecentLimit = 5;

    /// <summary>Upper bound for <see cref="ListRecentAsync"/> so a search/filter of accumulated
    /// chats cannot unbounded-scan the table in one request.</summary>
    public const int MaxRecentLimit = 100;

    /// <summary>
    /// The title a brand-new conversation carries until its first
    /// <see cref="ConversationRole.You"/> message derives a real one (see
    /// <see cref="DeriveTitle"/>). Echoes the V2 prototype's own "+ New chat" rail affordance
    /// (`raffa-v2/markup.html`) and its `app.jsx` fallback label
    /// (`'Ask Raffa · new chat'`, shortened here to fit comfortably under
    /// <see cref="TitleMaxLength"/> alongside a real title) — no ADR/spec pins the literal
    /// stored string, since `POST /api/conversations` takes no title at all (ADR-024 §6) and this
    /// value only shows on screen for the brief window before the first question lands.
    /// </summary>
    public const string DefaultTitle = "New chat";

    /// <summary>
    /// A chat not used for this long — no message, no restore — is archived: listed apart, so the
    /// rail's recent list stays short. Nothing is moved or deleted; it is a reading of
    /// <see cref="Conversation.UpdatedAt"/>, so the next message (or <see cref="RestoreAsync"/>)
    /// brings the chat back at once.
    /// </summary>
    public static readonly TimeSpan ArchiveAfter = TimeSpan.FromDays(7);

    private const string AuditConversationCreatedAction = "conversation.created";
    private const string AuditMessageAppendedAction = "conversation.message.appended";
    private const string AuditConversationDeletedAction = "conversation.deleted";
    private const string AuditConversationRenamedAction = "conversation.renamed";
    private const string AuditConversationRestoredAction = "conversation.restored";
    private const string AuditConversationResourceType = "conversation";
    private const string AuditConversationMessageResourceType = "conversation_message";

    public async Task<ConversationSummaryResult> CreateAsync(
        TenantId tenantId,
        string userId,
        EntityId? scopeContractId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(userId))
        {
            throw new ArgumentException("A user id is required.", nameof(userId));
        }

        using var tenantScope = tenantContext.BeginScope(tenantId);

        var now = clock.UtcNow;
        var conversation = new Conversation
        {
            TenantId = tenantId,
            UserId = userId,
            Title = DefaultTitle,
            ScopeContractId = scopeContractId,
            CreatedAt = now,
            UpdatedAt = now,
        };

        dbContext.Conversations.Add(conversation);
        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        await auditWriter.WriteAsync(
            new AuditEntry(
                tenantId,
                userId,
                AuditConversationCreatedAction,
                AuditConversationResourceType,
                conversation.Id.Value.ToString(),
                now,
                scopeContractId is null ? null : $"scopeContractId={scopeContractId}"),
            cancellationToken).ConfigureAwait(false);

        return ToSummary(conversation, now);
    }

    /// <summary>R-CONV-02 "last N conversations", most recently active first (see
    /// <see cref="Conversation.UpdatedAt"/>'s own doc comment). <paramref name="archive"/> narrows
    /// it to the chats in use or to the archived ones (<see cref="ArchiveAfter"/>), each its own
    /// "last N", so a long archive never crowds the recent list out of <paramref name="limit"/>.</summary>
    public async Task<IReadOnlyList<ConversationSummaryResult>> ListRecentAsync(
        TenantId tenantId,
        string userId,
        int limit = DefaultRecentLimit,
        ConversationArchiveFilter archive = ConversationArchiveFilter.All,
        CancellationToken cancellationToken = default)
    {
        using var tenantScope = tenantContext.BeginScope(tenantId);

        var take = Math.Clamp(limit, 1, MaxRecentLimit);
        var now = clock.UtcNow;
        var archivedBefore = now - ArchiveAfter;

        var query = dbContext.Conversations
            .AsNoTracking()
            .Where(c => c.TenantId == tenantId && c.UserId == userId);
        query = archive switch
        {
            ConversationArchiveFilter.Active => query.Where(c => c.UpdatedAt >= archivedBefore),
            ConversationArchiveFilter.Archived => query.Where(c => c.UpdatedAt < archivedBefore),
            _ => query,
        };

        var conversations = await query
            .OrderByDescending(c => c.UpdatedAt)
            .Take(take)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return conversations.Select(conversation => ToSummary(conversation, now)).ToList();
    }

    /// <summary>AC-2 "returns it with its messages"; AC-1 "another user... gets 404/403" —
    /// <see langword="null"/> when <paramref name="conversationId"/> does not name a conversation
    /// owned by <paramref name="userId"/> in <paramref name="tenantId"/> (whether it belongs to
    /// another tenant, another user, or does not exist at all — the same "not found" outcome for
    /// all three, so a caller can never distinguish "wrong tenant" from "wrong user" from
    /// "unknown id" by response shape alone).</summary>
    public async Task<ConversationDetailResult?> GetAsync(
        TenantId tenantId,
        string userId,
        EntityId conversationId,
        CancellationToken cancellationToken = default)
    {
        using var tenantScope = tenantContext.BeginScope(tenantId);

        var conversation = await dbContext.Conversations
            .AsNoTracking()
            .SingleOrDefaultAsync(
                c => c.TenantId == tenantId && c.UserId == userId && c.Id == conversationId,
                cancellationToken)
            .ConfigureAwait(false);

        if (conversation is null)
        {
            return null;
        }

        var messages = await dbContext.ConversationMessages
            .AsNoTracking()
            .Where(m => m.TenantId == tenantId && m.ConversationId == conversationId)
            .OrderBy(m => m.CreatedAt)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return new ConversationDetailResult(
            conversation.Id,
            conversation.Title,
            conversation.ScopeContractId,
            conversation.CreatedAt,
            conversation.UpdatedAt,
            messages.Select(ToMessageResult).ToList(),
            conversation.CustomTitle);
    }

    /// <summary><see langword="null"/> under the identical "not this user's conversation in this
    /// tenant" rule <see cref="GetAsync"/> documents. On success: persists the turn, bumps
    /// <see cref="Conversation.UpdatedAt"/>, derives <see cref="Conversation.Title"/> from the
    /// first <see cref="ConversationRole.You"/> question (R-CONV-01), and writes one
    /// <c>conversation.message.appended</c> audit row.</summary>
    public async Task<ConversationMessageResult?> AppendMessageAsync(
        TenantId tenantId,
        string userId,
        EntityId conversationId,
        AppendConversationMessageRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.Markdown is null || request.CitationsJson is null || request.ActionsJson is null)
        {
            throw new ArgumentException(
                "Markdown, CitationsJson and ActionsJson are required (pass \"[]\" for an empty " +
                "citations/actions array, never null).",
                nameof(request));
        }

        using var tenantScope = tenantContext.BeginScope(tenantId);

        var conversation = await dbContext.Conversations
            .SingleOrDefaultAsync(
                c => c.TenantId == tenantId && c.UserId == userId && c.Id == conversationId,
                cancellationToken)
            .ConfigureAwait(false);

        if (conversation is null)
        {
            return null;
        }

        var isFirstMessage = !await dbContext.ConversationMessages
            .AnyAsync(m => m.TenantId == tenantId && m.ConversationId == conversationId, cancellationToken)
            .ConfigureAwait(false);

        var now = clock.UtcNow;
        var message = new ConversationMessage
        {
            TenantId = tenantId,
            ConversationId = conversationId,
            Role = request.Role,
            Kind = request.Kind,
            Markdown = request.Markdown,
            CitationsJson = request.CitationsJson,
            ActionsJson = request.ActionsJson,
            ModelId = request.ModelId,
            PromptVersion = request.PromptVersion,
            InputHash = request.InputHash,
            PayloadJson = request.PayloadJson,
            InterviewJson = request.InterviewJson,
            CreatedAt = now,
        };

        // R-CONV-01 "A conversation has a title (first question, <= 48 chars)": a "question" is
        // the user's own turn, so a system-authored first turn (there is none today, but nothing
        // rules one out for a future onboarding message) never sets it — the title stays
        // DefaultTitle until a real You turn arrives.
        if (isFirstMessage && request.Role == ConversationRole.You)
        {
            conversation.Title = DeriveTitle(request.Markdown);
        }

        conversation.UpdatedAt = now;

        dbContext.ConversationMessages.Add(message);
        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        await auditWriter.WriteAsync(
            new AuditEntry(
                tenantId,
                userId,
                AuditMessageAppendedAction,
                AuditConversationMessageResourceType,
                message.Id.Value.ToString(),
                now,
                $"conversationId={conversationId} role={request.Role} kind={request.Kind} " +
                $"citationCount={CountJsonArrayItems(request.CitationsJson)} " +
                $"actionCount={CountJsonArrayItems(request.ActionsJson)} " +
                $"hasModelId={request.ModelId is not null}"),
            cancellationToken).ConfigureAwait(false);

        return ToMessageResult(message);
    }

    /// <summary>
    /// One message of the caller's own conversation (ADR-030: the interview turn an answer refers
    /// to). Null when the conversation is not this user's, or the message is not in it — the
    /// caller cannot tell the two apart, by design.
    /// </summary>
    public async Task<ConversationMessageResult?> GetMessageAsync(
        TenantId tenantId,
        string userId,
        EntityId conversationId,
        EntityId messageId,
        CancellationToken cancellationToken = default)
    {
        using var tenantScope = tenantContext.BeginScope(tenantId);

        var owned = await dbContext.Conversations
            .AsNoTracking()
            .AnyAsync(c => c.TenantId == tenantId && c.UserId == userId && c.Id == conversationId, cancellationToken)
            .ConfigureAwait(false);

        if (!owned)
        {
            return null;
        }

        var message = await dbContext.ConversationMessages
            .AsNoTracking()
            .SingleOrDefaultAsync(
                m => m.TenantId == tenantId && m.ConversationId == conversationId && m.Id == messageId,
                cancellationToken)
            .ConfigureAwait(false);

        return message is null ? null : ToMessageResult(message);
    }

    /// <summary>
    /// Marks a single-use interview option (a consent) as taken (ADR-030). Returns
    /// <see cref="InterviewConsumeOutcome.AlreadyConsumed"/> when the row was already stamped —
    /// the endpoint's 409 — so an authorization can never be replayed.
    /// </summary>
    public async Task<InterviewConsumeOutcome> MarkInterviewConsumedAsync(
        TenantId tenantId,
        string userId,
        EntityId conversationId,
        EntityId messageId,
        string optionKey,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(optionKey);

        using var tenantScope = tenantContext.BeginScope(tenantId);

        var owned = await dbContext.Conversations
            .AsNoTracking()
            .AnyAsync(c => c.TenantId == tenantId && c.UserId == userId && c.Id == conversationId, cancellationToken)
            .ConfigureAwait(false);

        if (!owned)
        {
            return InterviewConsumeOutcome.NotFound;
        }

        var message = await dbContext.ConversationMessages
            .SingleOrDefaultAsync(
                m => m.TenantId == tenantId && m.ConversationId == conversationId && m.Id == messageId,
                cancellationToken)
            .ConfigureAwait(false);

        if (message is null || message.Kind != ConversationMessageKind.Interview)
        {
            return InterviewConsumeOutcome.NotFound;
        }

        var record = InterviewJsonCodec.TryDecodeTurn(message.InterviewJson);
        if (record is null)
        {
            return InterviewConsumeOutcome.NotFound;
        }

        if (record.ConsumedAt is not null)
        {
            return InterviewConsumeOutcome.AlreadyConsumed;
        }

        message.InterviewJson = InterviewJsonCodec.MarkConsumed(record, optionKey, clock.UtcNow);
        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return InterviewConsumeOutcome.Consumed;
    }

    /// <summary>
    /// Renames the caller's own conversation: <paramref name="customTitle"/> becomes
    /// <see cref="Conversation.CustomTitle"/> (whitespace collapsed, like <see cref="DeriveTitle"/>),
    /// and a blank one clears it so the automatic title shows again. Never bumps
    /// <see cref="Conversation.UpdatedAt"/> — a rename is not activity, and must not reorder the
    /// rail. <see langword="null"/> under the identical "not this user's conversation in this
    /// tenant" rule <see cref="GetAsync"/> documents. Writes one <c>conversation.renamed</c> audit
    /// row, whose detail says whether a name was set or cleared — never the name itself (ADR-011:
    /// a chat name can quote a supplier or a figure).
    /// </summary>
    /// <exception cref="ArgumentException">The name is longer than <see cref="TitleMaxLength"/>
    /// once whitespace is collapsed (the endpoint answers 400 before calling).</exception>
    public async Task<ConversationSummaryResult?> RenameAsync(
        TenantId tenantId,
        string userId,
        EntityId conversationId,
        string? customTitle,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(userId))
        {
            throw new ArgumentException("A user id is required.", nameof(userId));
        }

        var name = NormalizeCustomTitle(customTitle);
        if (name is { Length: > TitleMaxLength })
        {
            throw new ArgumentException(
                $"A conversation name is at most {TitleMaxLength} characters.", nameof(customTitle));
        }

        using var tenantScope = tenantContext.BeginScope(tenantId);

        var conversation = await dbContext.Conversations
            .SingleOrDefaultAsync(
                c => c.TenantId == tenantId && c.UserId == userId && c.Id == conversationId,
                cancellationToken)
            .ConfigureAwait(false);

        if (conversation is null)
        {
            return null;
        }

        conversation.CustomTitle = name;
        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        var renamedAt = clock.UtcNow;

        await auditWriter.WriteAsync(
            new AuditEntry(
                tenantId,
                userId,
                AuditConversationRenamedAction,
                AuditConversationResourceType,
                conversationId.Value.ToString(),
                renamedAt,
                name is null ? "customTitle=cleared" : "customTitle=set"),
            cancellationToken).ConfigureAwait(false);

        return ToSummary(conversation, renamedAt);
    }

    /// <summary>
    /// Takes the caller's own chat back out of the archive (the rail's "Archive": click a chat and
    /// it is back in the list): bumps <see cref="Conversation.UpdatedAt"/> to now, so it is in use
    /// again and at the top of the recent list. A chat that is not archived is returned untouched
    /// — no write, no audit row. <see langword="null"/> under the identical "not this user's
    /// conversation in this tenant" rule <see cref="GetAsync"/> documents. Writes one
    /// <c>conversation.restored</c> audit row when it did restore.
    /// </summary>
    public async Task<ConversationSummaryResult?> RestoreAsync(
        TenantId tenantId,
        string userId,
        EntityId conversationId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(userId))
        {
            throw new ArgumentException("A user id is required.", nameof(userId));
        }

        using var tenantScope = tenantContext.BeginScope(tenantId);

        var conversation = await dbContext.Conversations
            .SingleOrDefaultAsync(
                c => c.TenantId == tenantId && c.UserId == userId && c.Id == conversationId,
                cancellationToken)
            .ConfigureAwait(false);

        if (conversation is null)
        {
            return null;
        }

        var now = clock.UtcNow;
        if (!IsArchived(conversation, now))
        {
            return ToSummary(conversation, now);
        }

        var archivedSince = conversation.UpdatedAt;
        conversation.UpdatedAt = now;
        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        await auditWriter.WriteAsync(
            new AuditEntry(
                tenantId,
                userId,
                AuditConversationRestoredAction,
                AuditConversationResourceType,
                conversationId.Value.ToString(),
                now,
                $"idleDays={(int)(now - archivedSince).TotalDays}"),
            cancellationToken).ConfigureAwait(false);

        return ToSummary(conversation, now);
    }

    /// <summary>A rename's name on one line: runs of whitespace (newlines included) collapse to a
    /// single space, and a blank name is <see langword="null"/> — "no name, use the automatic
    /// title".</summary>
    public static string? NormalizeCustomTitle(string? customTitle)
    {
        if (string.IsNullOrWhiteSpace(customTitle))
        {
            return null;
        }

        return string.Join(' ', customTitle.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
    }

    /// <summary>Deletes the caller's own conversation and its messages. Returns
    /// <see langword="false"/> under the identical "not this user's conversation in this tenant"
    /// rule <see cref="GetAsync"/> documents — the endpoint turns that into 404, never 403.</summary>
    public async Task<bool> DeleteAsync(
        TenantId tenantId,
        string userId,
        EntityId conversationId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(userId))
        {
            throw new ArgumentException("A user id is required.", nameof(userId));
        }

        using var tenantScope = tenantContext.BeginScope(tenantId);

        var conversation = await dbContext.Conversations
            .SingleOrDefaultAsync(
                c => c.TenantId == tenantId && c.UserId == userId && c.Id == conversationId,
                cancellationToken)
            .ConfigureAwait(false);

        if (conversation is null)
        {
            return false;
        }

        var messages = await dbContext.ConversationMessages
            .Where(m => m.TenantId == tenantId && m.ConversationId == conversationId)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        dbContext.ConversationMessages.RemoveRange(messages);
        dbContext.Conversations.Remove(conversation);
        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        await auditWriter.WriteAsync(
            new AuditEntry(
                tenantId,
                userId,
                AuditConversationDeletedAction,
                AuditConversationResourceType,
                conversationId.Value.ToString(),
                clock.UtcNow,
                $"messageCount={messages.Count}"),
            cancellationToken).ConfigureAwait(false);

        return true;
    }

    /// <summary>
    /// Deletes every conversation scoped to any of <paramref name="contractIds"/> in this tenant,
    /// regardless of owner — used by the host's bulk document purge, which must also drop Ask chats
    /// bound to the contracts those documents built. Unscoped chats are left alone.
    /// </summary>
    public async Task<int> DeleteByScopeContractsAsync(
        TenantId tenantId,
        string actor,
        IReadOnlyCollection<EntityId> contractIds,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(actor);
        if (contractIds.Count == 0)
        {
            return 0;
        }

        using var tenantScope = tenantContext.BeginScope(tenantId);

        // Nullable element type: ScopeContractId is EntityId?, and Npgsql cannot build
        // array-contains from List<EntityId> against a nullable converted column.
        var idList = contractIds.Select(id => (EntityId?)id).ToList();
        var conversations = await dbContext.Conversations
            .Where(c => c.TenantId == tenantId && c.ScopeContractId != null && idList.Contains(c.ScopeContractId))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        if (conversations.Count == 0)
        {
            return 0;
        }

        var conversationIds = conversations.Select(c => c.Id).ToList();
        var messages = await dbContext.ConversationMessages
            .Where(m => m.TenantId == tenantId && conversationIds.Contains(m.ConversationId))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        dbContext.ConversationMessages.RemoveRange(messages);
        dbContext.Conversations.RemoveRange(conversations);
        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        await auditWriter.WriteAsync(
            new AuditEntry(
                tenantId,
                actor,
                AuditConversationDeletedAction,
                AuditConversationResourceType,
                "bulk",
                clock.UtcNow,
                $"count={conversations.Count}; messageCount={messages.Count}"),
            cancellationToken).ConfigureAwait(false);

        return conversations.Count;
    }

    /// <summary>
    /// R-CONV-01's exact rule: the first question, at most <see cref="TitleMaxLength"/>
    /// characters. Collapses embedded newlines/runs of whitespace to a single space first — a
    /// multi-line question should still read as one title line (R-CONV-02's rail shows it in a
    /// single line) — then hard-truncates; no ellipsis is appended, so the invariant is always
    /// exactly "&lt;= 48 chars", never "48 chars plus a marker".
    /// </summary>
    internal static string DeriveTitle(string markdown)
    {
        var singleLine = string.Join(
            ' ', markdown.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

        return singleLine.Length <= TitleMaxLength
            ? singleLine
            : singleLine[..TitleMaxLength];
    }

    /// <summary>Best-effort item count for the audit <c>Detail</c> line only (ADR-011: counts,
    /// never content) — <see cref="AppendConversationMessageRequest"/>'s own contract guarantees
    /// a valid JSON array string, but a count is not worth failing an otherwise-successful append
    /// over, so a malformed value honestly reports <c>-1</c> instead of throwing.</summary>
    private static int CountJsonArrayItems(string json)
    {
        try
        {
            using var document = System.Text.Json.JsonDocument.Parse(json);
            return document.RootElement.ValueKind == System.Text.Json.JsonValueKind.Array
                ? document.RootElement.GetArrayLength()
                : -1;
        }
        catch (System.Text.Json.JsonException)
        {
            return -1;
        }
    }

    private static bool IsArchived(Conversation conversation, DateTimeOffset now) =>
        conversation.UpdatedAt < now - ArchiveAfter;

    private static ConversationSummaryResult ToSummary(Conversation conversation, DateTimeOffset now) =>
        new(
            conversation.Id,
            conversation.Title,
            conversation.ScopeContractId,
            conversation.UpdatedAt,
            conversation.CustomTitle,
            IsArchived(conversation, now));

    private static ConversationMessageResult ToMessageResult(ConversationMessage message) =>
        new(
            message.Id,
            message.ConversationId,
            message.Role,
            message.Kind,
            message.Markdown,
            message.CitationsJson,
            message.ActionsJson,
            message.ModelId,
            message.PromptVersion,
            message.InputHash,
            message.CreatedAt,
            message.PayloadJson,
            message.InterviewJson);
}

public enum InterviewConsumeOutcome
{
    Consumed,
    AlreadyConsumed,
    NotFound,
}
