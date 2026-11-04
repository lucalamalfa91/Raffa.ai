using System.Globalization;
using Raffa.Renewals.Application.Cases;
using Raffa.SharedKernel;
using Raffa.SharedKernel.Contracts;

namespace Raffa.Renewals.Application.CaseWork;

/// <summary>
/// The workspace members a case can name (BACKEND-SPEC §2.3). The engine speaks in display names
/// ("Luca Lamalfa"); the API maps them to workspace users. Implemented by the API host over the
/// Identity module (Renewals may not reference it).
/// </summary>
public interface ICaseMemberDirectory
{
    Task<IReadOnlyList<CaseMember>> ListAsync(TenantId tenantId, CancellationToken cancellationToken = default);

    /// <summary>The workspace's company name, which signs the case emails; null when unknown.</summary>
    Task<string?> CompanyNameAsync(TenantId tenantId, CancellationToken cancellationToken = default) => Task.FromResult<string?>(null);

    /// <summary>The workspace's currency (ISO code) and country, for the anonymised market benchmark; nulls when unknown.</summary>
    Task<(string? Currency, string? Country)> MarketProfileAsync(TenantId tenantId, CancellationToken cancellationToken = default) =>
        Task.FromResult<(string?, string?)>((null, null));
}

/// <param name="UserId">Workspace user id.</param>
/// <param name="ExternalSubjectId">The identity (<c>oid</c>) the caller signs in with.</param>
/// <param name="Name">Display name — the engine's person key.</param>
/// <param name="Title">"Head of Procurement" (Admin) / "Buyer" (Procurement) …</param>
/// <param name="IsAdmin">Workspace Admin: the CPO, default approver of every case.</param>
public sealed record CaseMember(Guid UserId, string? ExternalSubjectId, string Name, string Title, bool IsAdmin);

/// <summary>The caller as a case sees them.</summary>
public sealed record CaseCaller(Guid? UserId, string Name, string Title, bool IsAdmin)
{
    public static CaseCaller From(IReadOnlyList<CaseMember> members, string? externalSubjectId)
    {
        var m = members.FirstOrDefault(x => x.ExternalSubjectId is not null && string.Equals(x.ExternalSubjectId, externalSubjectId, StringComparison.Ordinal));
        return m is null
            ? new CaseCaller(null, externalSubjectId ?? "—", string.Empty, false)
            : new CaseCaller(m.UserId, m.Name, m.Title, m.IsAdmin);
    }
}

/// <summary>Maps the shared <see cref="ContractFacts"/> onto the engine's <see cref="CaseContractFacts"/>.</summary>
public static class CaseFactsMapper
{
    /// <param name="today">When given, a term that rolled over by itself (notice passed, automatic
    /// renewal) is read on its effective dates (<see cref="ContractTermCalculator"/>), so a case and
    /// Oggi work on the next notice deadline, not on one that passed.</param>
    public static CaseContractFacts ToCaseFacts(ContractFacts f, DateOnly? today = null)
    {
        ArgumentNullException.ThrowIfNull(f);
        var end = f.EndDate ?? Parse(f.End) ?? DateOnly.MinValue;
        // No end date read: there is no notice deadline either (and MinValue.AddDays(-n) would throw).
        var cancel = f.NoticeDeadline ?? Parse(f.Cancel) ?? (end == DateOnly.MinValue ? DateOnly.MinValue : end.AddDays(-f.Notice));
        var start = f.StartDate ?? Parse(f.Start) ?? DateOnly.MinValue;
        if (today is { } d && ContractTermCalculator.Compute(f, d) is { RolledOver: true } term)
        {
            start = term.Start ?? start;
            end = term.End ?? end;
            cancel = term.NoticeDeadline ?? cancel;
        }

        return new CaseContractFacts
        {
            Id = f.FixtureId ?? 0,
            Supplier = f.Supplier,
            Category = f.Category ?? string.Empty,
            Name = f.Name,
            Type = f.Type,
            File = f.File,
            Spend = (double)f.Spend,
            Currency = f.Currency,
            Start = start,
            End = end,
            Cancel = cancel,
            Notice = f.Notice,
            Auto = string.Equals(f.Auto, "Yes", StringComparison.OrdinalIgnoreCase) || string.Equals(f.Auto, "true", StringComparison.OrdinalIgnoreCase),
            Uplift = string.IsNullOrWhiteSpace(f.Uplift) ? "0%" : f.Uplift,
            Products = f.Deep.Products.Select(p => new CaseProduct(p.Name, p.Sku, p.Unit, (double)p.Quantity, (double)p.UnitPrice, ContractBillingPeriod.IsOneOff(p.BillingPeriod))).ToList(),
            Push = f.Deep.Push.Select(x => new CaseClause(x.Topic, x.Says, x.Proposal, x.Source)).ToList(),
            Raise = f.Deep.Raise.Select(x => new CaseClause(x.Topic, x.Says, x.Proposal, x.Source)).ToList(),
            Terms = f.Deep.Terms.Select(t => new CaseKeyTerm(t.Label, t.Value)).ToList(),
            OblYou = f.Deep.OblYou.Select(o => new CaseObligation(o.What, o.When, o.Priority, o.Recurrence)).ToList(),
            OblSup = f.Deep.OblSup.Select(o => new CaseObligation(o.What, o.When, o.Priority, o.Recurrence)).ToList(),
        };
    }

    /// <summary>"Sales Cloud Enterprise" from "Sales Cloud Enterprise — MSA + Order Form".</summary>
    public static string ShortName(string name) => name.Split(" — ")[0];

    private static DateOnly? Parse(string? s)
    {
        if (string.IsNullOrWhiteSpace(s))
        {
            return null;
        }

        return DateOnly.TryParseExact(s, "dd MMM yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d : null;
    }
}
