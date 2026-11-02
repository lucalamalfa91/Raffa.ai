using Microsoft.EntityFrameworkCore;
using Raffa.SharedKernel;

namespace Raffa.Renewals.Application.CaseWork;

public sealed partial class RenewalCaseService
{
    /// <summary>
    /// Removes everything the 4.0 renewal case keeps for these contracts — the cases, their emails,
    /// delegations, events, vendor messages and reminders, and the Oggi snoozes of their items — when a
    /// contract is deleted. Returns how many cases were removed.
    /// </summary>
    public async Task<int> DeleteForContractsAsync(TenantId tenantId, IReadOnlyCollection<Guid> contractIds, CancellationToken ct = default)
    {
        if (contractIds.Count == 0)
        {
            return 0;
        }

        using var scope = tenantContext.BeginScope(tenantId);
        var entityIds = contractIds.Select(id => new EntityId(id)).ToList();
        var cases = await db.RenewalCases.Where(r => entityIds.Contains(r.ContractId)).ToListAsync(ct).ConfigureAwait(false);
        var caseIds = cases.Select(c => c.Id).ToList();
        var caseGuids = caseIds.Select(c => (Guid?)c.Value).ToList();

        db.CaseEmails.RemoveRange(await db.CaseEmails.Where(x => caseIds.Contains(x.CaseId)).ToListAsync(ct).ConfigureAwait(false));
        db.CaseDelegations.RemoveRange(await db.CaseDelegations.Where(x => caseIds.Contains(x.CaseId)).ToListAsync(ct).ConfigureAwait(false));
        db.CaseEvents.RemoveRange(await db.CaseEvents.Where(x => caseIds.Contains(x.CaseId)).ToListAsync(ct).ConfigureAwait(false));
        db.CaseVendorMessages.RemoveRange(await db.CaseVendorMessages.Where(x => caseIds.Contains(x.CaseId)).ToListAsync(ct).ConfigureAwait(false));
        db.UserReminders.RemoveRange(await db.UserReminders.Where(x => x.CaseId != null && caseGuids.Contains(x.CaseId)).ToListAsync(ct).ConfigureAwait(false));
        var prefixes = contractIds.Select(id => id.ToString() + ":").ToList();
        var snoozes = await db.TodaySnoozes.ToListAsync(ct).ConfigureAwait(false);
        db.TodaySnoozes.RemoveRange(snoozes.Where(s => prefixes.Any(p => s.ItemKey.Contains(p, StringComparison.Ordinal))));
        db.RenewalCases.RemoveRange(cases);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return cases.Count;
    }
}
