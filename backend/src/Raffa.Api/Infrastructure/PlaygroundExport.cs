using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Raffa.AiGateway.Agents;
using Raffa.AiGateway.Telemetry;
using Raffa.Identity.Workspace.Infrastructure;

namespace Raffa.Api.Infrastructure;

/// <summary>One file a proposal writes to the repository: its path from the repository root, its content and the commit message.</summary>
internal sealed record PlaygroundFileChange(string Path, string Content, string Message);

/// <summary>A line-by-line unified diff (longest common subsequence), for the review of a changed prompt.</summary>
internal static class PlaygroundTextDiff
{
    public static (string Unified, int Added, int Removed) Unified(string oldText, string newText, string oldName, string newName, int context = 3)
    {
        var a = Lines(oldText);
        var b = Lines(newText);
        var lcs = new int[a.Length + 1, b.Length + 1];
        for (var i = a.Length - 1; i >= 0; i--)
        {
            for (var j = b.Length - 1; j >= 0; j--)
            {
                lcs[i, j] = a[i] == b[j] ? lcs[i + 1, j + 1] + 1 : Math.Max(lcs[i + 1, j], lcs[i, j + 1]);
            }
        }

        // ops: ' ' kept, '-' removed, '+' added
        var ops = new List<(char Op, string Line)>();
        int x = 0, y = 0;
        while (x < a.Length && y < b.Length)
        {
            if (a[x] == b[y])
            {
                ops.Add((' ', a[x])); x++; y++;
            }
            else if (lcs[x + 1, y] >= lcs[x, y + 1])
            {
                ops.Add(('-', a[x])); x++;
            }
            else
            {
                ops.Add(('+', b[y])); y++;
            }
        }

        while (x < a.Length) { ops.Add(('-', a[x++])); }
        while (y < b.Length) { ops.Add(('+', b[y++])); }

        var added = ops.Count(o => o.Op == '+');
        var removed = ops.Count(o => o.Op == '-');
        if (added == 0 && removed == 0)
        {
            return (string.Empty, 0, 0);
        }

        var sb = new StringBuilder();
        sb.Append("--- ").Append(oldName).Append('\n').Append("+++ ").Append(newName).Append('\n');
        var changed = Enumerable.Range(0, ops.Count).Where(i => ops[i].Op != ' ').ToList();
        var cursor = 0;
        while (cursor < changed.Count)
        {
            var first = Math.Max(0, changed[cursor] - context);
            var last = changed[cursor];
            var next = cursor + 1;
            while (next < changed.Count && changed[next] - last <= 2 * context + 1)
            {
                last = changed[next++];
            }

            var end = Math.Min(ops.Count - 1, last + context);
            var oldStart = ops.Take(first).Count(o => o.Op != '+') + 1;
            var newStart = ops.Take(first).Count(o => o.Op != '-') + 1;
            var slice = ops.Skip(first).Take(end - first + 1).ToList();
            sb.Append(CultureInfo.InvariantCulture, $"@@ -{oldStart},{slice.Count(o => o.Op != '+')} +{newStart},{slice.Count(o => o.Op != '-')} @@\n");
            foreach (var (op, line) in slice)
            {
                sb.Append(op).Append(line).Append('\n');
            }

            cursor = next;
        }

        return (sb.ToString(), added, removed);
    }

    private static string[] Lines(string text) => text.ReplaceLineEndings("\n").TrimEnd('\n').Split('\n');
}

/// <summary>
/// The one prompt whose code change can be made mechanically: the Ask orchestrator's. The production layout is a versioned
/// file <c>Prompts/agent/vN.md</c>, a C# constant <c>AgentPrompt.SystemPrompt</c> that must equal the file's body, a
/// <c>Version</c> string, and a test that reads the file by name. A proposal adds <c>vN+1.md</c>, replaces the constant and the
/// version, and points the test at the new file, so the drift test that guards all three still passes. Anything that does not
/// look exactly as expected makes the edit fail with the reason: the proposal then ships as files to apply by hand.
/// </summary>
internal static partial class OrchestratorPromptEdit
{
    public const string CodePath = "backend/src/Raffa.AiFlows/Ask/Agentic/AgentPrompt.cs";
    public const string TestPath = "backend/tests/Raffa.AiFlows.Tests/Ask/Agentic/AgenticAskTurnTests.cs";

    public static string PromptPath(string fileVersion) => $"backend/src/Raffa.AiFlows/Ask/Prompts/agent/{fileVersion}.md";

    [GeneratedRegex("""public const string Version = "agent-v(?<major>\d+)\.(?<minor>\d+)";""")]
    private static partial Regex VersionPattern();

    /// <summary>The next version after the one in <paramref name="codeText"/>: <c>(agent-v1.6, v1.7, agent-v1.7)</c>.</summary>
    public static (string Current, string NextFile, string NextVersion)? NextVersion(string codeText)
    {
        var match = VersionPattern().Match(codeText);
        if (!match.Success)
        {
            return null;
        }

        var major = match.Groups["major"].Value;
        var minor = int.Parse(match.Groups["minor"].Value, CultureInfo.InvariantCulture);
        return ($"v{major}.{minor}", $"v{major}.{minor + 1}", $"agent-v{major}.{minor + 1}");
    }

    /// <summary>The new prompt file: the title with the version, the prompt, and the footer the previous file carries.</summary>
    public static string PromptFile(string nextVersion, string prompt, string previousFile)
    {
        var footerAt = previousFile.LastIndexOf("Bump the version", StringComparison.Ordinal);
        var footer = footerAt >= 0
            ? previousFile[footerAt..].TrimEnd()
            : "Bump the version (`Agentic.AgentPrompt.Version`, whose `SystemPrompt` is exactly the body above) whenever this prompt's wording changes.";
        return $"# Ask Raffa — orchestrator persona ({nextVersion})\n\n{Normalize(prompt)}\n\n{footer}\n";
    }

    public static string Normalize(string text) => AgentPromptResource.Normalize(text);

    /// <summary>
    /// Builds the three changes (new prompt file, <c>AgentPrompt.cs</c>, the test), or says why the repository no longer has the
    /// shape this edit knows.
    /// </summary>
    public static (IReadOnlyList<PlaygroundFileChange>? Changes, string? Error, string? NextVersion) Apply(
        string codeText, string previousPromptFile, string testText, string newPrompt, string note)
    {
        var code = codeText.ReplaceLineEndings("\n");
        var test = testText.ReplaceLineEndings("\n");
        var prompt = Normalize(newPrompt);
        if (prompt.Length == 0 || prompt.Contains("\"\"\"", StringComparison.Ordinal))
        {
            return (null, "The orchestrator prompt is empty or contains a triple quote, which a C# raw string cannot hold.", null);
        }

        if (NextVersion(code) is not { } next)
        {
            return (null, "AgentPrompt.cs has no `Version = \"agent-vN.M\"` constant to bump.", null);
        }

        var docPath = $"Prompts/agent/{next.Current}.md";
        if (!code.Contains(docPath, StringComparison.Ordinal))
        {
            return (null, $"AgentPrompt.cs does not mention {docPath}.", null);
        }

        const string Opening = "public const string SystemPrompt =\n        \"\"\"\n";
        const string Closing = "\n        \"\"\";";
        var start = code.IndexOf(Opening, StringComparison.Ordinal);
        var end = start < 0 ? -1 : code.IndexOf(Closing, start + Opening.Length, StringComparison.Ordinal);
        if (start < 0 || end < 0)
        {
            return (null, "AgentPrompt.SystemPrompt is not a raw string literal in the expected layout.", null);
        }

        var indented = string.Join("\n", prompt.Split('\n').Select(line => line.Length == 0 ? string.Empty : "        " + line));
        var updated = code[..(start + Opening.Length)] + indented + code[end..];
        updated = updated.Replace(docPath, $"Prompts/agent/{next.NextFile}.md", StringComparison.Ordinal);
        updated = VersionPattern().Replace(updated, $"public const string Version = \"{next.NextVersion}\";", 1);

        var summaryClose = updated.IndexOf("/// </summary>\npublic static class AgentPrompt", StringComparison.Ordinal);
        if (summaryClose < 0)
        {
            return (null, "AgentPrompt.cs has no documentation block to record the new version in.", null);
        }

        var line = $"/// {next.NextFile}: {Sanitise(note)}\n";
        updated = updated.Insert(summaryClose, line);

        var testPath = $"\"{next.Current}.md\"";
        if (!test.Contains(testPath, StringComparison.Ordinal))
        {
            return (null, $"The drift test does not read {next.Current}.md by name.", null);
        }

        var updatedTest = test.Replace(testPath, $"\"{next.NextFile}.md\"", StringComparison.Ordinal);
        return (
            [
                new PlaygroundFileChange(PromptPath(next.NextFile), PromptFile(next.NextVersion, prompt, previousPromptFile), $"playground: orchestrator prompt {next.NextVersion}"),
                new PlaygroundFileChange(CodePath, updated, $"playground: AgentPrompt {next.NextVersion} (version, prompt, notes)"),
                new PlaygroundFileChange(TestPath, updatedTest, $"playground: drift test reads {next.NextFile}.md"),
            ],
            null,
            next.NextVersion);
    }

    /// <summary>One line, no markup that could break an XML doc comment.</summary>
    private static string Sanitise(string note)
    {
        var text = Regex.Replace(note, @"\s+", " ").Trim();
        text = text.Replace("&", "and", StringComparison.Ordinal).Replace("<", "(", StringComparison.Ordinal).Replace(">", ")", StringComparison.Ordinal);
        return text.Length > 220 ? text[..220] : text;
    }
}

/// <summary>What a proposal knows about a variant's evidence: its runs next to the baseline's, and the judgements over them.</summary>
internal sealed record ExportRunEvidence(string Workflow, string Sandbox, string Question, string Side, int DurationMs, decimal? CostUsd, string Status);

internal sealed record ExportJudgementEvidence(
    string Workflow, string Question, string Rubric, string? JudgeModel, int Passes, bool PassesAgreed, bool Tie, string? Preferred,
    IReadOnlyList<(string Name, double Weighted)> Scores);

/// <summary>A proposal: the documents it adds (report, proposed prompts and their diffs), and what a person has to apply by hand.</summary>
internal sealed record PlaygroundProposal(
    string Folder, string Branch, string Title, string Body, IReadOnlyList<PlaygroundFileChange> Documents,
    string? OrchestratorPrompt, IReadOnlyList<string> ManualPrompts);

/// <summary>Builds the proposal of a variant: the report with its evidence, the proposed prompts and diffs, and the PR text.</summary>
internal static class PlaygroundProposalBuilder
{
    private static readonly string[] RoleKeys = ["Agent", "Answer", "Analyst", "Extract", "Classify", "Research"];

    public static PlaygroundProposal Build(
        PlaygroundVariantRow variant, PlaygroundOverrides overrides, IReadOnlyList<(string Agent, string Flow, string Version, string Text)> baseline,
        IReadOnlyList<ExportRunEvidence> runs, IReadOnlyList<ExportJudgementEvidence> judgements, DateTimeOffset now)
    {
        var slug = Slug(variant.Name);
        var date = now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var folder = $"docs/playground/proposals/{date}-{slug}";
        var branch = $"playground/{slug}-{now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture)}";
        var documents = new List<PlaygroundFileChange>();
        var manual = new List<string>();
        string? orchestrator = null;

        var prompts = new StringBuilder();
        foreach (var (agent, text) in (overrides.Prompts ?? new Dictionary<string, string>()).OrderBy(p => p.Key, StringComparer.Ordinal))
        {
            var current = baseline.FirstOrDefault(b => b.Agent == agent);
            var proposed = AgentPromptResource.Normalize(text);
            documents.Add(new PlaygroundFileChange($"{folder}/prompts/{agent}.md", proposed + "\n", $"playground: proposed {agent} prompt"));
            var (diff, added, removed) = PlaygroundTextDiff.Unified(
                current.Text ?? string.Empty, proposed, $"{agent} ({current.Version ?? "production"})", $"{agent} (proposed)");
            if (diff.Length > 0)
            {
                documents.Add(new PlaygroundFileChange($"{folder}/prompts/{agent}.diff", diff, $"playground: {agent} prompt diff"));
            }

            prompts.Append(CultureInfo.InvariantCulture, $"- **{agent}** ({current.Flow ?? "?"}, production {current.Version ?? "?"}): +{added} / -{removed} lines. Proposed text: `prompts/{agent}.md`, diff: `prompts/{agent}.diff`.\n");
            if (agent == PlaygroundCatalog.OrchestratorAgent)
            {
                orchestrator = proposed;
            }
            else
            {
                manual.Add(agent);
                var hash = AgentDefinition.HashPrompt(proposed);
                prompts.Append(CultureInfo.InvariantCulture, $"  - Apply by hand: replace the prompt registered for `{agent}` (a versioned file under `Prompts/` or a constant, depending on the flow), bump its version and register the new hash `{hash}`.\n");
            }
        }

        var config = new StringBuilder();
        foreach (var (role, change) in (overrides.Models ?? new Dictionary<string, PlaygroundModelOverride>()).OrderBy(m => m.Key, StringComparer.Ordinal))
        {
            var key = RoleKeys.FirstOrDefault(k => string.Equals(k, role, StringComparison.OrdinalIgnoreCase)) ?? role;
            if (change.ModelId is not null) config.Append(CultureInfo.InvariantCulture, $"- `AiGateway__Models__{key}__ModelId` = `{change.ModelId}`\n");
            if (change.ReasoningEffort is not null) config.Append(CultureInfo.InvariantCulture, $"- `AiGateway__Models__{key}__ReasoningEffort` = `{change.ReasoningEffort}`\n");
            if (change.MaxCompletionTokens is { } tokens) config.Append(CultureInfo.InvariantCulture, $"- `AiGateway__Models__{key}__MaxCompletionTokens` = `{tokens}`\n");
            if (change.Temperature is { } temperature) config.Append(CultureInfo.InvariantCulture, $"- `AiGateway__Models__{key}__Temperature` = `{temperature.ToString(CultureInfo.InvariantCulture)}`\n");
        }

        if (overrides.MaxToolCalls is { } max)
        {
            config.Append(CultureInfo.InvariantCulture, $"- `Chat__AskTurn__AgenticMaxToolCalls` = `{max}`\n");
        }

        var tools = (overrides.DisabledTools ?? []).Count == 0
            ? string.Empty
            : $"Tools taken away from the orchestrator in this variant: {string.Join(", ", overrides.DisabledTools!.Select(t => $"`{t}`"))}. There is no configuration switch for this: removing a tool is a change to the tool catalogue in code.\n";

        var report = new StringBuilder();
        report.Append(CultureInfo.InvariantCulture, $"# Playground proposal: {variant.Name}\n\n");
        if (!string.IsNullOrWhiteSpace(variant.Description))
        {
            report.Append(variant.Description).Append("\n\n");
        }

        report.Append("This is a proposal from the Raffa agent playground, not a release. Nothing here has run in production: the prompts, models and tools below were tried only on the synthetic sandbox workspaces. Review the evidence, run the golden set (`dotnet test tests/Raffa.AiEval`) and merge only if you would ship it.\n\n");
        report.Append("## What changes\n\n");
        report.Append(prompts.Length > 0 ? "### Prompts\n\n" + prompts + "\n" : string.Empty);
        report.Append(config.Length > 0
            ? "### Configuration\n\nNot code: set these in `infra/environments/<env>/variables.tf` (`ai_gateway_extra_env`) for the environments that should use them.\n\n" + config + "\n"
            : string.Empty);
        report.Append(tools.Length > 0 ? "### Tools\n\n" + tools + "\n" : string.Empty);
        if (prompts.Length == 0 && config.Length == 0 && tools.Length == 0)
        {
            report.Append("The variant changes nothing: it is the same as production.\n\n");
        }

        report.Append("## Evidence\n\n");
        report.Append(EvidenceSection(runs, judgements));

        var readme = report.ToString().TrimEnd() + "\n";
        documents.Insert(0, new PlaygroundFileChange($"{folder}/README.md", readme, $"playground: proposal report for {variant.Name}"));

        var title = $"Playground proposal: {variant.Name}";
        var body = new StringBuilder();
        body.Append("Opened from the Raffa agent playground as a **draft**: a proposal for review, not a release.\n\n");
        body.Append(CultureInfo.InvariantCulture, $"Full report, proposed prompts and diffs: `{folder}/`.\n\n");
        if (orchestrator is not null) body.Append("- The orchestrator prompt is proposed as a new version (`AgentPrompt.cs`, `Prompts/agent/`, and the drift test pointed at the new file).\n");
        if (manual.Count > 0) body.Append(CultureInfo.InvariantCulture, $"- Prompts to apply by hand (listed with their new hashes in the report): {string.Join(", ", manual.Select(m => $"`{m}`"))}.\n");
        if (config.Length > 0) body.Append("- Model and limit changes are configuration, listed in the report: they are not applied by this pull request.\n");
        if (tools.Length > 0) body.Append("- Tool removals need a code change and are not made here.\n");
        body.Append("\n").Append(Headline(judgements)).Append("\n\nBefore merging: run `dotnet test tests/Raffa.AiEval` and the AiFlows tests.\n");

        return new PlaygroundProposal(folder, branch, title, body.ToString(), documents, orchestrator, manual);
    }

    private static string Headline(IReadOnlyList<ExportJudgementEvidence> judgements)
    {
        if (judgements.Count == 0)
        {
            return "No judge verdict is attached: the variant has not been judged yet.";
        }

        var won = judgements.Count(j => j.Preferred is not null && j.Preferred != "Baseline");
        return $"The LLM judge preferred the variant in {won} of {judgements.Count} judgement(s) (a signal, not a verdict; see the report).";
    }

    private static string EvidenceSection(IReadOnlyList<ExportRunEvidence> runs, IReadOnlyList<ExportJudgementEvidence> judgements)
    {
        var sb = new StringBuilder();
        if (runs.Count == 0)
        {
            return "No runs of this variant are recorded.\n";
        }

        sb.Append("### Runs\n\n| Workflow | Sandbox | Question | Side | Duration | Cost | Status |\n|---|---|---|---|---|---|---|\n");
        foreach (var r in runs)
        {
            var cost = r.CostUsd is { } c ? "$" + c.ToString("0.0000", CultureInfo.InvariantCulture) : "n/a";
            sb.Append(CultureInfo.InvariantCulture, $"| {r.Workflow} | {r.Sandbox} | {Cell(r.Question)} | {r.Side} | {r.DurationMs} ms | {cost} | {r.Status} |\n");
        }

        sb.Append('\n');
        sb.Append("### Judgements\n\n");
        if (judgements.Count == 0)
        {
            sb.Append("None yet.\n");
            return sb.ToString();
        }

        foreach (var j in judgements)
        {
            var verdict = j.Tie ? "tie" : j.Preferred ?? "n/a";
            sb.Append(CultureInfo.InvariantCulture, $"- **{j.Workflow}**, \"{Cell(j.Question)}\", rubric *{j.Rubric}*, judge `{j.JudgeModel ?? "default"}`, {j.Passes} pass(es): preferred **{verdict}**");
            sb.Append(" (" + string.Join(", ", j.Scores.Select(s => $"{s.Name} {s.Weighted.ToString("0.00", CultureInfo.InvariantCulture)}")) + ")");
            if (j.Passes == 2 && !j.PassesAgreed)
            {
                sb.Append(" — **the verdict changed with the order of the answers: do not rely on it**");
            }

            sb.Append('\n');
        }

        return sb.ToString();
    }

    private static string Cell(string text)
    {
        var flat = Regex.Replace(text, @"\s+", " ").Trim().Replace("|", "/", StringComparison.Ordinal);
        return flat.Length > 140 ? flat[..140] + "\u2026" : flat;
    }

    private static string Slug(string name)
    {
        var slug = Regex.Replace(name.ToLowerInvariant(), "[^a-z0-9]+", "-").Trim('-');
        slug = slug.Length == 0 ? "variant" : slug;
        return slug.Length > 40 ? slug[..40].TrimEnd('-') : slug;
    }

    /// <summary>The steps for the changes a bundle cannot make mechanically, as <c>APPLY.md</c>.</summary>
    public static string ApplyGuide(PlaygroundProposal proposal, string? nextVersion, string? nextFile)
    {
        var sb = new StringBuilder("# Applying this proposal\n\nUnzip at the repository root, then:\n\n");
        if (proposal.OrchestratorPrompt is not null && nextVersion is not null)
        {
            sb.Append(CultureInfo.InvariantCulture, $"1. `backend/src/Raffa.AiFlows/Ask/Agentic/AgentPrompt.cs`: set `Version` to `\"{nextVersion}\"`, replace the `SystemPrompt` raw string with the body of `Prompts/agent/{nextFile}.md` (indent each line by 8 spaces), point the doc comment at `{nextFile}.md` and add a `/// {nextFile}: ...` line.\n");
            sb.Append(CultureInfo.InvariantCulture, $"2. `backend/tests/Raffa.AiFlows.Tests/Ask/Agentic/AgenticAskTurnTests.cs`: make `The_system_prompt_is_exactly_the_reviewed_markdown` read `{nextFile}.md`.\n");
        }

        foreach (var agent in proposal.ManualPrompts)
        {
            sb.Append(CultureInfo.InvariantCulture, $"- `{agent}`: see `prompts/{agent}.md` and the hash in README.md.\n");
        }

        sb.Append("\nThen run `dotnet test tests/Raffa.AiFlows.Tests` and `dotnet test tests/Raffa.AiEval`.\n");
        return sb.ToString();
    }
}
