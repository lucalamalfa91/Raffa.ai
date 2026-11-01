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

    public static bool IsPermissionDenial(RenewalCase c)
    {
        var id = c.Ui.Warn?.Id;
        return id is not null
               && (id == NoApprove.Id || id == NoClose.Id || id == NoWork.Id || id == NoDelegations.Id
                   || id.StartsWith("Only the approver (CPO) can reassign", StringComparison.Ordinal));
    }
}
