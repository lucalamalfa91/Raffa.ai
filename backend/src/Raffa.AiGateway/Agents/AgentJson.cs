using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace Raffa.AiGateway.Agents;

/// <summary>
/// The one JSON shape every agent step is serialized and deserialized with (plan A-01): camelCase
/// (<see cref="JsonSerializerDefaults.Web"/>), enums as strings, nothing omitted. It is exactly the
/// option set <c>NegotiationCouncil</c> and <c>NegotiationDraftingWorkflow</c> already use, so a
/// flow migrated onto the runner sends a byte-identical <c>InputJson</c> (plan section 7, metric 1:
/// payloads identical up to key order), and <see cref="AgentSchema"/> derives the schema the model
/// is held to from the same options, so the type, the wire and the schema cannot disagree.
/// </summary>
public static class AgentJson
{
    /// <summary>Immutable and thread-safe; share it, never copy-and-mutate it.</summary>
    public static JsonSerializerOptions Options { get; } = Create();

    private static JsonSerializerOptions Create()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            DefaultIgnoreCondition = JsonIgnoreCondition.Never,
            TypeInfoResolver = new DefaultJsonTypeInfoResolver(),
        };
        options.Converters.Add(new JsonStringEnumConverter());
        options.MakeReadOnly();
        return options;
    }
}
