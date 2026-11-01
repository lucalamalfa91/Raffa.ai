namespace Raffa.SharedKernel.Contracts;

/// <summary>
/// AskRaffa 4.0: a text the client translates (<c>{id, vars}</c>; <c>id</c> = the English message,
/// translated by the web's Italian catalogue). A var may itself be a <see cref="ContractMessage"/>.
/// </summary>
public sealed record ContractMessage(string Id, IReadOnlyDictionary<string, object?>? Vars = null)
{
    public static ContractMessage Of(string id) => new(id);

    public static ContractMessage Of(string id, params (string Name, object? Value)[] vars) =>
        new(id, vars.ToDictionary(v => v.Name, v => v.Value, StringComparer.Ordinal));
}

/// <summary>Case outcome of a contract's renewal case (AskRaffa 4.0 status tag *Rinnovato / Disdetto*).</summary>
public static class ContractCaseOutcome
{
    public const string Renewed = "renewed";
    public const string Terminated = "terminated";
}

/// <summary>
/// The renewal-case state of one contract, as Contratti / Contratto show it: whether a case is
/// open (status tag *Pratica aperta*, CTA *Apri la pratica*), its id, its outcome once closed
/// (<see cref="ContractCaseOutcome"/>) and the case's next action (<c>RC.view(case).hdr.next</c>).
/// </summary>
public sealed record ContractCaseStatus(
    Guid ContractId,
    bool HasOpenCase,
    Guid? CaseId,
    string? Outcome,
    ContractMessage? NextAction)
{
    public static ContractCaseStatus None(Guid contractId) => new(contractId, false, null, null, null);

    /// <summary>A case exists (open or closed with an outcome).</summary>
    public bool HasCase => CaseId is not null;
}

/// <summary>
/// Reads the renewal-case state per contract. Owned by the contracts screens (slice 4) and
/// implemented by <c>Raffa.Renewals</c> (the case aggregate); until that implementation is
/// registered (<c>services.Replace(...)</c>), <see cref="NoContractCaseStatusReader"/> answers
/// "no case" for every contract.
/// </summary>
public interface IContractCaseStatusReader
{
    Task<IReadOnlyDictionary<Guid, ContractCaseStatus>> GetAsync(
        TenantId tenantId, IReadOnlyCollection<Guid> contractIds, CancellationToken cancellationToken = default);
}

/// <summary>The default <see cref="IContractCaseStatusReader"/>: no contract has a case.</summary>
public sealed class NoContractCaseStatusReader : IContractCaseStatusReader
{
    public Task<IReadOnlyDictionary<Guid, ContractCaseStatus>> GetAsync(
        TenantId tenantId, IReadOnlyCollection<Guid> contractIds, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(contractIds);
        IReadOnlyDictionary<Guid, ContractCaseStatus> result = contractIds
            .Distinct()
            .ToDictionary(id => id, ContractCaseStatus.None);
        return Task.FromResult(result);
    }
}
