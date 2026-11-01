using System.Collections.Concurrent;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Raffa.AiGateway;
using Raffa.AiGateway.Contracts;
using Raffa.SharedKernel;
using Raffa.SharedKernel.Contracts;

namespace Raffa.AiFlows.ContractSituation;

/// <summary>
/// The model-backed <see cref="IContractSituationAssessor"/>. Facts first: the effective term comes
/// from <see cref="ContractTermCalculator"/>; the model (one <c>analyst</c> call,
/// <see cref="ContractSituationAgent"/>) writes only the judgement and wording; the answer must pass
/// <see cref="ContractAssessmentGuard"/> or the deterministic copy is used. An active term (notice
/// still ahead) needs no judgement beyond the deterministic copy, so the model is asked only once the
/// notice deadline has passed. Answers are cached per tenant + contract + input hash (the facts,
/// the computed situation and today) in <see cref="ContractAssessmentCache"/>, so Oggi does not call
/// the model on every load.
/// </summary>
public sealed class AiContractSituationAssessor(
    IAiGateway aiGateway,
    ContractAssessmentCache cache,
    ILogger<AiContractSituationAssessor>? logger = null) : IContractSituationAssessor
{
    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(12);

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<ContractAssessment> AssessAsync(TenantId tenantId, ContractFacts facts, DateOnly today, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(facts);
        var term = ContractTermCalculator.Compute(facts, today);
        var rules = ContractSituationCopy.Rules(facts, term);
        if (term.Status == ContractTermStatus.Active)
        {
            return rules;
        }

        var (inputJson, guardFacts) = BuildInput(facts, term, rules);
        var key = string.Create(CultureInfo.InvariantCulture, $"{tenantId.Value:N}:{facts.ContractId:N}:{today:yyyyMMdd}:{Hash(inputJson)}");
        if (cache.TryGet(key, out var cached))
        {
            return cached;
        }

        var result = await AskAsync(facts, term, rules, inputJson, guardFacts, cancellationToken).ConfigureAwait(false);
        cache.Set(key, result);
        return result;
    }

    private async Task<ContractAssessment> AskAsync(
        ContractFacts facts, EffectiveContractTerm term, ContractAssessment rules, string inputJson, ContractSituationFacts guardFacts, CancellationToken cancellationToken)
    {
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(Timeout);
            var result = await aiGateway
                .AnalyzeAsync(new AiAnalysisRequest(ContractSituationAgent.Name, ContractSituationAgent.Prompt, inputJson, ContractSituationAgent.Schema, ContractSituationAgent.Version), cts.Token)
                .ConfigureAwait(false);
            if (result.IsFailure)
            {
                logger?.LogInformation("Contract situation: model unavailable ({Error}); deterministic copy.", result.Error);
                return rules;
            }

            ContractSituationAnswer? answer;
            try
            {
                answer = JsonSerializer.Deserialize<ContractSituationAnswer>(result.Value.PayloadJson, Json);
            }
            catch (JsonException)
            {
                answer = null;
            }

            if (ContractAssessmentGuard.Check(answer, guardFacts) is { } reason)
            {
                logger?.LogWarning("Contract situation: model answer rejected by the guard ({Reason}); deterministic copy.", reason);
                return rules;
            }

            var source = !string.IsNullOrEmpty(answer!.SourceKey) && facts.Evidence.TryGetValue(answer.SourceKey, out var ev)
                ? new AssessmentSource(answer.SourceKey, ev.Doc, ev.Page > 0 ? ev.Page : null, string.IsNullOrWhiteSpace(ev.Sec) ? null : ev.Sec, ev.Quote)
                : rules.Source;
            return new ContractAssessment(
                facts.ContractId,
                term,
                new LocalizedText(answer.HeadlineEn!.Trim(), answer.HeadlineIt!.Trim()),
                ContractSituationCopy.WithCitation(new LocalizedText(answer.WhyEn!.Trim(), answer.WhyIt!.Trim()), source),
                answer.RecommendedAction!,
                answer.Urgency!,
                source,
                "ai");
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            logger?.LogWarning("Contract situation: model timed out; deterministic copy.");
            return rules;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger?.LogWarning(exception, "Contract situation: model call failed; deterministic copy.");
            return rules;
        }
    }

    /// <summary>The model's input (facts, situation, evidence, what it may write, the rules' wording)
    /// and the facts the guard checks the answer against.</summary>
    public static (string InputJson, ContractSituationFacts GuardFacts) BuildInput(ContractFacts facts, EffectiveContractTerm term, ContractAssessment rules)
    {
        ArgumentNullException.ThrowIfNull(facts);
        ArgumentNullException.ThrowIfNull(term);
        ArgumentNullException.ThrowIfNull(rules);
        object? D(DateOnly? d) => d is { } v ? new { en = ContractSituationCopy.En(v), it = ContractSituationCopy.It(v) } : null;
        var dates = new[] { term.Today, term.Start, term.End, term.NoticeDeadline, term.RenewedOn, term.LockedOn, term.OriginalEnd, term.OriginalNoticeDeadline }
            .OfType<DateOnly>().Distinct().OrderBy(d => d).ToList();
        var evidence = facts.Evidence
            .Where(kv => !string.IsNullOrWhiteSpace(kv.Value.Quote) || kv.Value.Page > 0)
            .OrderBy(kv => kv.Key, StringComparer.Ordinal)
            .Take(8)
            .Select(kv => new { key = kv.Key, doc = kv.Value.Doc, page = kv.Value.Page > 0 ? (int?)kv.Value.Page : null, section = kv.Value.Sec, quote = Clip(kv.Value.Before + kv.Value.Quote + kv.Value.After, 600) })
            .ToList();
        var numbers = new HashSet<string>(StringComparer.Ordinal);
        void Num(int? n)
        {
            if (n is { } v and >= 0)
            {
                numbers.Add(v.ToString(CultureInfo.InvariantCulture));
            }
        }

        Num(term.NoticeDays);
        Num(term.EndDays);
        Num(term.DaysSinceRenewal);
        Num(term.RenewalTermMonths);
        Num(term.NoticePeriodDays);
        Num(term.Cycles);
        Num(facts.Notice);
        foreach (var e in evidence)
        {
            Num(e.page);
            if (!string.IsNullOrWhiteSpace(e.section))
            {
                numbers.Add(e.section);
            }
        }

        if (rules.Source is { } src)
        {
            Num(src.Page);
            if (!string.IsNullOrWhiteSpace(src.Section))
            {
                numbers.Add(src.Section);
            }
        }

        var literals = new[] { facts.Supplier, facts.Name, CaseShortName(facts.Name), facts.File }
            .Concat(evidence.Select(e => e.doc))
            .Concat(evidence.Select(e => e.section))
            .Concat(rules.Source is { } s ? [s.Doc, s.Section ?? ""] : [])
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.Ordinal)
            .ToList();

        var input = new
        {
            today = D(term.Today),
            contract = new
            {
                supplier = facts.Supplier,
                name = facts.Name,
                autoRenew = term.AutoRenew,
                noticePeriodDays = term.NoticePeriodDays,
                renewalTermMonths = term.RenewalTermMonths,
            },
            situation = new
            {
                status = term.Status,
                rolledOver = term.RolledOver,
                cycles = term.Cycles,
                start = D(term.Start),
                end = D(term.End),
                noticeDeadline = D(term.NoticeDeadline),
                noticeDaysLeft = term.NoticeDays is >= 0 ? term.NoticeDays : null,
                endDaysLeft = term.EndDays is >= 0 ? term.EndDays : null,
                renewedOn = D(term.RenewedOn),
                lockedOn = D(term.LockedOn),
                originalEnd = D(term.OriginalEnd),
                originalNoticeDeadline = D(term.OriginalNoticeDeadline),
            },
            evidence,
            allowedDates = dates.Select(d => D(d)).ToList(),
            allowedNumbers = numbers.OrderBy(n => n, StringComparer.Ordinal).ToList(),
            allowedActions = SituationAction.AllowedFor(term.Status),
            rules = new
            {
                headlineEn = rules.Headline.En,
                headlineIt = rules.Headline.It,
                whyEn = rules.Why.En,
                whyIt = rules.Why.It,
                recommendedAction = rules.RecommendedAction,
                urgency = rules.Urgency,
                sourceKey = rules.Source is { } rs && facts.Evidence.ContainsKey(rs.Key) ? rs.Key : "",
            },
        };

        var json = JsonSerializer.Serialize(input, Json);
        return (json, new ContractSituationFacts(term.Status, dates, numbers, facts.Evidence.Keys.ToList(), literals));
    }

    private static string CaseShortName(string name) => name.Split(" — ")[0];

    private static string Clip(string s, int max) => s.Length <= max ? s : s[..max];

    private static string Hash(string s) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(ContractSituationAgent.Version + "\n" + s)))[..32];
}

/// <summary>Assessments by tenant + contract + input hash + day (process-wide; bounded).</summary>
public sealed class ContractAssessmentCache
{
    public const int MaxEntries = 5000;

    private readonly ConcurrentDictionary<string, ContractAssessment> _entries = new(StringComparer.Ordinal);

    public bool TryGet(string key, out ContractAssessment assessment) => _entries.TryGetValue(key, out assessment!);

    public void Set(string key, ContractAssessment assessment)
    {
        if (_entries.Count >= MaxEntries)
        {
            _entries.Clear();
        }

        _entries[key] = assessment;
    }

    public int Count => _entries.Count;
}

public static class ContractSituationFlowServiceCollectionExtensions
{
    /// <summary>The model-backed situation assessor replaces the modules' deterministic default.</summary>
    public static IServiceCollection AddContractSituationFlow(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.TryAddSingleton<ContractAssessmentCache>();
        services.Replace(ServiceDescriptor.Scoped<IContractSituationAssessor, AiContractSituationAssessor>());
        return services;
    }
}
