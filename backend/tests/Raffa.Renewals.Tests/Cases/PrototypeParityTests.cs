using System.Text.Json.Nodes;

namespace Raffa.Renewals.Tests.Cases;

/// <summary>
/// Proves the C# port behaves exactly like the AskRaffa 4.0 prototype: every flow in
/// <c>flows.json</c> (the "Da zero" Salesforce golden path, the three seeded "Con dati" cases, the
/// alternatives, notice and follow-up branches) is replayed through
/// <see cref="Raffa.Renewals.Application.Cases.RenewalCaseEngine"/> and each step's English snapshot
/// (phase, header, current card with its ≤3 buttons and "More", completed steps, checklist, Today
/// items, timeline, emails …) must equal what <c>reference/renewal-case-v4.js</c> produced for the same
/// step (<c>golden-en.json</c>).
/// </summary>
public sealed class PrototypeParityTests
{
    public static TheoryData<string> Flows()
    {
        var data = new TheoryData<string>();
        foreach (var kv in CaseFixtures.Flows())
        {
            data.Add(kv.Key);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(Flows))]
    public void Every_step_matches_the_prototype(string flow)
    {
        var golden = CaseFixtures.Golden()[flow]!.AsArray();
        var cid = CaseFixtures.Cid(flow);
        var steps = CaseFixtures.Replay(flow).ToList();
        Assert.Equal(golden.Count, steps.Count);
        var diffs = new List<string>();
        for (var i = 0; i < steps.Count; i++)
        {
            var (who, type, c) = steps[i];
            var final = i == steps.Count - 1;
            var mine = JsonNode.Parse(CaseSnapshot.Build(c, CaseFixtures.Ctx(cid, who), who, type, final && i > 0).ToJsonString())!;
            Diff($"{flow}[{i}:{type}]", golden[i], mine, diffs);
            if (diffs.Count > 12)
            {
                break;
            }
        }

        Assert.True(diffs.Count == 0, string.Join("\n", diffs));
    }

    /// <summary>
    /// The same replay rendered in Italian with the generated catalogue
    /// <c>web/src/i18n/it/case4.ts</c> must equal the prototype's Italian (<c>golden-it.json</c>): every
    /// message id exists in the catalogue and its Italian text is the prototype's.
    /// </summary>
    [Theory]
    [MemberData(nameof(Flows))]
    public void Every_step_matches_the_prototype_in_Italian(string flow)
    {
        var it = ItalianCatalogue.Case4.Value;
        var golden = JsonNode.Parse(CaseFixtures.Resource("golden-it.json"))!.AsObject()[flow]!.AsArray();
        var cid = CaseFixtures.Cid(flow);
        var steps = CaseFixtures.Replay(flow).ToList();
        var diffs = new List<string>();
        for (var i = 0; i < steps.Count; i++)
        {
            var (who, type, c) = steps[i];
            var mine = JsonNode.Parse(CaseSnapshot.BuildItalian(c, CaseFixtures.Ctx(cid, who), who, type, id => it.TryGetValue(id, out var t) ? t : null).ToJsonString())!;
            Diff($"{flow}[{i}:{type}]", golden[i], mine, diffs);
            if (diffs.Count > 12)
            {
                break;
            }
        }

        Assert.True(diffs.Count == 0, string.Join("\n", diffs));
    }

    private static void Diff(string path, JsonNode? expected, JsonNode? actual, List<string> diffs)
    {
        if (diffs.Count > 12)
        {
            return;
        }

        switch (expected)
        {
            case JsonObject eo when actual is JsonObject ao:
                foreach (var kv in eo)
                {
                    Diff(path + "." + kv.Key, kv.Value, ao[kv.Key], diffs);
                }

                foreach (var kv in ao)
                {
                    if (!eo.ContainsKey(kv.Key) && kv.Value is not null)
                    {
                        diffs.Add($"{path}.{kv.Key}: unexpected {kv.Value.ToJsonString()}");
                    }
                }

                return;
            case JsonArray ea when actual is JsonArray aa:
                for (var i = 0; i < Math.Max(ea.Count, aa.Count); i++)
                {
                    Diff($"{path}[{i}]", i < ea.Count ? ea[i] : null, i < aa.Count ? aa[i] : null, diffs);
                }

                return;
        }

        var e = expected?.ToJsonString() ?? "null";
        var a = actual?.ToJsonString() ?? "null";
        if (expected is JsonValue ev && actual is JsonValue av && ev.TryGetValue<double>(out var ed) && av.TryGetValue<double>(out var ad))
        {
            if (Math.Abs(ed - ad) < 1e-9)
            {
                return;
            }
        }

        if (e != a)
        {
            diffs.Add($"{path}:\n   expected {e}\n   actual   {a}");
        }
    }
}
