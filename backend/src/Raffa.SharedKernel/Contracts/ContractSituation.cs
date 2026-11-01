using System.Globalization;

namespace Raffa.SharedKernel.Contracts;

/// <summary>What the assessment recommends doing next (a closed vocabulary).</summary>
public static class SituationAction
{
    public const string StartRenewal = "start_renewal";
    public const string PrepareNextNotice = "prepare_next_notice";
    public const string NegotiateMidTerm = "negotiate_mid_term";
    public const string PlanReplacement = "plan_replacement";
    public const string ConfirmTerms = "confirm_terms";
    public const string None = "none";

    public static IReadOnlyList<string> All { get; } =
        [StartRenewal, PrepareNextNotice, NegotiateMidTerm, PlanReplacement, ConfirmTerms, None];

    /// <summary>The actions that make sense for a term status (the guard rejects any other).</summary>
    public static IReadOnlyList<string> AllowedFor(string status) => status switch
    {
        ContractTermStatus.Active => [StartRenewal, NegotiateMidTerm, PlanReplacement, None],
        ContractTermStatus.RolledOver => [StartRenewal, PrepareNextNotice, NegotiateMidTerm, PlanReplacement],
        ContractTermStatus.Ending => [StartRenewal, NegotiateMidTerm, PlanReplacement],
        ContractTermStatus.Expired => [PlanReplacement, None],
        ContractTermStatus.NeedsReview => [ConfirmTerms],
        _ => [ConfirmTerms, None],
    };
}

/// <summary>How soon the user should act.</summary>
public static class SituationUrgency
{
    public const string Now = "now";
    public const string Soon = "soon";
    public const string Later = "later";
    public const string None = "none";

    public static IReadOnlyList<string> All { get; } = [Now, Soon, Later, None];
}

/// <summary>A text already written in both languages (the model writes both; so do the rules).</summary>
public sealed record LocalizedText(string En, string It)
{
    public string For(string lang) => string.Equals(lang, "it", StringComparison.OrdinalIgnoreCase) ? It : En;
}

/// <summary>The clause an assessment rests on.</summary>
/// <param name="Key">The evidence key (<c>sf_renew</c>) or <c>std:N</c> for a standard term.</param>
public sealed record AssessmentSource(string Key, string Doc, int? Page, string? Section, string? Quote);

/// <summary>
/// A contract's situation assessment: the deterministic <see cref="Term"/> (dates are never
/// computed by a model) and the judgement on it — headline, why, recommended action, urgency,
/// source. <see cref="Origin"/> is <c>ai</c> when the model's answer passed the guard, else
/// <c>rules</c> (deterministic copy).
/// </summary>
public sealed record ContractAssessment(
    Guid ContractId,
    EffectiveContractTerm Term,
    LocalizedText Headline,
    LocalizedText Why,
    string RecommendedAction,
    string Urgency,
    AssessmentSource? Source,
    string Origin);

/// <summary>Assesses a contract's situation for today. The default (<see cref="RulesContractSituationAssessor"/>)
/// writes deterministic copy; <c>Raffa.AiFlows</c> replaces it with the model-backed, guarded one.</summary>
public interface IContractSituationAssessor
{
    Task<ContractAssessment> AssessAsync(TenantId tenantId, ContractFacts facts, DateOnly today, CancellationToken cancellationToken = default);
}

/// <summary>Deterministic assessment only (no model).</summary>
public sealed class RulesContractSituationAssessor : IContractSituationAssessor
{
    public Task<ContractAssessment> AssessAsync(TenantId tenantId, ContractFacts facts, DateOnly today, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(facts);
        return Task.FromResult(ContractSituationCopy.Rules(facts, ContractTermCalculator.Compute(facts, today)));
    }
}

/// <summary>The deterministic wording of an assessment, in English and Italian, and the clause it cites.</summary>
public static class ContractSituationCopy
{
    /// <summary>Notice deadlines this close have to be started now (prototype: 120 days).</summary>
    public const int StartWindowDays = 120;

    private static readonly string[] ItMonths = ["Gen", "Feb", "Mar", "Apr", "Mag", "Giu", "Lug", "Ago", "Set", "Ott", "Nov", "Dic"];

    /// <summary>"07 Jul 2026".</summary>
    public static string En(DateOnly? d) => d is { } v ? v.ToString("dd MMM yyyy", CultureInfo.InvariantCulture) : "—";

    /// <summary>"07 Lug 2026".</summary>
    public static string It(DateOnly? d) => d is { } v
        ? string.Create(CultureInfo.InvariantCulture, $"{v.Day:00} {ItMonths[v.Month - 1]} {v.Year}")
        : "—";

    public static ContractAssessment Rules(ContractFacts facts, EffectiveContractTerm term)
    {
        ArgumentNullException.ThrowIfNull(facts);
        ArgumentNullException.ThrowIfNull(term);
        var source = Source(facts, term.Status);
        var (headline, why, action, urgency) = Copy(term);
        return new ContractAssessment(facts.ContractId, term, headline, WithCitation(why, source), action, urgency, source, "rules");
    }

    /// <summary>Appends the citation (or the honest "cannot cite") unless the text already names the page.</summary>
    public static LocalizedText WithCitation(LocalizedText why, AssessmentSource? source)
    {
        ArgumentNullException.ThrowIfNull(why);
        var (en, it) = Citation(source);
        var page = source?.Page is { } p ? "p. " + p.ToString(CultureInfo.InvariantCulture) : null;
        bool Cites(string text) => page is not null
            ? text.Contains(page, StringComparison.Ordinal) || text.Contains(page.Replace(" ", "", StringComparison.Ordinal), StringComparison.Ordinal)
            : text.Contains(en, StringComparison.Ordinal) || text.Contains(it, StringComparison.Ordinal);
        return new LocalizedText(
            Cites(why.En) ? why.En : (why.En.TrimEnd() + " " + en),
            Cites(why.It) ? why.It : (why.It.TrimEnd() + " " + it));
    }

    public static (string En, string It) Citation(AssessmentSource? source)
    {
        if (source is null)
        {
            return ("I cannot cite the clause: no source page was recorded for it.",
                "Non posso citare la clausola: non è registrata la pagina di origine.");
        }

        var where = source.Page is { } p ? ", p. " + p.ToString(CultureInfo.InvariantCulture) : "";
        var sec = string.IsNullOrWhiteSpace(source.Section) ? "" : ", § " + source.Section;
        return ($"Source: {source.Doc}{where}{sec}.", $"Fonte: {source.Doc}{where}{sec}.");
    }

    /// <summary>The clause about renewal/notice the contract's evidence carries (renewal term first
    /// when the question is the term), else a standard term with a page; null when none.</summary>
    public static AssessmentSource? Source(ContractFacts facts, string status)
    {
        ArgumentNullException.ThrowIfNull(facts);
        var termFirst = status is ContractTermStatus.RolledOver or ContractTermStatus.NeedsReview;
        int Rank(string key)
        {
            var k = key.ToLowerInvariant();
            if (k.Contains("renewterm", StringComparison.Ordinal)) return termFirst ? 0 : 2;
            if (k.Contains("renew", StringComparison.Ordinal)) return termFirst ? 1 : 0;
            if (k.Contains("notice", StringComparison.Ordinal)) return termFirst ? 2 : 1;
            if (k.Contains("term", StringComparison.Ordinal)) return 3;
            return int.MaxValue;
        }

        var hit = facts.Evidence
            .Select(kv => (kv.Key, kv.Value, Rank: Rank(kv.Key)))
            .Where(x => x.Rank != int.MaxValue)
            .OrderBy(x => x.Rank).ThenBy(x => x.Key, StringComparer.Ordinal)
            .FirstOrDefault();
        if (hit.Value is { } ev)
        {
            return new AssessmentSource(hit.Key, ev.Doc, ev.Page > 0 ? ev.Page : null, string.IsNullOrWhiteSpace(ev.Sec) ? null : ev.Sec, ev.Quote);
        }

        string[] topics = ["renew", "rinnov", "notice", "disdetta", "terminat"];
        for (var i = 0; i < facts.Deep.Std.Count; i++)
        {
            var s = facts.Deep.Std[i];
            if (topics.Any(t => s.Topic.Contains(t, StringComparison.OrdinalIgnoreCase)) && PageOf(s.Source) is { } page)
            {
                return new AssessmentSource("std:" + i.ToString(CultureInfo.InvariantCulture), facts.File, page, null, s.Says);
            }
        }

        return null;
    }

    private static int? PageOf(string? src)
    {
        if (string.IsNullOrWhiteSpace(src))
        {
            return null;
        }

        var digits = new string(src.SkipWhile(c => !char.IsDigit(c)).TakeWhile(char.IsDigit).ToArray());
        return int.TryParse(digits, NumberStyles.None, CultureInfo.InvariantCulture, out var p) && p > 0 ? p : null;
    }

    private static string UrgencyFor(int? days) => days switch
    {
        null => SituationUrgency.Later,
        <= 30 => SituationUrgency.Now,
        <= StartWindowDays => SituationUrgency.Soon,
        _ => SituationUrgency.Later,
    };

    private static (LocalizedText Headline, LocalizedText Why, string Action, string Urgency) Copy(EffectiveContractTerm t)
    {
        string N(int? n) => (n ?? 0).ToString(CultureInfo.InvariantCulture);
        switch (t.Status)
        {
            case ContractTermStatus.Active:
            {
                var months = t.RenewalTermMonths;
                var why = t.AutoRenew
                    ? months is { } m
                        ? new LocalizedText(
                            $"It renews by itself for {N(m)} months if nothing happens by {En(t.NoticeDeadline)}.",
                            $"Si rinnova da solo per {N(m)} mesi se non succede nulla entro il {It(t.NoticeDeadline)}.")
                        : new LocalizedText(
                            $"It renews by itself if nothing happens by {En(t.NoticeDeadline)}.",
                            $"Si rinnova da solo se non succede nulla entro il {It(t.NoticeDeadline)}.")
                    : new LocalizedText(
                        $"It ends on {En(t.End)}; to renew or renegotiate, start before {En(t.NoticeDeadline)}.",
                        $"Termina il {It(t.End)}; per rinnovare o rinegoziare, parti prima del {It(t.NoticeDeadline)}.");
                return (new LocalizedText($"Notice deadline on {En(t.NoticeDeadline)}", $"Scadenza di disdetta il {It(t.NoticeDeadline)}"),
                    why, SituationAction.StartRenewal, UrgencyFor(t.NoticeDays));
            }

            case ContractTermStatus.RolledOver:
            {
                var past = t.RenewedOn is { } r && r <= t.Today;
                var headline = past
                    ? new LocalizedText($"Renewed by itself on {En(t.RenewedOn)}", $"Rinnovato da solo il {It(t.RenewedOn)}")
                    : new LocalizedText($"Renews by itself on {En(t.RenewedOn)}", $"Si rinnova da solo il {It(t.RenewedOn)}");
                var why = new LocalizedText(
                    $"The notice deadline of {En(t.LockedOn)} has passed, so the contract runs for another {N(t.RenewalTermMonths)} months, until {En(t.End)}. The next notice deadline is {En(t.NoticeDeadline)}, in {N(t.NoticeDays)} days.",
                    $"La scadenza di disdetta del {It(t.LockedOn)} è passata, quindi il contratto prosegue per altri {N(t.RenewalTermMonths)} mesi, fino al {It(t.End)}. La prossima scadenza di disdetta è il {It(t.NoticeDeadline)}, tra {N(t.NoticeDays)} giorni.");
                var action = t.NoticeDays <= StartWindowDays ? SituationAction.StartRenewal : SituationAction.PrepareNextNotice;
                return (headline, why, action, UrgencyFor(t.NoticeDays));
            }

            case ContractTermStatus.Ending:
                return (new LocalizedText($"Ends on {En(t.End)} — it does not renew by itself", $"Termina il {It(t.End)} — non si rinnova da solo"),
                    new LocalizedText(
                        $"The notice deadline of {En(t.OriginalNoticeDeadline)} has passed and the contract has no automatic renewal: it ends on {En(t.End)}, in {N(t.EndDays)} days. You can still negotiate a new contract before then.",
                        $"La scadenza di disdetta del {It(t.OriginalNoticeDeadline)} è passata e il contratto non ha rinnovo automatico: termina il {It(t.End)}, tra {N(t.EndDays)} giorni. Puoi ancora negoziare un nuovo contratto prima di allora."),
                    SituationAction.PlanReplacement, UrgencyFor(t.EndDays));

            case ContractTermStatus.Expired:
                return (new LocalizedText($"Expired on {En(t.End)}", $"Scaduto il {It(t.End)}"),
                    new LocalizedText(
                        $"The contract ended on {En(t.End)} and does not renew by itself. If the service is still in use, plan a new contract.",
                        $"Il contratto è terminato il {It(t.End)} e non si rinnova da solo. Se il servizio è ancora in uso, pianifica un nuovo contratto."),
                    SituationAction.PlanReplacement, SituationUrgency.None);

            case ContractTermStatus.NeedsReview:
                return (new LocalizedText("Confirm the renewal term", "Conferma la durata del rinnovo"),
                    t.AutoRenew
                        ? new LocalizedText(
                            $"The notice deadline of {En(t.OriginalNoticeDeadline)} has passed and the contract renews by itself, but the renewal term was not read. Confirm it to know the new end date and the next notice deadline.",
                            $"La scadenza di disdetta del {It(t.OriginalNoticeDeadline)} è passata e il contratto si rinnova da solo, ma la durata del rinnovo non è stata letta. Confermala per conoscere la nuova fine e la prossima scadenza di disdetta.")
                        : new LocalizedText(
                            $"The notice deadline of {En(t.OriginalNoticeDeadline)} has passed but the end date was not read. Confirm the contract dates.",
                            $"La scadenza di disdetta del {It(t.OriginalNoticeDeadline)} è passata ma la data di fine non è stata letta. Conferma le date del contratto."),
                    SituationAction.ConfirmTerms, SituationUrgency.Soon);

            default:
                return (new LocalizedText("Dates to verify", "Date da verificare"),
                    new LocalizedText("Neither the end date nor the notice deadline was read.", "Non sono state lette né la data di fine né la scadenza di disdetta."),
                    SituationAction.ConfirmTerms, SituationUrgency.Later);
        }
    }
}

/// <summary>The assessment as the screens read it (<c>assessment</c> on Oggi items, Contract 360, Contratti rows).</summary>
public sealed record ContractAssessmentView(
    string Status,
    bool RolledOver,
    string? RenewedOn,
    bool RenewedOnPassed,
    string? LockedOn,
    string? Start,
    string? End,
    string? NoticeDeadline,
    int? NoticeDays,
    int? EndDays,
    string? OriginalEnd,
    string? OriginalNoticeDeadline,
    int? RenewalTermMonths,
    LocalizedText Headline,
    LocalizedText Why,
    string RecommendedAction,
    string Urgency,
    AssessmentSource? Source,
    string Origin)
{
    public static ContractAssessmentView Of(ContractAssessment a)
    {
        ArgumentNullException.ThrowIfNull(a);
        var t = a.Term;
        string? D(DateOnly? d) => d is { } v ? ContractSituationCopy.En(v) : null;
        return new ContractAssessmentView(
            t.Status, t.RolledOver, D(t.RenewedOn), t.RenewedOn is { } r && r <= t.Today, D(t.LockedOn), D(t.Start), D(t.End), D(t.NoticeDeadline),
            t.NoticeDays, t.EndDays, D(t.OriginalEnd), D(t.OriginalNoticeDeadline), t.RenewalTermMonths,
            a.Headline, a.Why, a.RecommendedAction, a.Urgency, a.Source, a.Origin);
    }
}
