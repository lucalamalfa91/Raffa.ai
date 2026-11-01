using System.Globalization;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Raffa.AiGateway;
using Raffa.AiGateway.Contracts;
using Raffa.Savings.Application.Results;

namespace Raffa.AiFlows.SavingsGoals;

/// <summary>
/// AskRaffa 4.0 Risultati goal parser (BACKEND-SPEC §5 LLM task 5, "free-text goal parser →
/// {pct, annual, scope, period}"): one schema-constrained <c>extract</c> call reads the user's words
/// against the workspace's validated suppliers and categories; the scope is then applied to the
/// contracts by <see cref="DeterministicSavingsGoalParser.BuildDraft"/>, so the draft has the same shape
/// and texts as the prototype. Any gateway failure or malformed answer falls back to the
/// deterministic reading (the prototype's <c>parseGoal</c>). The model never computes money: amounts
/// it returns are the user's own figures; the spend base comes from the validated contracts.
/// </summary>
public sealed class LlmSavingsGoalParser(IAiGateway aiGateway, ILogger<LlmSavingsGoalParser>? logger = null) : ISavingsGoalParser
{
    public const string StageName = "savings-goal-parse";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<SavingsGoalDraft?> ParseAsync(string text, IReadOnlyList<ResultsContract> validated, DateOnly today, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        try
        {
            var result = await aiGateway
                .ExtractAsync(new AiExtractionRequest(StageName, Prompt(text, validated, today), Schema()), cancellationToken)
                .ConfigureAwait(false);
            if (result.IsSuccess && Read(result.Value.PayloadJson, validated) is { } interpretation)
            {
                return DeterministicSavingsGoalParser.BuildDraft(text, interpretation, validated, today, "llm");
            }

            logger?.LogInformation("Savings goal parse: model answer unusable, deterministic fallback.");
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger?.LogWarning(exception, "Savings goal parse failed; deterministic fallback.");
        }

        return DeterministicSavingsGoalParser.Parse(text, validated, today);
    }

    private static string Prompt(string text, IReadOnlyList<ResultsContract> validated, DateOnly today)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Read a procurement savings goal written by the user in their own words (Italian or English).");
        sb.AppendLine(CultureInfo.InvariantCulture, $"Today is {today:yyyy-MM-dd}; the fiscal year is the calendar year.");
        sb.AppendLine("Return the percentage and/or the annual amount the user stated (never invent one), the scope and the period.");
        sb.AppendLine("Scope: a supplier only if the user names one of the suppliers below; otherwise the categories the goal is about");
        sb.AppendLine("(\"IT\" / software / SaaS = CRM, Productivity and Cloud infrastructure); null when the goal covers every contract.");
        sb.AppendLine("Suppliers: " + string.Join(", ", validated.Select(c => c.Supplier).Distinct()));
        sb.AppendLine("Categories: " + string.Join(", ", validated.Select(c => c.Category).OfType<string>().Concat(DeterministicSavingsGoalParser.ItCategories).Distinct()));
        sb.AppendLine("Goal text:");
        sb.AppendLine(text);
        return sb.ToString();
    }

    private static string Schema() => JsonSerializer.Serialize(new
    {
        type = "object",
        properties = new
        {
            pct = new { type = new[] { "number", "null" }, description = "The percentage the user wants to save (20 for \"20%\"), or null if not stated." },
            annual = new { type = new[] { "number", "null" }, description = "The yearly amount the user wants to save in currency units (300000 for \"CHF 300k\"), or null if not stated." },
            supplier = new { type = new[] { "string", "null" }, description = "One of the listed suppliers, exactly as listed, or null." },
            categories = new { type = new[] { "array", "null" }, items = new { type = "string" }, description = "Categories from the list, or null for all contracts." },
            period = new { type = "string", @enum = new[] { "current", "next", "quarter" }, description = "current fiscal year, next fiscal year, or this quarter." },
        },
        required = new[] { "pct", "annual", "supplier", "categories", "period" },
        additionalProperties = false,
    });

    private static SavingsGoalInterpretation? Read(string payload, IReadOnlyList<ResultsContract> validated)
    {
        Answer? answer;
        try
        {
            answer = JsonSerializer.Deserialize<Answer>(payload, Json);
        }
        catch (JsonException)
        {
            return null;
        }

        if (answer is null || answer.Period is not ("current" or "next" or "quarter")) return null;
        var supplier = answer.Supplier is { } s
            ? validated.Select(c => c.Supplier).FirstOrDefault(x => string.Equals(x, s, StringComparison.OrdinalIgnoreCase))
            : null;
        var known = validated.Select(c => c.Category).OfType<string>().Concat(DeterministicSavingsGoalParser.ItCategories).ToHashSet(StringComparer.Ordinal);
        var cats = answer.Categories?.Where(known.Contains).Distinct().ToList();
        return new SavingsGoalInterpretation(
            answer.Pct is > 0 and <= 100 ? answer.Pct : null,
            answer.Annual is > 0 ? answer.Annual : null,
            supplier,
            cats is { Count: > 0 } ? cats : null,
            null,
            answer.Period);
    }

    private sealed record Answer(decimal? Pct, decimal? Annual, string? Supplier, List<string>? Categories, string? Period);
}

public static class SavingsGoalFlowServiceCollectionExtensions
{
    /// <summary>LLM-backed goal parser (TryAdd: fixture mode registers the deterministic one first).</summary>
    public static IServiceCollection AddSavingsGoalFlow(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.TryAddScoped<ISavingsGoalParser, LlmSavingsGoalParser>();
        return services;
    }
}
