using System.Text.Json;
using System.Text.Json.Nodes;
using Raffa.Renewals.Application.Cases;
using Raffa.SharedKernel.Text;

namespace Raffa.Renewals.Tests.Cases;

/// <summary>
/// Test data for the AskRaffa 4.0 renewal case: the prototype's four demo contracts
/// (<c>reference/raffa-data.js</c> CONTRACTS + DEEP, as <c>contracts.json</c>), the replayed action
/// scripts (<c>flows.json</c>) and the prototype's own output for them (<c>golden-en.json</c>).
/// The scenario clock is the prototype's: 08 Sep 2026.
/// </summary>
internal static class CaseFixtures
{
    public const string Luca = "Luca Lamalfa";
    public const string Marta = "Marta Keller";
    public static readonly DateOnly Today = new(2026, 9, 8);

    private static readonly Lazy<Dictionary<int, CaseContractFacts>> ContractsLazy = new(LoadContracts);

    public static CaseContractFacts Contract(int id) => ContractsLazy.Value[id];

    public static CaseContext Ctx(int cid, string viewer) => new(Contract(cid), viewer, Today, FixtureCaseScriptServices.Instance);

    public static string Resource(string name)
    {
        var asm = typeof(CaseFixtures).Assembly;
        using var s = asm.GetManifestResourceStream("Raffa.Renewals.Tests.Cases." + name)
                      ?? throw new InvalidOperationException("Missing embedded resource " + name);
        using var r = new StreamReader(s);
        return r.ReadToEnd();
    }

    public static JsonObject Flows() => JsonNode.Parse(Resource("flows.json"))!.AsObject();

    public static JsonObject Golden() => JsonNode.Parse(Resource("golden-en.json"))!.AsObject();

    /// <summary>Maps a prototype payload (<c>{eid, v, text …}</c>) onto <see cref="CaseActionPayload"/>.</summary>
    public static CaseActionPayload Payload(string type, JsonObject? p)
    {
        if (p is null)
        {
            return CaseActionPayload.None;
        }

        int? I(string k) => p[k] is JsonValue v && v.TryGetValue<int>(out var n) ? n : null;
        string? S(string k) => p[k] is JsonValue v && v.TryGetValue<string>(out var s) ? s : null;
        object? V()
        {
            if (p["v"] is not JsonValue jv)
            {
                return null;
            }

            if (jv.TryGetValue<bool>(out var b))
            {
                return b;
            }

            if (jv.TryGetValue<int>(out var n))
            {
                return n;
            }

            return jv.TryGetValue<string>(out var s) ? s : null;
        }

        return new CaseActionPayload
        {
            Q = I("q"),
            Val = S("val"),
            K = S("k"),
            V = type == "markAnnex" ? null : V(),
            Version = type == "markAnnex" ? I("v") : null,
            Eid = I("eid"),
            Rid = I("rid"),
            Did = I("did"),
            Opt = S("opt"),
            Text = S("text") is { } t ? CaseText.Of(t) : null,
            Resume = S("resume") is { } r ? CaseText.Of(r) : null,
            Who = S("who"),
            On = p["on"] is JsonValue ov && ov.TryGetValue<bool>(out var on) ? on : null,
            I = I("i"),
            N = I("n"),
            Vendor = S("vendor"),
            T = S("t"),
        };
    }

    /// <summary>Replays one flow of <c>flows.json</c>; yields (who, type, case) after create and after every step.</summary>
    public static IEnumerable<(string Who, string Type, RenewalCase Case)> Replay(string flow)
    {
        var f = Flows()[flow]!.AsObject();
        var cid = f["cid"]!.GetValue<int>();
        var creator = f["creator"]!.GetValue<string>();
        var owner = f["owner"]!.GetValue<string>();
        var branch = f["branch"]?.GetValue<string>();
        var c = RenewalCaseEngine.Create(cid, Ctx(cid, creator), branch, owner);
        yield return (creator, "create", c);
        foreach (var step in f["steps"]!.AsArray())
        {
            var a = step!.AsArray();
            var who = a[0]!.GetValue<string>();
            var type = a[1]!.GetValue<string>();
            var p = a.Count > 2 ? a[2] as JsonObject : null;
            c = RenewalCaseEngine.Act(c, type, Payload(type, p), Ctx(cid, who));
            yield return (who, type, c);
        }
    }

    public static int Cid(string flow) => Flows()[flow]!["cid"]!.GetValue<int>();

    private static Dictionary<int, CaseContractFacts> LoadContracts()
    {
        var arr = JsonNode.Parse(Resource("contracts.json"))!.AsArray();
        var map = new Dictionary<int, CaseContractFacts>();
        foreach (var n in arr)
        {
            var o = n!.AsObject();
            List<CaseClause> Clauses(string k) => o[k]!.AsArray().Select(x => new CaseClause(
                x!["type"]!.GetValue<string>(), x["norm"]!.GetValue<string>(), x["ask"]!.GetValue<string>(), x["src"]!.GetValue<string>())).ToList();
            List<CaseObligation> Obl(string k) => o[k]!.AsArray().Select(x => new CaseObligation(
                x!["text"]!.GetValue<string>(), x["when"]!.GetValue<string>(), x["priority"]!.GetValue<string>(), x["cadence"]!.GetValue<string>())).ToList();
            var facts = new CaseContractFacts
            {
                Id = o["id"]!.GetValue<int>(),
                Supplier = o["supplier"]!.GetValue<string>(),
                // The prototype's workspace (the fixture company signing the case emails).
                Company = "Helvetia Foods AG",
                Name = o["name"]!.GetValue<string>(),
                Type = o["type"]!.GetValue<string>(),
                File = o["file"]!.GetValue<string>(),
                Spend = o["spend"]!.GetValue<double>(),
                Start = CaseFormat.ParseDate(o["start"]!.GetValue<string>()),
                End = CaseFormat.ParseDate(o["end"]!.GetValue<string>()),
                Cancel = CaseFormat.ParseDate(o["cancel"]!.GetValue<string>()),
                Notice = o["notice"]!.GetValue<int>(),
                Auto = o["auto"]!.GetValue<bool>(),
                Uplift = o["uplift"]!.GetValue<string>(),
                Products = o["products"]!.AsArray().Select(x => new CaseProduct(
                    x!["name"]!.GetValue<string>(), x["sku"]!.GetValue<string>(), x["unit"]!.GetValue<string>(),
                    x["qty"]!.GetValue<double>(), x["price"]!.GetValue<double>())).ToList(),
                Push = Clauses("push"),
                Raise = Clauses("raise"),
                Terms = o["terms"]!.AsArray().Select(x => new CaseKeyTerm(x!["label"]!.GetValue<string>(), x["value"]!.GetValue<string>())).ToList(),
                OblYou = Obl("oblYou"),
                OblSup = Obl("oblSup"),
            };
            map[facts.Id] = facts;
        }

        return map;
    }

    public static readonly JsonSerializerOptions Indented = new() { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
}
