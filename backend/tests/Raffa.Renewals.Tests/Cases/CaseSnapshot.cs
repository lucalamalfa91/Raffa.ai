using System.Text.Json.Nodes;
using Raffa.Renewals.Application.Cases;
using Raffa.SharedKernel.Text;

namespace Raffa.Renewals.Tests.Cases;

/// <summary>
/// Renders the C# engine's state + view in English into exactly the shape the prototype harness
/// writes into <c>golden-en.json</c> (one snapshot per replayed step), so the two can be compared
/// field by field.
/// </summary>
internal static class CaseSnapshot
{
    [ThreadStatic]
    private static Func<string, string?>? catalogue;

    private static string R(CaseText? t) => t?.Render(catalogue) ?? string.Empty;

    private static JsonArray Arr(IEnumerable<string> items) => new(items.Select(s => (JsonNode?)JsonValue.Create(s)).ToArray());

    private static string Row(CaseRow r) => R(r.K) + ": " + R(r.V);

    private static string InputValue(object v) => v switch
    {
        bool b => b ? "true" : "false",
        _ => v.ToString() ?? string.Empty,
    };

    /// <summary>The prototype's Italian snapshot subset (see the harness), rendered with <paramref name="italian"/>.</summary>
    public static JsonObject BuildItalian(RenewalCase c, CaseContext ctx, string who, string type, Func<string, string?> italian)
    {
        catalogue = italian;
        try
        {
            var full = Build(c, ctx, who, type, false);
            var keep = new JsonObject();
            foreach (var k in new[] { "type", "who", "warn", "hdr", "page", "summary", "check", "example", "todo", "emailsList", "docsList", "upcoming", "termsCmp", "stepsX" })
            {
                keep[k] = full[k]?.DeepClone();
            }

            return keep;
        }
        finally
        {
            catalogue = null;
        }
    }

    public static JsonObject Build(RenewalCase c, CaseContext ctx, string who, string type, bool final)
    {
        var v = RenewalCaseEngine.View(c, ctx);
        var p = v.Page;
        var cur = p.Current;
        var page = new JsonObject
        {
            ["k"] = cur.K,
            ["kicker"] = R(cur.Kicker),
            ["title"] = R(cur.Title),
            ["banner"] = R(cur.Banner),
            ["lines"] = Arr(cur.Lines.Select(R)),
            ["rows"] = Arr(cur.Rows.Select(Row)),
            ["table"] = cur.Table is { } t
                ? Arr(new[] { string.Join(" | ", t.Cols.Select(R)) }.Concat(t.Rows.Select(r => string.Join(" | ", r.Select(x => R(x.V))))))
                : new JsonArray(),
            ["steps"] = Arr(cur.Steps.Select(s => R(s.Label) + (s.Now ? " (now)" : s.On ? " (on)" : string.Empty))),
            ["email"] = cur.Email is { } e
                ? new JsonObject
                {
                    ["to"] = e.To,
                    ["toMissing"] = e.ToMissing,
                    ["state"] = R(e.State),
                    ["editing"] = e.Editing,
                    ["attachNote"] = R(e.AttachNote),
                    ["sentNote"] = R(e.SentNote),
                }
                : null,
            ["pre"] = cur.Pre,
            ["fields"] = Arr(cur.Fields.Select(f => R(f.Name) + ": " + f.Val + " " + f.Conf + " " + f.State)),
            ["chips"] = Arr(cur.Chips.Select(ch => (ch.On ? "[x] " : string.Empty) + R(ch.Label) + " [" + ch.Act + "]")),
            ["inputs"] = Arr(cur.Inputs.Select(i => R(i.Label) + "=" + InputValue(i.Value) + " (" + i.Ui + ")")),
            ["buttons"] = Arr(cur.Buttons.Select(b => (b.Primary ? "*" : string.Empty) + R(b.Label) + " [" + b.Act + "]" + (b.Off ? " (off: " + R(b.Why) + ")" : string.Empty))),
            ["more"] = Arr(cur.More.Select(b => R(b.Label) + " [" + b.Act + "]")),
            ["note"] = R(cur.Note),
            ["bullets"] = Arr(cur.Bullets.Select(R)),
            ["stats"] = Arr(cur.Stats.Select(s => R(s.K) + ": " + R(s.V))),
            ["done"] = Arr(p.Done.Select(d => R(d.Kicker) + " | " + R(d.Title) + " | " + R(d.Note))),
            ["msgs"] = Arr(p.Msgs.Select(m => m.By + ": " + R(m.Text))),
        };
        var h = v.Hdr;
        var s = new JsonObject
        {
            ["type"] = type,
            ["who"] = who,
            ["phase"] = c.Phase,
            ["stage"] = RenewalCaseEngine.Stage(c),
            ["warn"] = R(c.Ui.Warn),
            ["hdr"] = new JsonObject
            {
                ["title"] = R(h.Title),
                ["status"] = R(h.Status),
                ["next"] = R(h.Next),
                ["noticeBy"] = h.NoticeBy,
                ["noticeDays"] = h.NoticeDays,
                ["end"] = R(h.End),
                ["tracks"] = Arr(h.Tracks.Select(R)),
                ["people"] = Arr(h.People.Select(x => x.Name + ": " + R(x.Role))),
                ["youRole"] = R(h.YouRole),
            },
            ["page"] = page,
            ["summary"] = new JsonObject
            {
                ["goals"] = Arr(v.Summary.Goals.Select(Row)),
                ["terms"] = Arr(v.Summary.Terms.Select(x => R(x.Label) + ": " + x.Val + " · " + R(x.State))),
                ["decisions"] = Arr(v.Summary.Decisions.Select(d => d.By + " · " + d.At + " · " + R(d.Text))),
                ["refs"] = R(v.Summary.Refs),
            },
            ["check"] = Arr(v.Check.Select(x => (x.Done ? "[x] " : "[ ] ") + R(x.Label) + (x.Who.Length > 0 ? " · " + x.Who : string.Empty))),
            ["example"] = R(v.Example),
            ["todo"] = Arr(RenewalCaseEngine.Todo(c, ctx).Select(t => t.Kind + " · " + t.Who + " · " + R(t.Title) + " · " + R(t.Why) + " · " + t.Due + " (" + t.DueDays + ")")),
            ["emailsList"] = Arr(v.EmailsList.Select(x => R(x.Label) + ": " + R(x.State))),
            ["docsList"] = Arr(v.DocsList.Select(x => R(x.Label) + ": " + R(x.State))),
            ["upcoming"] = Arr(v.Upcoming.Select(u => u.Date + " " + R(u.Label) + " " + R(u.When))),
            ["termsCmp"] = Arr(v.TermsCmp.Select(t => string.Join(" | ", R(t.Label), R(t.Current), R(t.From), R(t.Proposed), R(t.PropFrom), R(t.State)))),
            ["stepsX"] = Arr(v.StepsX.Select(x => x.N + " " + R(x.Label) + (x.Now ? " (now)" : x.Done ? " (done)" : string.Empty))),
            ["since"] = Arr(v.Since?.Lines.Select(R) ?? []),
            ["shortStatus"] = v.ShortStatus,
            ["timeline"] = Arr(v.Timeline.Select(t => t.At + " " + t.Time + " " + t.By + " · " + R(t.Label) + (R(t.Label) == "Raffa.ai" ? string.Empty : " · " + R(t.Text)))),
            ["threads"] = Arr(v.Threads.Select(t => t.T + (t.On ? " (on)" : string.Empty))),
            ["reply"] = new JsonObject { ["open"] = v.Reply.Open, ["text"] = v.Reply.Text, ["from"] = v.Reply.From },
        };
        if (final)
        {
            s["cards"] = Arr(v.Cards.Select(x => string.Join(" | ", x.K, R(x.Kicker), R(x.Title), x.Dim ? "dim" : string.Empty, x.Current ? "current" : string.Empty)));
            s["emails"] = new JsonArray(c.Emails.Select(e => (JsonNode?)Email(e)).ToArray());
            s["outcome"] = Outcome(c.Outcome);
            s["agreed"] = c.Agreed is { } a ? Agreed(a) : null;
            s["limits"] = c.Limits is { } l ? Limits(l) : null;
        }

        return s;
    }

    private static JsonObject Email(CaseEmail e)
    {
        var o = new JsonObject
        {
            ["kind"] = e.Kind,
            ["ver"] = e.Ver,
            ["state"] = e.State,
            ["to"] = e.To,
            ["subject"] = e.Subject,
            ["body"] = e.Body,
            ["approvedBy"] = e.ApprovedBy,
            ["sent"] = e.Sent is { } sd
                ? new JsonObject { ["by"] = sd.By, ["at"] = sd.At, ["to"] = sd.To, ["edited"] = sd.Edited, ["text"] = sd.Text }
                : null,
        };
        return o;
    }

    private static JsonNode? Outcome(CaseOutcome? o)
    {
        if (o is null)
        {
            return null;
        }

        var j = new JsonObject { ["kind"] = o.Kind };
        if (o.Kind == "renewed")
        {
            j["start"] = CaseFormat.Date(o.Start!.Value);
            j["end"] = CaseFormat.Date(o.End);
            j["cancel"] = CaseFormat.Date(o.Cancel!.Value);
            j["cancelDays"] = o.CancelDays;
            j["spend"] = o.Spend;
            j["delta"] = o.Delta;
            j["price"] = R(o.Price);
            j["file"] = o.File;
        }
        else
        {
            j["end"] = CaseFormat.Date(o.End);
            j["when"] = o.When is { } w ? CaseFormat.Date(w) : null;
            if (o.ReplacedBy is not null)
            {
                j["replacedBy"] = o.ReplacedBy;
            }
        }

        return j;
    }

    private static JsonObject Agreed(CaseAgreed a)
    {
        var j = new JsonObject { ["price"] = a.Price, ["term"] = a.Term, ["cap"] = a.Cap, ["liab"] = a.Liab };
        if (a.Auth is not null)
        {
            j["auth"] = a.Auth;
        }

        return j;
    }

    private static JsonObject Limits(CaseLimits l)
    {
        var j = new JsonObject { ["term"] = l.Term };
        if (l.PriceMax is { } pm)
        {
            j["priceMax"] = pm;
        }

        if (l.Exit is { } ex)
        {
            j["exit"] = ex;
        }

        return j;
    }
}
