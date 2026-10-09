namespace Raffa.AiGateway.Jev;

/// <summary>Jev "leans toward the option that comes first" in a Choice
/// (https://docs.typesafe.ai/model-jaggedness/jev-1.13: "reorder the options and check the answer is
/// consistent"). The same question is asked a second time with the options reversed; the callers
/// accept an answer only when both orders agree.</summary>
public static class JevOptionOrder
{
    /// <summary>The criteria in the opposite order. <see cref="Dictionary{TKey,TValue}"/> enumerates
    /// (and the JSON serializer writes) in insertion order, so the reversed copy is built by
    /// inserting in reverse.</summary>
    public static IReadOnlyDictionary<string, string> Reversed(IReadOnlyDictionary<string, string> criteria)
    {
        var reversed = new Dictionary<string, string>(criteria.Count);
        foreach (var pair in criteria.Reverse())
        {
            reversed.Add(pair.Key, pair.Value);
        }

        return reversed;
    }
}
