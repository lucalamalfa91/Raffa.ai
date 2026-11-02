using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Raffa.AiGateway;
using Raffa.AiGateway.Contracts;
using Raffa.SharedKernel;
using Raffa.SharedKernel.Renewals;
using Raffa.SharedKernel.Tenancy;

namespace Raffa.AiFlows.CompanyRules;

/// <summary>
/// The model-backed <see cref="ICompanyRulesAssistant"/>: one <c>analyst</c> call per interview turn or
/// per uploaded policy. Anything the model cannot do (unavailable, timed out, off-schema, an empty
/// proposal) falls back to the <see cref="ScriptedCompanyRulesAssistant"/>, so the screen always answers.
/// </summary>
public sealed class AiCompanyRulesAssistant(
    IAiGateway aiGateway,
    ITenantContext tenantContext,
    ILogger<AiCompanyRulesAssistant>? logger = null) : ICompanyRulesAssistant
{
    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);

    /// <summary>A policy longer than this is cut before it is sent (the head of a policy carries its rules).</summary>
    public const int MaxDocumentChars = 60_000;

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly ScriptedCompanyRulesAssistant _scripted = new();

    private sealed record InterviewAnswer(string? Message, string? Proposal, int? StartWindowDays, bool? Done);

    private sealed record DistillAnswer(string? Rules, int? StartWindowDays, string? Summary);

    public async Task<RuleInterviewReply> InterviewAsync(
        TenantId tenantId, string currentRules, IReadOnlyList<RuleTurn> history, string lang, CancellationToken cancellationToken = default)
    {
        var input = JsonSerializer.Serialize(
            new { language = lang, currentRules, conversation = history.Select(t => new { role = t.Role, text = t.Text }) }, Json);
        var payload = await CallAsync(tenantId, CompanyRulesAgents.InterviewerName, CompanyRulesAgents.InterviewerPrompt, input,
            CompanyRulesAgents.InterviewerSchema, CompanyRulesAgents.InterviewerVersion, cancellationToken).ConfigureAwait(false);
        var answer = Parse<InterviewAnswer>(payload);
        if (answer is { Message: { Length: > 0 } message })
        {
            var proposal = answer.Proposal?.Trim();
            var done = answer.Done == true && !string.IsNullOrWhiteSpace(proposal);
            if (!done || proposal!.Length <= CompanyRulesDefaults.MaxTextLength)
            {
                return new RuleInterviewReply(
                    message.Trim(),
                    done ? proposal : null,
                    done && answer.StartWindowDays is > 0 ? CompanyRulesDefaults.ClampStartWindow(answer.StartWindowDays.Value) : null,
                    done);
            }
        }

        return await _scripted.InterviewAsync(tenantId, currentRules, history, lang, cancellationToken).ConfigureAwait(false);
    }

    public async Task<RuleDistillation> DistillAsync(
        TenantId tenantId, string fileName, string documentText, string currentRules, string lang, CancellationToken cancellationToken = default)
    {
        var clipped = documentText.Length > MaxDocumentChars ? documentText[..MaxDocumentChars] : documentText;
        var input = JsonSerializer.Serialize(new { language = lang, fileName, currentRules, document = clipped }, Json);
        var payload = await CallAsync(tenantId, CompanyRulesAgents.DistillerName, CompanyRulesAgents.DistillerPrompt, input,
            CompanyRulesAgents.DistillerSchema, CompanyRulesAgents.DistillerVersion, cancellationToken).ConfigureAwait(false);
        var answer = Parse<DistillAnswer>(payload);
        if (answer is { Rules: { Length: > 0 } rules, Summary: { Length: > 0 } summary } && rules.Length <= CompanyRulesDefaults.MaxTextLength)
        {
            return new RuleDistillation(
                rules.Trim(),
                answer.StartWindowDays is > 0 ? CompanyRulesDefaults.ClampStartWindow(answer.StartWindowDays.Value) : null,
                summary.Trim());
        }

        return await _scripted.DistillAsync(tenantId, fileName, documentText, currentRules, lang, cancellationToken).ConfigureAwait(false);
    }

    private async Task<string?> CallAsync(TenantId tenantId, string name, string prompt, string input, string schema, string version, CancellationToken cancellationToken)
    {
        try
        {
            // The gateway refuses an AI call with no ambient tenant (ADR-011): open this call's own scope
            // rather than trust the caller's is still active.
            using var tenantScope = tenantContext.BeginScope(tenantId);
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(Timeout);
            var result = await aiGateway.AnalyzeAsync(new AiAnalysisRequest(name, prompt, input, schema, version), cts.Token).ConfigureAwait(false);
            if (result.IsFailure)
            {
                logger?.LogInformation("Company rules: model unavailable ({Error}); scripted assistant.", result.Error);
                return null;
            }

            return result.Value.PayloadJson;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            logger?.LogWarning("Company rules: model timed out; scripted assistant.");
            return null;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger?.LogWarning(exception, "Company rules: model call failed; scripted assistant.");
            return null;
        }
    }

    private static T? Parse<T>(string? payload)
        where T : class
    {
        if (string.IsNullOrWhiteSpace(payload))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<T>(payload, Json);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}

public static class CompanyRulesFlowServiceCollectionExtensions
{
    /// <summary>The model-backed company-rules assistant replaces the scripted default.</summary>
    public static IServiceCollection AddCompanyRulesFlow(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.Replace(ServiceDescriptor.Scoped<ICompanyRulesAssistant, AiCompanyRulesAssistant>());
        return services;
    }
}
