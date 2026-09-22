using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Raffa.Chat.Application.Capabilities;
using Raffa.Chat.Application.Conversations;
using Raffa.Chat.Application.Reply;
using Raffa.Chat.Domain.Conversations;
using Raffa.Chat.Domain.Feedback;
using Raffa.Chat.Infrastructure;
using Raffa.SharedKernel;
using Raffa.SharedKernel.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace Raffa.Chat.Application.Feedback;

/// <summary>Why a submission was refused, or that it succeeded.</summary>
public enum FeedbackSubmitStatus
{
    Success,

    /// <summary>Not this user's conversation in this tenant — 404, never 403.</summary>
    NotFound,

    /// <summary>The message is not a Raffa turn of this conversation carrying a feedback offer — 400.</summary>
    InvalidMessage,

    /// <summary>A feature request already exists for this message — 409, with the existing row.</summary>
    AlreadySubmitted,
}

/// <summary>The stored row's wire-facing facts.</summary>
public sealed record FeatureRequestResult(EntityId FeedbackId, string Status, int? IssueNumber, string? IssueUrl);

/// <summary>What <see cref="FeedbackService.SubmitAsync"/> returns: the status, the stored row
/// when there is one, and — on success only — the confirmation reply the host appends to the
/// conversation and returns as the new turn.</summary>
public sealed record FeedbackSubmitResult(
    FeedbackSubmitStatus Status,
    FeatureRequestResult? Request,
    CopilotReply? Confirmation);

/// <summary>
/// The feedback loop's one write path (ADR-030 D5): validates that the answered turn really
/// carried an offer, stores the request first, publishes it best-effort, then builds the
/// confirmation turn. Store-first means a GitHub outage loses nothing; best-effort means the user
/// always gets a thank-you, with an issue link when one exists. The audit row carries the gap key
/// and the outcome — never the answers (ADR-011).
/// </summary>
public sealed class FeedbackService(
    ChatDbContext dbContext,
    ConversationService conversations,
    IFeatureRequestPublisher publisher,
    FeedbackOptions options,
    ITenantContext tenantContext,
    IClock clock,
    IAuditWriter auditWriter)
{
    public const string AuditSubmittedAction = "conversation.feedback.submitted";
    private const string AuditResourceType = "feature_request";

    public async Task<FeedbackSubmitResult> SubmitAsync(
        TenantId tenantId,
        string userId,
        EntityId conversationId,
        EntityId messageId,
        FeedbackAnswers answers,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);
        ArgumentNullException.ThrowIfNull(answers);

        using var tenantScope = tenantContext.BeginScope(tenantId);

        var conversation = await conversations.GetAsync(tenantId, userId, conversationId, cancellationToken).ConfigureAwait(false);
        if (conversation is null)
        {
            return new FeedbackSubmitResult(FeedbackSubmitStatus.NotFound, null, null);
        }

        var message = conversation.Messages.FirstOrDefault(m => m.MessageId == messageId && m.Role == ConversationRole.Raffa);
        var offer = ReplyPayloadJson.Deserialize(message?.PayloadJson);
        if (message is null || offer?.Gap is null || offer.FeedbackOffer is null)
        {
            return new FeedbackSubmitResult(FeedbackSubmitStatus.InvalidMessage, null, null);
        }

        var existing = await dbContext.FeatureRequests
            .SingleOrDefaultAsync(r => r.TenantId == tenantId && r.MessageId == messageId, cancellationToken)
            .ConfigureAwait(false);
        if (existing is not null)
        {
            return new FeedbackSubmitResult(FeedbackSubmitStatus.AlreadySubmitted, ToResult(existing), null);
        }

        var gap = offer.Gap;
        var now = clock.UtcNow;
        var request = new FeatureRequest
        {
            TenantId = tenantId,
            ConversationId = conversationId,
            MessageId = messageId,
            UserId = userId,
            GapKey = gap.Key,
            GapTitle = gap.Title,
            Language = gap.Language,
            AnswersJson = JsonSerializer.Serialize(answers, JsonSerializerOptions.Web),
            Environment = options.Environment,
            WorkspaceHash = WorkspaceHash(tenantId),
            Status = FeatureRequestStatus.Recorded,
            CreatedAt = now,
        };

        dbContext.FeatureRequests.Add(request);
        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        // Best-effort publish: a failing or missing publisher never fails the submission.
        var issue = new FeatureRequestIssue(gap.Key, gap.Title, gap.Language, options.Environment, request.WorkspaceHash, answers);
        FeatureRequestPublishResult publish;
        try
        {
            publish = publisher.IsConfigured
                ? await publisher.TryPublishAsync(issue, cancellationToken).ConfigureAwait(false)
                : FeatureRequestPublishResult.Failed("no feature-request publisher is configured.");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            publish = FeatureRequestPublishResult.Failed(ex.GetType().Name + ": " + ex.Message);
        }

        if (publish.Published && publish.IssueNumber is { } issueNumber && publish.IssueUrl is { } issueUrl)
        {
            request.Status = FeatureRequestStatus.IssueOpened;
            request.IssueNumber = issueNumber;
            request.IssueUrl = issueUrl;
        }
        else if (publisher.IsConfigured)
        {
            request.Status = FeatureRequestStatus.IssueFailed;
            request.PublishError = Truncate(publish.Failure ?? "unknown failure", 500);
        }

        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        await auditWriter.WriteAsync(
            new AuditEntry(
                tenantId,
                userId,
                AuditSubmittedAction,
                AuditResourceType,
                request.Id.Value.ToString(),
                now,
                $"conversationId={conversationId} messageId={messageId} gapKey={gap.Key} status={request.Status} " +
                $"issueNumber={(request.IssueNumber?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "none")}"),
            cancellationToken).ConfigureAwait(false);

        return new FeedbackSubmitResult(FeedbackSubmitStatus.Success, ToResult(request), BuildConfirmation(request, messageId));
    }

    /// <summary>The confirmation turn: an <see cref="ReplyKind.Answer"/> with no citation, one
    /// <see cref="CopilotActionKind.External"/> action when an issue exists, and the
    /// <see cref="FeedbackResult"/> payload that tells a resumed conversation which offer was
    /// already answered.</summary>
    private static CopilotReply BuildConfirmation(FeatureRequest request, EntityId messageId)
    {
        var wireStatus = ToWireStatus(request.Status);
        var actions = request.IssueUrl is not null && request.IssueNumber is { } number
            ? new[] { CopilotAction.External(FeedbackReplyCopy.OpenIssueLabel(request.Language, number), request.IssueUrl) }
            : [];

        return new CopilotReply(
            ReplyKind.Answer,
            FeedbackReplyCopy.Markdown(request.Language, request.GapTitle, wireStatus == FeatureRequestStatus.IssueOpened ? request.IssueNumber : null),
            [],
            actions,
            ReplyProvenance.NoModelCall([]),
            [],
            new ReplyPayload(FeedbackResult: new FeedbackResult(
                messageId.Value.ToString(), wireStatus, request.IssueNumber, request.IssueUrl)));
    }

    private static FeatureRequestResult ToResult(FeatureRequest request) =>
        new(request.Id, ToWireStatus(request.Status), request.IssueNumber, request.IssueUrl);

    /// <summary>A publish failure is served as "recorded" — the row still holds the reason.</summary>
    public static string ToWireStatus(string status) =>
        status == FeatureRequestStatus.IssueOpened ? FeatureRequestStatus.IssueOpened : FeatureRequestStatus.Recorded;

    /// <summary>The first eight hex characters of SHA-256 over the tenant id — groups reports
    /// from one workspace without ever naming it.</summary>
    public static string WorkspaceHash(TenantId tenantId) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(tenantId.Value.ToString())))[..8].ToLowerInvariant();

    private static string Truncate(string text, int max) => text.Length <= max ? text : text[..max];
}
