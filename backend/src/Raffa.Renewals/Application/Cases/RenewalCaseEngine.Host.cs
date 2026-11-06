using Raffa.SharedKernel.Text;

namespace Raffa.Renewals.Application.Cases;

/// <summary>What the API host needs from the engine beyond create/act/view (stage labels, denials).</summary>
public static partial class RenewalCaseEngine
{
    /// <summary>"Start" … "Acquisition" for stage 0–5 (empty outside).</summary>
    public static CaseText StageLabel(int stage) => stage >= 0 && stage < Stg.Length ? Stg[stage] : CaseText.Empty;

    /// <summary>
    /// True when the last <see cref="Act"/> was refused for lack of rights (approve, close, work,
    /// delegate, reassign): the API answers 403 with the case's warning instead of saving.
    /// </summary>
    /// <summary>
    /// Adds a draft written outside the case (Offerta's counter-proposal, slice 6) to the case's email
    /// lifecycle: state <c>draft</c>, no recipient (never invented), superseding open drafts of the same kind.
    /// </summary>
    public static RenewalCase AddExternalDraft(RenewalCase c0, CaseContext ctx, string kind, string subject, string body)
    {
        var c = c0.DeepClone();
        foreach (var x in c.Emails.Where(x => x.Kind == kind && x.State is EmailStates.Draft or EmailStates.ToApprove))
        {
            x.State = EmailStates.Superseded;
        }

        var e = new CaseEmail
        {
            Id = c.Emails.Count,
            Kind = kind,
            Ver = 1,
            To = string.Empty,
            Subject = subject,
            Body = body,
            Attach = [],
            State = EmailStates.Draft,
        };
        c.Emails.Add(e);
        c.Updated = ctx.Today;
        Push(c, ctx, new CaseFeedItem { K = "email", Eid = e.Id });
        return c;
    }

    /// <summary>
    /// Drafts written before contracts had a commercial name carry the uploaded file name in their subject
    /// and text ("Veeam_Subscription_2024.docx — renewal discussion"). An email still in draft takes the
    /// contract's name instead; one already in approval, approved or sent keeps the words it was approved
    /// or sent with. The attachment keeps the file name: that is the file.
    /// </summary>
    public static RenewalCase RenameDraftsFromFileName(RenewalCase c, CaseContractFacts cn)
    {
        ArgumentNullException.ThrowIfNull(c);
        ArgumentNullException.ThrowIfNull(cn);
        var file = cn.File?.Trim() ?? string.Empty;
        if (file.Length == 0 || string.Equals(file, cn.Name, StringComparison.Ordinal))
        {
            return c;
        }

        var stem = Path.GetFileNameWithoutExtension(file);
        // Idempotent: the contract name can itself contain the file stem ("QA06-05-Northstar" inside
        // "QA06-05-Northstar Systems Ltd."), so a plain Replace would grow the text on every run.
        // Occurrences of the name already present are parked behind a sentinel while the file name and stem
        // are swapped, then restored.
        const string Sentinel = "\u0001CONTRACT_NAME\u0001";
        string Fix(string text)
        {
            if (cn.Name.Length > 0)
            {
                text = text.Replace(cn.Name, Sentinel, StringComparison.Ordinal);
            }

            var fixedText = text.Replace(file, Sentinel, StringComparison.Ordinal);
            if (stem.Length >= 6 && !string.Equals(stem, cn.Name, StringComparison.Ordinal))
            {
                fixedText = fixedText.Replace(stem, Sentinel, StringComparison.Ordinal);
            }

            return fixedText.Replace(Sentinel, cn.Name, StringComparison.Ordinal);
        }

        foreach (var e in c.Emails.Where(e => e.State == EmailStates.Draft))
        {
            e.Subject = Fix(e.Subject);
            e.Body = Fix(e.Body);
        }

        return c;
    }

    public static bool IsPermissionDenial(RenewalCase c)
    {
        var id = c.Ui.Warn?.Id;
        return id is not null
               && (id == NoApprove.Id || id == NoClose.Id || id == NoWork.Id || id == NoDelegations.Id
                   || id.StartsWith("Only the approver (CPO) can reassign", StringComparison.Ordinal));
    }
}
