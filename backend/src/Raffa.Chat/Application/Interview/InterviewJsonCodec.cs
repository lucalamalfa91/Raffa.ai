using System.Text.Json;
using System.Text.Json.Serialization;
using Raffa.SharedKernel;

namespace Raffa.Chat.Application.Interview;

/// <summary>The persisted shape of an interview turn (<c>conversation_message.interview_json</c>
/// on a Raffa row). <see cref="ConsumedAt"/>/<see cref="ConsumedOptionKey"/> make a consent option
/// single-use.</summary>
public sealed record InterviewTurnRecord(
    int Version,
    string Prompt,
    IReadOnlyList<InterviewQuestion> Questions,
    DateTimeOffset? ConsumedAt = null,
    string? ConsumedOptionKey = null)
{
    public InterviewTurn ToTurn() => new(Prompt, Questions);

    public InterviewQuestion? FindQuestion(string key) =>
        Questions.FirstOrDefault(q => string.Equals(q.Key, key, StringComparison.Ordinal));
}

/// <summary>The persisted shape of the You row that answered an interview.</summary>
public sealed record InterviewAnswerRecord(int Version, Guid AnswerTo, string QuestionKey, string? OptionKey, bool FreeText);

/// <summary>
/// JSON in and out of <c>interview_json</c>. One codec, so the endpoint, the service and the tests
/// agree byte for byte; camelCase, enums as strings, nulls omitted.
/// </summary>
public static class InterviewJsonCodec
{
    public const int Version = 1;

    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static string SerializeTurn(InterviewTurn turn)
    {
        ArgumentNullException.ThrowIfNull(turn);
        return JsonSerializer.Serialize(new InterviewTurnRecord(Version, turn.Prompt, turn.Questions), Options);
    }

    public static string SerializeRecord(InterviewTurnRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);
        return JsonSerializer.Serialize(record, Options);
    }

    public static InterviewTurnRecord? TryDecodeTurn(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            var record = JsonSerializer.Deserialize<InterviewTurnRecord>(json, Options);
            return record is { Questions.Count: > 0 } ? record : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public static string SerializeAnswer(EntityId answerTo, string questionKey, string? optionKey, bool freeText) =>
        JsonSerializer.Serialize(new InterviewAnswerRecord(Version, answerTo.Value, questionKey, optionKey, freeText), Options);

    public static InterviewAnswerRecord? TryDecodeAnswer(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<InterviewAnswerRecord>(json, Options);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public static string MarkConsumed(InterviewTurnRecord record, string optionKey, DateTimeOffset at)
    {
        ArgumentNullException.ThrowIfNull(record);
        ArgumentException.ThrowIfNullOrWhiteSpace(optionKey);
        return SerializeRecord(record with { ConsumedAt = at, ConsumedOptionKey = optionKey });
    }
}
