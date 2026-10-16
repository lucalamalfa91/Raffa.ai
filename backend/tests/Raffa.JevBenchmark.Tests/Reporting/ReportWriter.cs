using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Raffa.JevBenchmark.Tests.Metrics;

namespace Raffa.JevBenchmark.Tests.Reporting;

/// <summary>Writes <c>jev-benchmark-result.json</c> and <c>jev-benchmark-report.md</c> and renders the console summary.</summary>
public static class ReportWriter
{
    public const string JsonFileName = "jev-benchmark-result.json";
    public const string MarkdownFileName = "jev-benchmark-report.md";

    public static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals,
    };

    public static (string JsonPath, string MarkdownPath) Write(BenchmarkResult result, string directory)
    {
        Directory.CreateDirectory(directory);
        var jsonPath = Path.Combine(directory, JsonFileName);
        var markdownPath = Path.Combine(directory, MarkdownFileName);
        File.WriteAllText(jsonPath, ToJson(result), new UTF8Encoding(false));
        File.WriteAllText(markdownPath, ToMarkdown(result), new UTF8Encoding(false));
        return (jsonPath, markdownPath);
    }

    public static string ToJson(BenchmarkResult result) => JsonSerializer.Serialize(result, JsonOptions);

    public static string ToMarkdown(BenchmarkResult result)
    {
        var md = new StringBuilder();
        var config = result.Config;

        md.AppendLine("# Benchmark Jev: prima (A) e dopo (B)");
        md.AppendLine();
        md.AppendLine($"- Generato: {result.GeneratedAtUtc} (durata {result.DurationSeconds.ToString("0", CultureInfo.InvariantCulture)} s)");
        md.AppendLine($"- Braccio **A** ({BenchmarkNames.Baseline}): {config.ArmADescription}");
        md.AppendLine($"- Braccio **B** ({BenchmarkNames.WithJev}): {config.ArmBDescription}");
        md.AppendLine("- Tutti i valori sono misure su dati etichettati di prova (vedi provenienza sotto): non sono una garanzia sul traffico reale.");
        md.AppendLine();

        foreach (var track in result.Tracks)
        {
            AppendTrack(md, track);
        }

        AppendConfig(md, config);
        return md.ToString();
    }

    public static string ToConsoleSummary(BenchmarkResult result)
    {
        var text = new StringBuilder();
        text.AppendLine("=== Jev benchmark: summary (A = Foundry only, B = Jev + Foundry fallback) ===");
        foreach (var track in result.Tracks)
        {
            var c = track.Comparison;
            text.AppendLine(
                $"[{track.Track}] cases={track.Dataset.Cases} | acc A={Pct(track.Baseline.StrictAccuracy)} B={Pct(track.WithJev.StrictAccuracy)} " +
                $"| p50 A={Ms(track.Baseline.Latency.P50)} B={Ms(track.WithJev.Latency.P50)} | fallback B={Pct(track.WithJev.FallbackRate)} " +
                $"| errors A={Pct(track.Baseline.ErrorRate)} B={Pct(track.WithJev.ErrorRate)}");
            text.AppendLine($"    {c.Verdict.Summary}");
            foreach (var warning in c.Verdict.Warnings)
            {
                text.AppendLine($"    ! {warning}");
            }
        }

        return text.ToString();
    }

    private static void AppendTrack(StringBuilder md, TrackResult track)
    {
        var a = track.Baseline;
        var b = track.WithJev;
        var c = track.Comparison;
        var title = track.Track == Harness.BenchTracks.Classify
            ? "Classificazione documenti (ruolo `classify`)"
            : "Verdetto del capability investigator (gap / non gap)";

        md.AppendLine($"## {title}");
        md.AppendLine();
        md.AppendLine($"Dataset: **{track.Dataset.Name}** v{track.Dataset.Version}, {track.Dataset.Cases} casi, file `{track.Dataset.SourceFile}`, SHA-256 `{track.Dataset.ContentHash}`.");
        md.AppendLine();
        md.AppendLine($"Provenienza etichette: {track.Dataset.Provenance}");
        md.AppendLine();
        md.AppendLine("Distribuzione etichette: " + string.Join(", ", track.Dataset.LabelCounts.OrderBy(p => p.Key, StringComparer.Ordinal).Select(p => $"{p.Key}={p.Value}")) + ".");
        md.AppendLine();

        md.AppendLine("### Verdetto");
        md.AppendLine();
        md.AppendLine($"- Qualita: **{c.Verdict.Quality}**");
        md.AppendLine($"- Latenza: **{c.Verdict.Latency}**");
        md.AppendLine($"- Costo: **{c.Verdict.Cost}**");
        md.AppendLine();
        md.AppendLine(c.Verdict.Summary);
        md.AppendLine();
        if (c.Verdict.Warnings.Count > 0)
        {
            md.AppendLine("Avvertenze:");
            md.AppendLine();
            foreach (var warning in c.Verdict.Warnings)
            {
                md.AppendLine($"- {warning}");
            }

            md.AppendLine();
        }

        md.AppendLine("### Confronto affiancato");
        md.AppendLine();
        md.AppendLine("| Metrica | A (baseline) | B (Jev) | Delta (B - A) |");
        md.AppendLine("|---|---:|---:|---:|");
        md.AppendLine($"| Chiamate misurate | {a.Calls} | {b.Calls} | |");
        md.AppendLine($"| Accuratezza (errori = sbagliato) | {Pct(a.StrictAccuracy)} | {Pct(b.StrictAccuracy)} | {Points(b.StrictAccuracy - a.StrictAccuracy)} |");
        md.AppendLine($"| Accuratezza sulle risposte date | {Pct(a.AnsweredAccuracy)} | {Pct(b.AnsweredAccuracy)} | {Points(b.AnsweredAccuracy - a.AnsweredAccuracy)} |");
        md.AppendLine($"| F1 macro | {Num(a.MacroF1)} | {Num(b.MacroF1)} | {Num(b.MacroF1 - a.MacroF1)} |");
        md.AppendLine($"| Accuratezza per caso (maggioranza ripetizioni) | {Pct(c.BaselineCaseAccuracy)} | {Pct(c.JevCaseAccuracy)} | {Points(c.AccuracyDelta.Estimate)} (IC95% {Points(c.AccuracyDelta.Lower)} .. {Points(c.AccuracyDelta.Upper)}) |");
        md.AppendLine($"| Latenza p50 | {Ms(a.Latency.P50)} | {Ms(b.Latency.P50)} | {Ms(c.P50DeltaMs)} |");
        md.AppendLine($"| Latenza p95 | {Ms(a.Latency.P95)} | {Ms(b.Latency.P95)} | {Ms(c.P95DeltaMs)} |");
        md.AppendLine($"| Latenza p99 | {Ms(a.Latency.P99)} | {Ms(b.Latency.P99)} | {Ms(c.P99DeltaMs)} |");
        md.AppendLine($"| Latenza media | {Ms(a.Latency.Mean)} | {Ms(b.Latency.Mean)} | {Ms(b.Latency.Mean - a.Latency.Mean)} |");
        md.AppendLine($"| Delta latenza accoppiato per caso (mediana) | | | {Ms(c.PairedLatencyDeltaMs.Estimate)} (IC95% {Ms(c.PairedLatencyDeltaMs.Lower)} .. {Ms(c.PairedLatencyDeltaMs.Upper)}) |");
        md.AppendLine($"| Errori | {a.Errors} ({Pct(a.ErrorRate)}) | {b.Errors} ({Pct(b.ErrorRate)}) | |");
        md.AppendLine($"| Timeout | {a.Timeouts} ({Pct(a.TimeoutRate)}) | {b.Timeouts} ({Pct(b.TimeoutRate)}) | |");
        md.AppendLine($"| Fallback su Foundry | n/a | {b.Fallbacks} ({Pct(b.FallbackRate)}): jev-error {b.FallbackJevError}, low-confidence {b.FallbackLowConfidence} | |");
        md.AppendLine($"| Risposte servite da Jev | n/a | {b.ServedByJev} | |");
        md.AppendLine($"| Stabilita (stesso input ripetuto) | {Pct(a.Stability.MeanConsistency)} | {Pct(b.Stability.MeanConsistency)} | |");
        md.AppendLine($"| Casi con risposta instabile | {a.Stability.CasesWithFlips}/{a.Stability.CasesMeasured} | {b.Stability.CasesWithFlips}/{b.Stability.CasesMeasured} | |");
        md.AppendLine($"| Token Foundry (in/out) | {a.Usage.FoundryInputTokens}/{a.Usage.FoundryOutputTokens} | {b.Usage.FoundryInputTokens}/{b.Usage.FoundryOutputTokens} | |");
        md.AppendLine($"| Token Jev (in/out) | n/a | {b.Usage.JevInputTokens}/{b.Usage.JevOutputTokens} | |");
        md.AppendLine($"| Token medi per chiamata | {Num(a.Usage.MeanTokensPerCall)} | {Num(b.Usage.MeanTokensPerCall)} | {Num(b.Usage.MeanTokensPerCall - a.Usage.MeanTokensPerCall)} |");
        md.AppendLine($"| Tentativi HTTP Jev (falliti) | n/a | {b.Usage.JevHttpAttempts} ({b.Usage.JevHttpFailures}) | |");
        md.AppendLine($"| Costo stimato ogni 1000 chiamate (USD) | {Usd(a.Usage.CostPer1000Calls)} | {Usd(b.Usage.CostPer1000Calls)} | {Usd(c.CostPer1000DeltaUsd)} |");
        md.AppendLine($"| Costo Jev riportato da OpenRouter (USD, totale) | n/a | {Usd(b.Usage.JevReportedCostUsd)} | |");
        md.AppendLine();

        md.AppendLine("### Accordo tra i bracci e test statistici");
        md.AppendLine();
        md.AppendLine($"- Accordo A/B sulle risposte date: {Pct(c.Agreement)} su {c.PairedCalls} coppie (kappa di Cohen {Num(c.Kappa)}).");
        md.AppendLine($"- Tabella McNemar sui {c.CasesCompared} casi (esito di maggioranza): entrambi giusti {c.McNemar.BothCorrect}, solo A giusto {c.McNemar.OnlyBaselineCorrect}, solo B giusto {c.McNemar.OnlyJevCorrect}, entrambi sbagliati {c.McNemar.BothWrong}; p esatto a due code = {c.McNemar.PValue.ToString("0.####", CultureInfo.InvariantCulture)}.");
        md.AppendLine($"- Casi corretti da Jev (A sbaglia, B giusto): {IdList(c.FixedByJev)}.");
        md.AppendLine($"- Casi peggiorati da Jev (A giusto, B sbaglia): {IdList(c.RegressedByJev)}.");
        md.AppendLine();

        AppendArm(md, a);
        AppendArm(md, b);
    }

    private static void AppendArm(StringBuilder md, ArmSummary arm)
    {
        md.AppendLine($"### Dettaglio {arm.Arm}");
        md.AppendLine();
        md.AppendLine("| Classe | Supporto | Precision | Recall | F1 |");
        md.AppendLine("|---|---:|---:|---:|---:|");
        foreach (var c in arm.PerClass.Where(c => c.Support > 0 || c.FalsePositives > 0))
        {
            md.AppendLine($"| {c.Label} | {c.Support} | {Num(c.Precision)} | {Num(c.Recall)} | {Num(c.F1)} |");
        }

        md.AppendLine();
        md.AppendLine("Matrice di confusione (righe = atteso, colonne = predetto):");
        md.AppendLine();
        var labels = arm.ConfusionLabels.Append(ConfusionMatrix.ErrorColumn).ToList();
        md.AppendLine("| atteso \\ predetto | " + string.Join(" | ", labels) + " |");
        md.AppendLine("|---|" + string.Join("|", labels.Select(_ => "---:")) + "|");
        for (var i = 0; i < arm.ConfusionLabels.Count; i++)
        {
            if (arm.Confusion[i].Sum() == 0)
            {
                continue;
            }

            md.AppendLine($"| {arm.ConfusionLabels[i]} | " + string.Join(" | ", arm.Confusion[i]) + " |");
        }

        md.AppendLine();
    }

    private static void AppendConfig(StringBuilder md, BenchConfigSnapshot config)
    {
        var s = config.Settings;
        md.AppendLine("## Configurazione usata (riproducibilita)");
        md.AppendLine();
        md.AppendLine("| Parametro | Valore |");
        md.AppendLine("|---|---|");
        md.AppendLine($"| Foundry endpoint (host) | {config.FoundryEndpointHost} |");
        md.AppendLine($"| Modello classify | {config.ClassifyModelId} (versione {config.ClassifyModelVersion}) |");
        md.AppendLine($"| Modello analyst (investigator) | {config.AnalystModelId} |");
        md.AppendLine($"| Jev modello | {config.JevModel} |");
        md.AppendLine($"| Jev endpoint | {config.JevEndpoint} |");
        md.AppendLine($"| Jev chiave configurata | {(config.JevApiKeyConfigured ? "si (valore non registrato)" : "no")} |");
        md.AppendLine($"| Jev ClassifyMinConfidence | {config.JevClassifyMinConfidence.ToString(CultureInfo.InvariantCulture)} |");
        md.AppendLine($"| Jev CheckOptionOrder | {config.JevCheckOptionOrder} |");
        md.AppendLine($"| Jev ClassifyMaxInputChars | {config.JevClassifyMaxInputChars} |");
        md.AppendLine($"| Investigator soglie Jev alta/media | {config.GapJevHighConfidenceThreshold.ToString(CultureInfo.InvariantCulture)} / {config.GapJevMediumConfidenceThreshold.ToString(CultureInfo.InvariantCulture)} |");
        md.AppendLine($"| Investigator MinConfidence | {config.GapMinConfidence} |");
        md.AppendLine($"| Resilienza: tentativi / timeout richiesta (s) | {config.ResilienceMaxRetries} / {config.ResilienceRequestTimeoutSeconds} |");
        md.AppendLine($"| Ripetizioni / warm-up scartati / pausa (ms) | {s.Repetitions} / {s.WarmupCalls} / {s.DelayBetweenCallsMs} |");
        md.AppendLine($"| Tracce / max casi per traccia | {string.Join(", ", s.Tracks)} / {(s.MaxCasesPerTrack == 0 ? "tutti" : s.MaxCasesPerTrack.ToString(CultureInfo.InvariantCulture))} |");
        md.AppendLine($"| Timeout investigator (s) | {s.GapTimeoutSeconds} |");
        md.AppendLine($"| Bootstrap iterazioni / seed / alpha | {s.BootstrapIterations} / {s.Seed} / {s.Alpha.ToString(CultureInfo.InvariantCulture)} |");
        md.AppendLine($"| Prezzo Foundry USD per 1M token (in/out) | {Usd(s.FoundryUsdPerMillionInputTokens)} / {Usd(s.FoundryUsdPerMillionOutputTokens)} |");
        md.AppendLine($"| Runtime / OS | {config.Runtime} / {config.OperatingSystem} |");
        md.AppendLine($"| Commit | {config.GitSha ?? "n/d"} |");
        md.AppendLine($"| Run CI | {config.RunUrl ?? "n/d"} |");
        md.AppendLine();
    }

    private static string IdList(IReadOnlyList<string> ids) =>
        ids.Count == 0 ? "nessuno" : $"{ids.Count} ({string.Join(", ", ids.Take(25))}{(ids.Count > 25 ? ", ..." : "")})";

    private static string Pct(double v) => double.IsNaN(v) ? "n/a" : (v * 100).ToString("0.0", CultureInfo.InvariantCulture) + "%";

    private static string Points(double v) => double.IsNaN(v) ? "n/a" : (v * 100).ToString("+0.0;-0.0;0.0", CultureInfo.InvariantCulture) + " pt";

    private static string Ms(double v) => double.IsNaN(v) ? "n/a" : v.ToString("0", CultureInfo.InvariantCulture) + " ms";

    private static string Num(double v) => double.IsNaN(v) ? "n/a" : v.ToString("0.###", CultureInfo.InvariantCulture);

    private static string Usd(double? v) => v is null ? "n/d" : v.Value.ToString("0.0000", CultureInfo.InvariantCulture);
}

/// <summary>Display names of the arms, kept next to the report so the wording lives in one place.</summary>
public static class BenchmarkNames
{
    public const string Baseline = Harness.BenchArms.Baseline;
    public const string WithJev = Harness.BenchArms.WithJev;
}
