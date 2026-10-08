using System.Text.Json;
using System.Text.Json.Serialization;
using Raffa.AiGateway.Agents;
using Raffa.AiGateway.Foundry;

namespace Raffa.AiGateway.Tests.Agents;

/// <summary>
/// Plan A-01: the schema of an agent's output is derived from its C# type and is born inside the
/// strict subset Azure accepts (<see cref="StrictJsonSchemaValidator"/>), so the type, the wire and
/// the schema cannot disagree and a refusal-on-every-call schema cannot be written by accident.
/// </summary>
public sealed class AgentSchemaTests
{
    public enum Lever { MarketDiscount, UpliftCap }

    public sealed record Line(string Title, decimal Amount, int Rank, double? Share, bool Flag);

    public sealed record Rich(
        string Title,
        string? Note,
        IReadOnlyList<string> Keys,
        IReadOnlyList<Line> Lines,
        Lever Lever,
        Lever? OptionalLever,
        DateOnly Day,
        [property: JsonPropertyName("renamed")] string Original,
        [property: JsonIgnore] string Hidden = "x");

    public sealed record WithDictionary(Dictionary<string, string> Map);

    public sealed record Recursive(string Name, Recursive? Child);

    public sealed record Open(JsonElement Anything);

    private static JsonElement Schema<T>() => JsonDocument.Parse(AgentSchema.For<T>()).RootElement;

    [Fact]
    public void A_derived_schema_passes_the_strict_validator()
    {
        var result = StrictJsonSchemaValidator.Validate(AgentSchema.For<Rich>());

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error : null);
    }

    [Fact]
    public void Every_object_lists_all_its_properties_as_required_and_forbids_additional_ones()
    {
        var schema = Schema<Rich>();

        Assert.Equal("object", schema.GetProperty("type").GetString());
        Assert.False(schema.GetProperty("additionalProperties").GetBoolean());
        var required = schema.GetProperty("required").EnumerateArray().Select(e => e.GetString()).ToList();
        Assert.Equal(
            ["title", "note", "keys", "lines", "lever", "optionalLever", "day", "renamed"],
            required);

        var line = schema.GetProperty("properties").GetProperty("lines").GetProperty("items");
        Assert.False(line.GetProperty("additionalProperties").GetBoolean());
        Assert.Equal(5, line.GetProperty("required").GetArrayLength());
    }

    [Fact]
    public void Types_map_to_json_schema_types_without_format_or_pattern()
    {
        var properties = Schema<Rich>().GetProperty("properties");
        var line = properties.GetProperty("lines").GetProperty("items").GetProperty("properties");

        Assert.Equal("string", properties.GetProperty("title").GetProperty("type").GetString());
        Assert.Equal("array", properties.GetProperty("keys").GetProperty("type").GetString());
        Assert.Equal("string", properties.GetProperty("keys").GetProperty("items").GetProperty("type").GetString());
        Assert.Equal("number", line.GetProperty("amount").GetProperty("type").GetString());
        Assert.Equal("integer", line.GetProperty("rank").GetProperty("type").GetString());
        Assert.Equal("boolean", line.GetProperty("flag").GetProperty("type").GetString());
        // A date is a string; the "format": "date" the BCL exporter adds is refused by Azure strict mode.
        Assert.Equal("string", properties.GetProperty("day").GetProperty("type").GetString());
        Assert.False(properties.GetProperty("day").TryGetProperty("format", out _));
        Assert.DoesNotContain("pattern", AgentSchema.For<Rich>(), StringComparison.Ordinal);
    }

    [Fact]
    public void Nullable_values_are_a_type_union_with_null()
    {
        var properties = Schema<Rich>().GetProperty("properties");
        var share = properties.GetProperty("lines").GetProperty("items").GetProperty("properties").GetProperty("share");

        Assert.Equal(["string", "null"], properties.GetProperty("note").GetProperty("type").EnumerateArray().Select(e => e.GetString()));
        Assert.Equal(["number", "null"], share.GetProperty("type").EnumerateArray().Select(e => e.GetString()));
        // A non-nullable reference type is not nullable.
        Assert.Equal(JsonValueKind.String, properties.GetProperty("title").GetProperty("type").ValueKind);
    }

    [Fact]
    public void Enums_are_string_enums_and_a_nullable_enum_never_puts_null_in_the_enum()
    {
        var properties = Schema<Rich>().GetProperty("properties");

        var lever = properties.GetProperty("lever");
        Assert.Equal("string", lever.GetProperty("type").GetString());
        Assert.Equal(["MarketDiscount", "UpliftCap"], lever.GetProperty("enum").EnumerateArray().Select(e => e.GetString()));

        var optional = properties.GetProperty("optionalLever").GetProperty("anyOf");
        Assert.Equal(2, optional.GetArrayLength());
        Assert.Equal("null", optional[1].GetProperty("type").GetString());
    }

    [Fact]
    public void Property_names_follow_the_wire_options_and_ignored_properties_are_left_out()
    {
        var properties = Schema<Rich>().GetProperty("properties");

        Assert.True(properties.TryGetProperty("renamed", out _));
        Assert.False(properties.TryGetProperty("original", out _));
        Assert.False(properties.TryGetProperty("hidden", out _));
        Assert.True(properties.TryGetProperty("optionalLever", out _));
    }

    [Fact]
    public void A_payload_that_matches_the_schema_deserializes_into_the_type()
    {
        var value = new Rich(
            "t", null, ["a"], [new Line("l", 12.5m, 1, null, true)], Lever.UpliftCap, null, new DateOnly(2026, 10, 8), "o");

        var json = JsonSerializer.Serialize(value, AgentJson.Options);
        var back = JsonSerializer.Deserialize<Rich>(json, AgentJson.Options);

        Assert.NotNull(back);
        Assert.Equal(Lever.UpliftCap, back.Lever);
        Assert.Equal("o", back.Original);
        // Every property the type writes is a property the schema declares, and the reverse.
        var written = JsonDocument.Parse(json).RootElement.EnumerateObject().Select(p => p.Name).Order().ToList();
        var declared = Schema<Rich>().GetProperty("properties").EnumerateObject().Select(p => p.Name).Order().ToList();
        Assert.Equal(declared, written);
    }

    [Fact]
    public void Open_ended_shapes_are_refused_with_the_path()
    {
        var dictionary = Assert.Throws<NotSupportedException>(() => AgentSchema.For<WithDictionary>());
        Assert.Contains("$.map", dictionary.Message, StringComparison.Ordinal);

        Assert.Throws<NotSupportedException>(() => AgentSchema.For<Open>());
        var recursive = Assert.Throws<NotSupportedException>(() => AgentSchema.For<Recursive>());
        Assert.Contains("recursive", recursive.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void The_root_must_be_an_object()
    {
        Assert.Throws<NotSupportedException>(() => AgentSchema.For<string>());
        Assert.Throws<NotSupportedException>(() => AgentSchema.For<List<string>>());
    }

    [Fact]
    public void The_derived_schema_is_deterministic()
    {
        Assert.Equal(AgentSchema.For<Rich>(), AgentSchema.For<Rich>());
    }
}
