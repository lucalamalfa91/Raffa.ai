using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace Raffa.SharedKernel.Text;

/// <summary>
/// A piece of user-facing text produced by a deterministic engine (renewal case, quote check): either
/// a plain string that is already final (a person's own words, a file name, an email body) or a
/// message not yet turned into a language — <c>{ "id": "Plan v{planVer} confirmed", "vars": {...} }</c>.
/// The id is the exact English source template (gettext style); the web app looks the id up in its
/// Italian catalogue and fills <c>{placeholders}</c> from <see cref="Vars"/>, whose values are
/// themselves <see cref="CaseText"/> (so a message can nest another message). JSON shape: a string, or
/// the object above. This mirrors <c>web/src/i18n/translate.ts</c>' <c>Text = string | Msg</c>.
/// </summary>
[JsonConverter(typeof(CaseTextJsonConverter))]
public sealed partial class CaseText : IEquatable<CaseText>
{
    private static readonly IReadOnlyDictionary<string, CaseText> NoVars = new Dictionary<string, CaseText>();

    private CaseText(string? plain, string? id, IReadOnlyDictionary<string, CaseText>? vars)
    {
        Plain = plain;
        Id = id;
        Vars = vars;
    }

    /// <summary>The final text when this is a plain string; otherwise null.</summary>
    public string? Plain { get; }

    /// <summary>The message id (English template) when this is a message; otherwise null.</summary>
    public string? Id { get; }

    /// <summary>Placeholder values of a message (null for a plain string or a message without vars).</summary>
    public IReadOnlyDictionary<string, CaseText>? Vars { get; }

    public bool IsMessage => Id is not null;

    public static CaseText Empty { get; } = new(string.Empty, null, null);

    public static CaseText Of(string? text) => text is null or "" ? Empty : new CaseText(text, null, null);

    public static CaseText Message(string id, IReadOnlyDictionary<string, CaseText>? vars = null) =>
        new(null, id, vars is { Count: > 0 } ? vars : null);

    /// <summary><c>Message("Plan v{planVer} confirmed", ("planVer", 2))</c>. Values: string, number, bool, CaseText or null.</summary>
    public static CaseText Message(string id, params (string Name, object? Value)[] vars)
    {
        if (vars.Length == 0)
        {
            return new CaseText(null, id, null);
        }

        var map = new Dictionary<string, CaseText>(vars.Length, StringComparer.Ordinal);
        foreach (var (name, value) in vars)
        {
            map[name] = From(value);
        }

        return new CaseText(null, id, map);
    }

    /// <summary>Converts a placeholder value: numbers print like JavaScript's <c>String(n)</c>.</summary>
    public static CaseText From(object? value) => value switch
    {
        null => Empty,
        CaseText t => t,
        string s => Of(s),
        bool b => Of(b ? "true" : "false"),
        int i => Of(i.ToString(CultureInfo.InvariantCulture)),
        long l => Of(l.ToString(CultureInfo.InvariantCulture)),
        double d => Of(JsNumber(d)),
        decimal m => Of(m.ToString(CultureInfo.InvariantCulture)),
        _ => Of(Convert.ToString(value, CultureInfo.InvariantCulture)),
    };

    /// <summary>JavaScript's default number-to-string for the values these engines print.</summary>
    public static string JsNumber(double d)
    {
        if (double.IsNaN(d))
        {
            return "NaN";
        }

        if (d == Math.Floor(d) && Math.Abs(d) < 1e21)
        {
            return d.ToString("0", CultureInfo.InvariantCulture);
        }

        return d.ToString("R", CultureInfo.InvariantCulture);
    }

    public static implicit operator CaseText(string? text) => Of(text);

    /// <summary>
    /// Renders the text. <paramref name="catalogue"/> maps a message id to its translation (null =
    /// English, i.e. the id itself). A message whose id is missing from the catalogue renders in English.
    /// </summary>
    public string Render(Func<string, string?>? catalogue = null)
    {
        if (Id is null)
        {
            return Plain ?? string.Empty;
        }

        var template = catalogue?.Invoke(Id) ?? ShownId(Id);
        var vars = Vars ?? NoVars;
        return Placeholder().Replace(template, m =>
        {
            var name = m.Groups[1].Value;
            return vars.TryGetValue(name, out var value) ? value.Render(catalogue) : m.Value;
        });
    }

    /// <summary>
    /// Two messages that read the same in English but not in Italian get different ids by ending one
    /// with a context note in ⟦brackets⟧ ("Term⟦condition⟧"); the note is never shown (as in
    /// <c>web/src/i18n/translate.ts</c> <c>shownId</c>).
    /// </summary>
    public static string ShownId(string id)
    {
        var i = id.LastIndexOf('⟦');
        return i >= 0 && id.EndsWith('⟧') ? id[..i] : id;
    }

    /// <summary>English rendering (the id with its placeholders filled).</summary>
    public override string ToString() => Render();

    public bool Equals(CaseText? other)
    {
        if (other is null)
        {
            return false;
        }

        if (Id is null || other.Id is null)
        {
            return Id is null && other.Id is null && string.Equals(Plain ?? string.Empty, other.Plain ?? string.Empty, StringComparison.Ordinal);
        }

        if (!string.Equals(Id, other.Id, StringComparison.Ordinal))
        {
            return false;
        }

        var a = Vars ?? NoVars;
        var b = other.Vars ?? NoVars;
        return a.Count == b.Count && a.All(kv => b.TryGetValue(kv.Key, out var v) && kv.Value.Equals(v));
    }

    public override bool Equals(object? obj) => obj is CaseText t && Equals(t);

    public override int GetHashCode() => Id is null ? (Plain ?? string.Empty).GetHashCode(StringComparison.Ordinal) : Id.GetHashCode(StringComparison.Ordinal);

    /// <summary>Joins texts with a separator, keeping messages as messages (<c>"{a}{sep}{b}"</c> chains).</summary>
    public static CaseText Join(string separator, IEnumerable<CaseText> parts)
    {
        var list = parts.ToList();
        if (list.Count == 0)
        {
            return Empty;
        }

        if (list.All(p => !p.IsMessage))
        {
            return Of(string.Join(separator, list.Select(p => p.Plain)));
        }

        var id = new StringBuilder();
        var vars = new Dictionary<string, CaseText>(StringComparer.Ordinal);
        for (var i = 0; i < list.Count; i++)
        {
            if (i > 0)
            {
                id.Append(separator);
            }

            id.Append('{').Append('p').Append(i.ToString(CultureInfo.InvariantCulture)).Append('}');
            vars["p" + i.ToString(CultureInfo.InvariantCulture)] = list[i];
        }

        return Message(id.ToString(), vars);
    }

    [GeneratedRegex(@"\{(\w+)\}")]
    private static partial Regex Placeholder();
}

/// <summary>JSON: a plain <see cref="CaseText"/> is a string; a message is <c>{ "id", "vars"? }</c>.</summary>
public sealed class CaseTextJsonConverter : JsonConverter<CaseText>
{
    public override CaseText? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        switch (reader.TokenType)
        {
            case JsonTokenType.Null:
                return null;
            case JsonTokenType.String:
                return CaseText.Of(reader.GetString());
            case JsonTokenType.Number:
                return CaseText.Of(CaseText.JsNumber(reader.GetDouble()));
            case JsonTokenType.StartObject:
                string? id = null;
                Dictionary<string, CaseText>? vars = null;
                while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
                {
                    var name = reader.GetString();
                    reader.Read();
                    if (string.Equals(name, "id", StringComparison.OrdinalIgnoreCase))
                    {
                        id = reader.GetString();
                    }
                    else if (string.Equals(name, "vars", StringComparison.OrdinalIgnoreCase) && reader.TokenType == JsonTokenType.StartObject)
                    {
                        vars = new Dictionary<string, CaseText>(StringComparer.Ordinal);
                        while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
                        {
                            var key = reader.GetString()!;
                            reader.Read();
                            vars[key] = Read(ref reader, typeToConvert, options) ?? CaseText.Empty;
                        }
                    }
                    else
                    {
                        reader.Skip();
                    }
                }

                return id is null ? CaseText.Empty : CaseText.Message(id, vars);
            default:
                throw new JsonException($"Unexpected token {reader.TokenType} for a CaseText.");
        }
    }

    public override void Write(Utf8JsonWriter writer, CaseText value, JsonSerializerOptions options)
    {
        if (!value.IsMessage)
        {
            writer.WriteStringValue(value.Plain ?? string.Empty);
            return;
        }

        writer.WriteStartObject();
        writer.WriteString("id", value.Id);
        if (value.Vars is { Count: > 0 })
        {
            writer.WritePropertyName("vars");
            writer.WriteStartObject();
            foreach (var (key, v) in value.Vars)
            {
                writer.WritePropertyName(key);
                Write(writer, v, options);
            }

            writer.WriteEndObject();
        }

        writer.WriteEndObject();
    }
}
