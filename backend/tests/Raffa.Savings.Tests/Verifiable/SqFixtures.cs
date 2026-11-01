using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Raffa.Savings.Application.Verifiable;
using Raffa.SharedKernel.Text;

namespace Raffa.Savings.Tests.Verifiable;

/// <summary>
/// The prototype's quote/savings demo data (<c>SQ.HIST</c>, <c>SQ.QUOTES</c>, <c>SQ.CASEQ</c>) and its own
/// outputs for a set of <c>assess</c> / <c>report</c> / <c>counterEmail</c> calls (<c>sq-golden.json</c>).
/// </summary>
internal static partial class SqFixtures
{
    public static readonly DateOnly Today = new(2026, 9, 8);

    private static readonly Lazy<JsonObject> GoldenLazy = new(() =>
    {
        using var s = typeof(SqFixtures).Assembly.GetManifestResourceStream("Raffa.Savings.Tests.Verifiable.sq-golden.json")!;
        return JsonNode.Parse(s)!.AsObject();
    });

    public static JsonObject Golden => GoldenLazy.Value;

    /// <summary>Italian of the quotes' own "not stated" gaps (demo data, not engine messages).</summary>
    public static Dictionary<string, string> GapsItalian()
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var q in Golden["quotes"]!.AsArray())
        {
            foreach (var g in q!["gaps"]!.AsArray())
            {
                map[g!["en"]!.GetValue<string>()] = g["it"]!.GetValue<string>();
            }
        }

        return map;
    }

    public static DateOnly Date(string s)
    {
        string[] mon = ["Jan", "Feb", "Mar", "Apr", "May", "Jun", "Jul", "Aug", "Sep", "Oct", "Nov", "Dec"];
        var p = s.Split(' ');
        return new DateOnly(int.Parse(p[2], CultureInfo.InvariantCulture), Array.IndexOf(mon, p[1]) + 1, int.Parse(p[0], CultureInfo.InvariantCulture));
    }

    private static string? Str(JsonNode? n) => n is JsonValue v && v.TryGetValue<string>(out var s) ? s : null;

    private static double? Dbl(JsonNode? n) => n is JsonValue v && v.TryGetValue<double>(out var d) ? d : null;

    public static Quote Quote(string id)
    {
        var q = Golden["quotes"]!.AsArray().Single(x => x!["id"]!.GetValue<string>() == id)!;
        var st = q["stated"];
        var tgt = q["target"];
        return new Quote
        {
            Id = id,
            Ref = Str(q["ref"]) ?? string.Empty,
            Supplier = Str(q["supplier"])!,
            File = Str(q["file"])!,
            Kind = Str(q["kind"])!,
            Term = Str(q["term"]),
            Valid = Str(q["valid"]) is { } v ? Date(v) : null,
            Status = Str(q["status"]) ?? "checked",
            Lines = q["lines"]!.AsArray().Select(l => new QuoteLine
            {
                Name = Str(l!["name"])!,
                Sku = Str(l["sku"]) ?? string.Empty,
                Qty = Dbl(l["qty"])!.Value,
                Unit = Str(l["unit"])!,
                Price = Dbl(l["price"])!.Value,
                Existing = Dbl(l["existing"]),
                ExistingLabel = Str(l["existingLabel"]),
            }).ToList(),
            Stated = st is null ? null : new QuoteStated(Str(st["payment"]), Str(st["uplift"]), Str(st["renewal"]), Str(st["liability"]), Str(st["sla"])),
            Gaps = q["gaps"]!.AsArray().Select(g => CaseText.Message(g!["en"]!.GetValue<string>())).ToList(),
            Target = tgt is null ? null : new QuoteTarget(Str(tgt["kind"])!, Dbl(tgt["budget"]), Str(tgt["label"])),
        };
    }

    public static AssessOptions Options(JsonNode? o)
    {
        if (o is null)
        {
            return new AssessOptions();
        }

        var t = o["target"];
        return new AssessOptions
        {
            Target = t is null ? null : new QuoteTarget(Str(t["kind"])!, Dbl(t["budget"]), Str(t["label"])),
            PlanTerm = Str(o["planTerm"]),
            Others = o["others"]?.AsArray().Select(x => new OtherOffer(Str(x!["name"])!, Dbl(x["total"])!.Value, (int)Dbl(x["term"])!.Value)).ToList() ?? [],
            NoticeDate = Str(o["noticeDate"]) is { } nd ? Date(nd) : null,
            Policy = o["policy"] is { } p ? new QuotePolicy((int)Dbl(p["liabMin"])!.Value) : null,
        };
    }

    public static List<SavingResult> Hist() => Golden["hist"]!.AsArray().Select(h => new SavingResult
    {
        Id = Str(h!["id"])!,
        Supplier = Str(h["supplier"])!,
        Contract = Str(h["contract"])!,
        Cat = Str(h["cat"])!,
        Kind = Str(h["kind"])!,
        Lever = Str(h["lever"])!,
        Owner = Str(h["owner"])!,
        Amount = Dbl(h["amount"])!.Value,
        When = Date(Str(h["when"])!),
        Evidence = Str(h["evidence"])!,
        Note = Str(h["note"])!,
        Baseline = Str(h["baseline"])!,
        Period = Str(h["period"])!,
        Method = Str(h["method"])!,
        Realized = h["realized"]!.GetValue<bool>(),
        RealizedEv = Str(h["realizedEv"]),
        RealizedAmount = Dbl(h["realizedAmount"]),
        Quote = Str(h["quote"]),
    }).ToList();

    public static List<OpenObjective> Open() => Golden["open"]!.AsArray().Select(o => new OpenObjective
    {
        Supplier = Str(o!["supplier"])!,
        Cat = Str(o["cat"])!,
        Owner = Str(o["owner"])!,
        TargetAmount = Dbl(o["targetAmount"]),
        Days = Dbl(o["days"]) is { } d ? (int)d : null,
    }).ToList();

    /// <summary>The generated <c>web/src/i18n/it/quote4.ts</c>.</summary>
    public static Dictionary<string, string> Quote4Catalogue()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "web", "src", "i18n", "it", "quote4.ts")))
        {
            dir = dir.Parent;
        }

        if (dir is null)
        {
            throw new FileNotFoundException("web/src/i18n/it/quote4.ts not found above " + AppContext.BaseDirectory);
        }

        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var line in File.ReadLines(Path.Combine(dir.FullName, "web", "src", "i18n", "it", "quote4.ts")))
        {
            var m = Entry().Match(line);
            if (m.Success)
            {
                map[JsonSerializer.Deserialize<string>(m.Groups[1].Value)!] = JsonSerializer.Deserialize<string>(m.Groups[2].Value)!;
            }
        }

        return map;
    }

    [GeneratedRegex(@"^\s*(""(?:[^""\\]|\\.)*""):\s*(""(?:[^""\\]|\\.)*""),\s*$")]
    private static partial Regex Entry();
}
