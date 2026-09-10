using System.Text.Json;
using Contigo.SharedKernel;

namespace Contigo.AiGateway.Foundry;

/// <summary>
/// Checks a JSON Schema against the subset Azure OpenAI structured outputs accept in strict mode
/// (Microsoft Learn, "How to use structured outputs" — supported schemas): every object declares
/// <c>additionalProperties: false</c> and lists <em>all</em> of its properties in <c>required</c>
/// (nullability is expressed as <c>["type", "null"]</c>), no numeric/string/array bound keywords
/// (<c>minimum</c>, <c>format</c>, <c>minItems</c> ...), no <c>null</c> inside an <c>enum</c>, and
/// an object at the root. A schema that violates one of these is refused by Azure with an opaque
/// 400 on every call, so <see cref="FoundryExtractClient"/> validates the caller's schema here first
/// and names the offending path; every domain schema builder has a unit test over this validator.
/// </summary>
public static class StrictJsonSchemaValidator
{
    private static readonly HashSet<string> UnsupportedKeywords = new(StringComparer.Ordinal)
    {
        "minimum", "maximum", "exclusiveMinimum", "exclusiveMaximum", "multipleOf",
        "minLength", "maxLength", "pattern", "format",
        "minItems", "maxItems", "uniqueItems", "contains", "minContains", "maxContains", "unevaluatedItems",
        "minProperties", "maxProperties", "patternProperties", "propertyNames", "unevaluatedProperties",
    };

    public static Result<bool> Validate(JsonElement schema)
    {
        if (schema.ValueKind != JsonValueKind.Object)
        {
            return Result<bool>.Failure("strict schema: the root must be a JSON object schema.");
        }

        if (!DeclaresType(schema, "object"))
        {
            return Result<bool>.Failure("strict schema: the root schema must be type \"object\".");
        }

        var problems = new List<string>();
        Walk(schema, "$", problems);

        return problems.Count == 0
            ? Result<bool>.Success(true)
            : Result<bool>.Failure("strict schema: " + string.Join("; ", problems));
    }

    /// <summary>Convenience overload for the JSON text the extraction stages send.</summary>
    public static Result<bool> Validate(string schemaJson)
    {
        try
        {
            using var document = JsonDocument.Parse(schemaJson);
            return Validate(document.RootElement.Clone());
        }
        catch (JsonException exception)
        {
            return Result<bool>.Failure($"strict schema: not valid JSON ({exception.Message}).");
        }
    }

    private static void Walk(JsonElement node, string path, List<string> problems)
    {
        if (node.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        foreach (var property in node.EnumerateObject())
        {
            if (UnsupportedKeywords.Contains(property.Name))
            {
                problems.Add($"{path} uses unsupported keyword '{property.Name}'");
            }
        }

        if (node.TryGetProperty("enum", out var enumElement) && enumElement.ValueKind == JsonValueKind.Array)
        {
            foreach (var member in enumElement.EnumerateArray())
            {
                if (member.ValueKind == JsonValueKind.Null)
                {
                    problems.Add($"{path}.enum contains null (use a sentinel value or [\"string\",\"null\"] without an enum)");
                    break;
                }
            }
        }

        var hasProperties = node.TryGetProperty("properties", out var properties) && properties.ValueKind == JsonValueKind.Object;
        if (DeclaresType(node, "object") || hasProperties)
        {
            if (!node.TryGetProperty("additionalProperties", out var additional)
                || additional.ValueKind != JsonValueKind.False)
            {
                problems.Add($"{path} must set additionalProperties: false");
            }

            var declared = hasProperties
                ? properties.EnumerateObject().Select(p => p.Name).ToHashSet(StringComparer.Ordinal)
                : [];
            var required = node.TryGetProperty("required", out var requiredElement) && requiredElement.ValueKind == JsonValueKind.Array
                ? requiredElement.EnumerateArray().Where(e => e.ValueKind == JsonValueKind.String).Select(e => e.GetString()!).ToHashSet(StringComparer.Ordinal)
                : [];

            var missing = declared.Except(required).OrderBy(n => n, StringComparer.Ordinal).ToList();
            if (missing.Count > 0)
            {
                problems.Add($"{path} must list every property in required (missing: {string.Join(", ", missing)})");
            }

            var unknown = required.Except(declared).OrderBy(n => n, StringComparer.Ordinal).ToList();
            if (unknown.Count > 0)
            {
                problems.Add($"{path}.required names undeclared properties ({string.Join(", ", unknown)})");
            }

            if (hasProperties)
            {
                foreach (var property in properties.EnumerateObject())
                {
                    Walk(property.Value, $"{path}.{property.Name}", problems);
                }
            }
        }

        if (node.TryGetProperty("items", out var items))
        {
            Walk(items, $"{path}.items", problems);
        }

        foreach (var combinator in new[] { "anyOf", "oneOf", "allOf" })
        {
            if (node.TryGetProperty(combinator, out var branches) && branches.ValueKind == JsonValueKind.Array)
            {
                var index = 0;
                foreach (var branch in branches.EnumerateArray())
                {
                    Walk(branch, $"{path}.{combinator}[{index++}]", problems);
                }
            }
        }

        if (node.TryGetProperty("$defs", out var definitions) && definitions.ValueKind == JsonValueKind.Object)
        {
            foreach (var definition in definitions.EnumerateObject())
            {
                Walk(definition.Value, $"{path}.$defs.{definition.Name}", problems);
            }
        }
    }

    private static bool DeclaresType(JsonElement node, string typeName)
    {
        if (!node.TryGetProperty("type", out var type))
        {
            return false;
        }

        return type.ValueKind switch
        {
            JsonValueKind.String => type.GetString() == typeName,
            JsonValueKind.Array => type.EnumerateArray().Any(t => t.ValueKind == JsonValueKind.String && t.GetString() == typeName),
            _ => false,
        };
    }
}
