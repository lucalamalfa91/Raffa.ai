using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization.Metadata;

namespace Raffa.AiGateway.Agents;

/// <summary>
/// Derives the strict JSON Schema of an agent's output from its C# type (plan 3.2: "schema JSON
/// derivato dal tipo di output"). The result is born inside the subset Azure OpenAI structured
/// outputs accept in strict mode, the one <see cref="Foundry.StrictJsonSchemaValidator"/> enforces:
/// every object lists all of its properties in <c>required</c> and sets
/// <c>additionalProperties: false</c>; a nullable property is <c>["type","null"]</c>; no
/// <c>format</c>, <c>pattern</c> or bound keyword is ever emitted. (The BCL
/// <c>JsonSchemaExporter</c> is deliberately not used: it omits <c>additionalProperties</c>, emits
/// <c>["string","integer"]</c> plus a <c>pattern</c> for numbers under the Web defaults, and adds
/// <c>format</c> for dates and ids -- all refused by Azure with an opaque 400.)
///
/// Supported: records and classes with public readable properties, <c>string</c>, <c>bool</c>,
/// integers (<c>integer</c>), <c>float</c>/<c>double</c>/<c>decimal</c> (<c>number</c>), enums (as
/// strings), <c>Guid</c>/<c>DateTime</c>/<c>DateOnly</c>/... (as <c>string</c>), arrays and
/// <c>IEnumerable&lt;T&gt;</c>, and <c>Nullable&lt;T&gt;</c> or annotated reference types as
/// nullable. Not supported, and refused with <see cref="NotSupportedException"/> naming the path:
/// dictionaries (strict mode forbids open objects), <c>object</c>/<c>JsonElement</c>, recursive
/// types. Property names follow <see cref="AgentJson.Options"/>, so the schema describes exactly the
/// JSON the type deserializes from.
/// </summary>
public static class AgentSchema
{
    private const int MaxDepth = 12;

    /// <summary>The compact strict schema (a JSON object) for <typeparamref name="T"/>.</summary>
    public static string For<T>() => For(typeof(T));

    /// <summary>The compact strict schema (a JSON object) for <paramref name="type"/>.</summary>
    public static string For(Type type)
    {
        ArgumentNullException.ThrowIfNull(type);

        var node = Describe(type, nullable: false, "$", new Stack<Type>(), 0);
        if (node["type"] is not JsonValue || node["properties"] is null)
        {
            throw new NotSupportedException(
                $"Agent output type {type.Name} must be an object (a record or class): the root of a strict schema is an object.");
        }

        return node.ToJsonString();
    }

    private static JsonNode Describe(Type type, bool nullable, string path, Stack<Type> stack, int depth)
    {
        if (depth > MaxDepth)
        {
            throw new NotSupportedException($"{path}: nested deeper than {MaxDepth} levels.");
        }

        if (Nullable.GetUnderlyingType(type) is { } underlying)
        {
            type = underlying;
            nullable = true;
        }

        if (type.IsEnum)
        {
            var names = new JsonArray();
            foreach (var name in Enum.GetNames(type))
            {
                names.Add(name);
            }

            var plain = new JsonObject { ["type"] = "string", ["enum"] = names };

            // A strict enum may not contain null, so a nullable enum is a choice between the enum
            // and null rather than ["string","null"] plus an enum.
            return nullable
                ? new JsonObject { ["anyOf"] = new JsonArray(plain, new JsonObject { ["type"] = "null" }) }
                : plain;
        }

        if (PrimitiveTypeName(type) is { } primitive)
        {
            return new JsonObject { ["type"] = TypeKeyword(primitive, nullable) };
        }

        var info = AgentJson.Options.GetTypeInfo(type);
        switch (info.Kind)
        {
            case JsonTypeInfoKind.Enumerable:
            {
                var element = info.ElementType
                    ?? throw new NotSupportedException($"{path}: cannot determine the element type of {type.Name}.");
                return new JsonObject
                {
                    ["type"] = TypeKeyword("array", nullable),
                    ["items"] = Describe(element, nullable: false, path + "[]", stack, depth + 1),
                };
            }

            case JsonTypeInfoKind.Object:
            {
                if (stack.Contains(type))
                {
                    throw new NotSupportedException($"{path}: {type.Name} is recursive; a strict schema has no cycles.");
                }

                stack.Push(type);
                try
                {
                    var properties = new JsonObject();
                    var required = new JsonArray();
                    foreach (var property in info.Properties)
                    {
                        if (property.Get is null)
                        {
                            continue;
                        }

                        properties[property.Name] = Describe(
                            property.PropertyType, property.IsGetNullable, $"{path}.{property.Name}", stack, depth + 1);
                        required.Add(property.Name);
                    }

                    return new JsonObject
                    {
                        ["type"] = TypeKeyword("object", nullable),
                        ["properties"] = properties,
                        ["required"] = required,
                        ["additionalProperties"] = false,
                    };
                }
                finally
                {
                    stack.Pop();
                }
            }

            default:
                throw new NotSupportedException(
                    $"{path}: {type.Name} cannot be described as a strict schema (dictionaries, object and JsonElement are open-ended).");
        }
    }

    private static JsonNode TypeKeyword(string name, bool nullable) =>
        nullable ? new JsonArray(name, "null") : JsonValue.Create(name);

    private static string? PrimitiveTypeName(Type type)
    {
        if (type == typeof(string) || type == typeof(char) ||
            type == typeof(Guid) || type == typeof(DateTime) || type == typeof(DateTimeOffset) ||
            type == typeof(DateOnly) || type == typeof(TimeOnly) || type == typeof(TimeSpan) || type == typeof(Uri))
        {
            return "string";
        }

        if (type == typeof(bool))
        {
            return "boolean";
        }

        if (type == typeof(byte) || type == typeof(sbyte) || type == typeof(short) || type == typeof(ushort) ||
            type == typeof(int) || type == typeof(uint) || type == typeof(long) || type == typeof(ulong))
        {
            return "integer";
        }

        if (type == typeof(float) || type == typeof(double) || type == typeof(decimal))
        {
            return "number";
        }

        return null;
    }
}
