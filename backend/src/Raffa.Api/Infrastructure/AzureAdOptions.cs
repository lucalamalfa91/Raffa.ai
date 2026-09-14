namespace Raffa.Api.Infrastructure;

/// <summary>
/// Binds the <c>AzureAd</c> configuration section — the four <c>AzureAd__*</c> keys
/// <c>infra/modules/identity</c> publishes onto the API container app (task E18/F01/US01/T01, wave
/// w15, NW-05; ADR-010 w15 footer §1; ADR-005 w15 footer §4). Provisioned since R0 and unconsumed
/// until this task — see that ADR footer's cloud-architect cell.
///
/// <para>
/// Every property is nullable and <c>Program.cs</c> falls back to a plain <c>new
/// AzureAdOptions()</c> when the whole section is absent (a local dev box, or a container image
/// deployed before the Terraform apply that publishes these keys lands) — ADR-016 w15 clause 15
/// "fail closed, never crash closed". A null <see cref="Authority"/> means
/// <c>JwtBearerHandler</c> never resolves a <c>ConfigurationManager</c> and every bearer token fails
/// signature validation instead of throwing at startup; a null <see cref="ClientId"/> means
/// <c>ValidateAudience</c> rejects every token the same way. Both failures surface as a <b>401</b>
/// at the point of use — the API still <b>boots</b>.
/// </para>
/// </summary>
public sealed class AzureAdOptions
{
    public const string SectionName = "AzureAd";

    /// <summary>
    /// This environment's own Entra tenant issuer, e.g.
    /// <c>https://login.microsoftonline.com/&lt;tenant-id&gt;/v2.0</c> — <c>infra/modules/identity</c>'s
    /// own <c>issuer</c> output (<c>outputs.tf:25-28</c>). Doubles as both the OIDC discovery
    /// authority (JWKS) and the pinned <c>iss</c> validation value (ADR-010 w15 footer §1.3/S15-3):
    /// never <c>common</c>, <c>organizations</c> or <c>consumers</c>.
    /// </summary>
    public string? Authority { get; set; }

    /// <summary>
    /// The Entra directory (<c>tid</c>) this environment's app registrations live in. Bound for
    /// completeness with the other three keys; no code path reads a token's <c>tid</c> claim as a
    /// Raffa tenant (ADR-010 w15 footer §2.2/S15-7 — both environments share one directory, so
    /// <c>tid</c> can never select one).
    /// </summary>
    public string? TenantId { get; set; }

    /// <summary>
    /// The API app registration's client id (<c>infra/modules/identity</c>'s <c>api_client_id</c>
    /// output). At <c>requested_access_token_version = 2</c> this is the exact value a v2 access
    /// token's <c>aud</c> claim carries (ADR-010 w15 footer §1.2/S15-2) — the one key
    /// <c>Program.cs</c> binds into <c>TokenValidationParameters.ValidAudience</c>.
    /// </summary>
    public string? ClientId { get; set; }

    /// <summary>
    /// The API's alternate <c>api://...</c> identifier URI (the <c>api_identifier_uri</c> output) a
    /// client <em>requests</em> scopes against. Published alongside <see cref="ClientId"/>
    /// deliberately (ADR-005 w15 footer §4, "removes an entire class of token acquired, then 401")
    /// but is <b>not</b> the JWT audience — wiring this value into <c>ValidateAudience</c> instead of
    /// <see cref="ClientId"/> is the exact hazard ADR-010 w15 footer §1.2 names and rejects every
    /// token; S-T18 asserts the validator compares against <see cref="ClientId"/>. Bound here for
    /// completeness only — <c>Program.cs</c>'s JWT bearer handler never reads it.
    /// </summary>
    public string? Audience { get; set; }
}
