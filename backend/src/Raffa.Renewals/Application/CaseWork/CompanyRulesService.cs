using Microsoft.EntityFrameworkCore;
using Raffa.Renewals.Domain.Cases;
using Raffa.Renewals.Infrastructure;
using Raffa.SharedKernel;
using Raffa.SharedKernel.Renewals;
using Raffa.SharedKernel.Tenancy;

namespace Raffa.Renewals.Application.CaseWork;

/// <summary>
/// The tenant's company rules: read, saved, and refined by Raffa's interview or distilled from an
/// uploaded policy. The alerting parameter that comes out of them (<see cref="CompanyRules.StartWindowDays"/>)
/// is what Oggi and Rinnovi read; the text is what Raffa reads when it writes and judges.
/// </summary>
public sealed class CompanyRulesService(
    RenewalsDbContext db,
    ITenantContext tenantContext,
    ICompanyRulesAssistant assistant,
    ICaseMemberDirectory memberDirectory,
    IClock clock)
{
    /// <summary>Only a workspace Admin (the Head of Procurement) writes the rules: they bind everyone's alerts.</summary>
    public async Task<bool> CanEditAsync(TenantId tenantId, string? identity, CancellationToken ct = default)
    {
        var members = await memberDirectory.ListAsync(tenantId, ct).ConfigureAwait(false);
        return CaseCaller.From(members, identity).IsAdmin;
    }

    public async Task<CompanyRules> GetAsync(TenantId tenantId, CancellationToken ct = default)
    {
        using var scope = tenantContext.BeginScope(tenantId);
        var row = await db.CompanyRules.AsNoTracking().FirstOrDefaultAsync(ct).ConfigureAwait(false);
        return row is null ? CompanyRules.Default : ToView(row);
    }

    /// <summary>Saves the rules. Null when the text is too long; the window is clamped to its bounds.</summary>
    public async Task<CompanyRules?> SaveAsync(
        TenantId tenantId, string actor, string text, int? startWindowDays, string? sourceFile, CancellationToken ct = default)
    {
        text = (text ?? string.Empty).Trim();
        if (text.Length > CompanyRulesDefaults.MaxTextLength)
        {
            return null;
        }

        using var scope = tenantContext.BeginScope(tenantId);
        var row = await db.CompanyRules.FirstOrDefaultAsync(ct).ConfigureAwait(false);
        var window = CompanyRulesDefaults.ClampStartWindow(startWindowDays ?? row?.StartWindowDays ?? CompanyRulesDefaults.StartWindowDays);
        var file = string.IsNullOrWhiteSpace(sourceFile) ? row?.SourceFile : sourceFile.Trim();
        if (row is null)
        {
            row = new CompanyRulesRecord { TenantId = tenantId, Text = text, StartWindowDays = window, SourceFile = file, UpdatedBy = actor, UpdatedAt = clock.UtcNow };
            db.CompanyRules.Add(row);
        }
        else
        {
            row.Text = text;
            row.StartWindowDays = window;
            row.SourceFile = file;
            row.UpdatedBy = actor;
            row.UpdatedAt = clock.UtcNow;
        }

        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return ToView(row);
    }

    public async Task<RuleInterviewReply> InterviewAsync(
        TenantId tenantId, IReadOnlyList<RuleTurn> history, string? currentText, string lang, CancellationToken ct = default)
    {
        var current = currentText ?? (await GetAsync(tenantId, ct).ConfigureAwait(false)).Text;
        return await assistant.InterviewAsync(tenantId, current, Trim(history), lang, ct).ConfigureAwait(false);
    }

    public async Task<RuleDistillation> DistillAsync(
        TenantId tenantId, string fileName, string documentText, string lang, CancellationToken ct = default)
    {
        var current = (await GetAsync(tenantId, ct).ConfigureAwait(false)).Text;
        return await assistant.DistillAsync(tenantId, fileName, documentText, current, lang, ct).ConfigureAwait(false);
    }

    /// <summary>The start window alone, for the screens that decide what shows today.</summary>
    internal async Task<int> StartWindowDaysAsync(TenantId tenantId, CancellationToken ct)
    {
        using var scope = tenantContext.BeginScope(tenantId);
        var days = await db.CompanyRules.AsNoTracking().Select(r => (int?)r.StartWindowDays).FirstOrDefaultAsync(ct).ConfigureAwait(false);
        return days ?? CompanyRulesDefaults.StartWindowDays;
    }

    private static IReadOnlyList<RuleTurn> Trim(IReadOnlyList<RuleTurn> history) =>
        history.Where(t => t.Role is "user" or "raffa" && !string.IsNullOrWhiteSpace(t.Text))
            .TakeLast(40)
            .Select(t => new RuleTurn(t.Role, t.Text.Length > 2000 ? t.Text[..2000] : t.Text))
            .ToList();

    private static CompanyRules ToView(CompanyRulesRecord r) =>
        new(r.Text, r.StartWindowDays, r.SourceFile, r.UpdatedAt, r.UpdatedBy, false);
}
