using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Raffa.AiGateway.Contracts;

namespace Raffa.JevBenchmark.Tests.Harness;

/// <summary>One labelled input. <see cref="ExpectedLabel"/> is the ground truth the arms are scored on.</summary>
public sealed record LabelledCase(
    string Track,
    string Id,
    string Text,
    string ExpectedLabel,
    string Language,
    string Category);

/// <summary>A labelled set with its provenance, so a report always says where the labels come from.</summary>
public sealed record CaseSet(
    string Track,
    string Name,
    string Version,
    string Provenance,
    string SourceFile,
    IReadOnlyList<string> Labels,
    IReadOnlyList<LabelledCase> Cases)
{
    /// <summary>SHA-256 over ids, labels and texts: changes whenever the data the arms see changes.</summary>
    public string ContentHash
    {
        get
        {
            var builder = new StringBuilder();
            foreach (var c in Cases)
            {
                builder.Append(c.Id).Append('\u001f').Append(c.ExpectedLabel).Append('\u001f').Append(c.Text).Append('\u001e');
            }

            return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(builder.ToString()))).ToLowerInvariant();
        }
    }

    public CaseSet Take(int max) =>
        max <= 0 || max >= Cases.Count ? this : this with { Cases = Cases.Take(max).ToList() };
}

/// <summary>Loads the two labelled sets from <c>cases/</c> next to the test assembly.</summary>
public static class CaseSets
{
    public const string ClassifyFile = "classify-cases.v1.json";
    public const string InvestigatorFile = "investigator-eval.v1.json";
    public const string GapLabel = "gap";
    public const string NoGapLabel = "no-gap";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    public static string DefaultDirectory => Path.Combine(AppContext.BaseDirectory, "cases");

    public static CaseSet LoadClassify(string? directory = null)
    {
        var path = Path.Combine(directory ?? DefaultDirectory, ClassifyFile);
        var doc = Read<ClassifyDoc>(path);
        var cases = (doc.Cases ?? [])
            .Select(c => new LabelledCase(
                BenchTracks.Classify, c.Id ?? "", c.Text ?? "", c.Expected ?? "", c.Language ?? "", c.Category ?? "synthetic"))
            .ToList();

        return new CaseSet(
            BenchTracks.Classify,
            doc.Name ?? "classify",
            doc.Version ?? "unversioned",
            doc.Provenance ?? "unknown",
            path,
            Enum.GetNames<AiDocumentType>(),
            cases);
    }

    /// <summary>
    /// The INV-04 investigator set (Raffa.AiEval), re-labelled for the Jev comparison: a phrase that a
    /// person would say asks for something Raffa cannot do (<c>gapWorthy</c>) must come out as
    /// <see cref="GapLabel"/>, every other phrase as <see cref="NoGapLabel"/>.
    /// </summary>
    public static CaseSet LoadGaps(string? directory = null)
    {
        var path = Path.Combine(directory ?? DefaultDirectory, InvestigatorFile);
        var doc = Read<InvestigatorDoc>(path);
        var cases = (doc.Cases ?? [])
            .Select(c => new LabelledCase(
                BenchTracks.Gaps, c.Id ?? "", c.Text ?? "", c.GapWorthy ? GapLabel : NoGapLabel, c.Language ?? "", c.Category ?? ""))
            .ToList();

        return new CaseSet(
            BenchTracks.Gaps,
            "investigator-eval (Raffa.AiEval/investigator-set)",
            doc.Version ?? "unversioned",
            $"Reused from Raffa.AiEval INV-04 (status: {doc.Status ?? "unknown"}, date: {doc.Date ?? "unknown"}); " +
            "synthetic seed phrases, labelled gapWorthy by the INV-04 authors, pending product-owner review.",
            path,
            [GapLabel, NoGapLabel],
            cases);
    }

    private static T Read<T>(string path)
    {
        if (!File.Exists(path))
        {
            throw new FileNotFoundException($"Labelled case set not found: '{path}'.", path);
        }

        return JsonSerializer.Deserialize<T>(File.ReadAllText(path), JsonOptions)
               ?? throw new InvalidOperationException($"'{path}' deserialized to null.");
    }

    private sealed record ClassifyDoc(string? Name, string? Version, string? Provenance, List<ClassifyCaseDoc>? Cases);

    private sealed record ClassifyCaseDoc(string? Id, string? Language, string? Expected, string? Category, string? Text);

    private sealed record InvestigatorDoc(string? Version, string? Status, string? Date, List<InvestigatorCaseDoc>? Cases);

    private sealed record InvestigatorCaseDoc(string? Id, string? Language, string? Text, string? Category, bool GapWorthy);
}
