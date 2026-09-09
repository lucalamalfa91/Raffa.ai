namespace Contigo.AiGateway.Configuration;

/// <summary>
/// Foundry connection configuration (ADR-004, ADR-008, ADR-017), added by task
/// E13/F01/US01/T02 (foundry-gateway). Binds the same root <c>"AiGateway"</c> configuration
/// section <c>infra/modules/containerapps/main.tf</c> already injects as three non-secret
/// Container Apps env vars — <c>AiGateway__Endpoint</c>, <c>AiGateway__ProjectName</c>,
/// <c>AiGateway__DocumentIntelligenceConnection</c> (see <c>infra/README.md</c> "AI Gateway /
/// Foundry + Document Intelligence") — rather than a nested subsection like
/// <see cref="AiGatewayModelOptions"/> (<c>"AiGateway:Models"</c>) or
/// <see cref="AiGatewayOcrOptions"/> (<c>"AiGateway:Ocr"</c>), because those three keys already
/// exist at the top level and this options type must bind them exactly as infra already writes
/// them, not a level deeper. Binding the same parent section as a sibling class is safe:
/// <see cref="Microsoft.Extensions.Configuration.ConfigurationBinder.Bind"/> only maps the
/// properties each options type actually declares, so the nested <c>Models</c>/<c>Ocr</c>/
/// <c>Compliance</c> subsections are silently ignored here, the same way this type's own
/// <see cref="Endpoint"/>/<see cref="ProjectName"/>/<see cref="DocumentIntelligenceConnection"/>/
/// <see cref="AnswerTemperature"/> properties are ignored by those siblings' own <c>Bind</c> calls.
///
/// <see cref="Endpoint"/> unset/blank is how <see cref="ServiceCollectionExtensions
/// .AddAiGatewayModule"/> decides to register <see cref="Fixtures.FixtureAiGateway"/> instead of
/// <see cref="Foundry.FoundryAiGateway"/> (this task's coding objective: "when AiGateway:Endpoint
/// is set register FoundryAiGateway, else FixtureAiGateway") — every property here defaults to
/// "absent", never a placeholder URL/name, so a deployment that forgets to set them fails the same
/// honest way local/CI already does today (fixture path), not with a broken Foundry client
/// pointed at nowhere.
/// </summary>
public sealed class AiGatewayFoundryOptions
{
    /// <summary>Conventional configuration section path for binding this options object — the
    /// root <c>"AiGateway"</c> section itself, not a nested child (see type doc comment).</summary>
    public const string SectionName = "AiGateway";

    /// <summary>
    /// Azure AI services (Cognitive Services multi-service) endpoint that hosts both the
    /// OpenAI-compatible chat/embeddings routes and Document Intelligence on the one shared,
    /// pay-as-you-go account ADR-008 fixes (<c>https://aisvc-contigo.cognitiveservices.azure.com/</c>
    /// per <c>infra/modules/foundry/outputs.tf</c>). <see langword="null"/>/blank means "no live
    /// Foundry endpoint configured" — the fixture path.
    /// </summary>
    public string? Endpoint { get; init; }

    /// <summary>
    /// Per-environment Foundry project name (ADR-008: <c>contigo-dev</c> / <c>contigo-demo</c>).
    /// Threaded through as the <c>x-ms-foundry-project</c> request header on every Foundry HTTP
    /// call (<see cref="Foundry.FoundryHttpJsonClient"/>) for per-project attribution on the
    /// shared AI services account — an informational passthrough this gateway controls, not a
    /// documented Azure REST contract requirement for the underlying Cognitive Services resource.
    /// </summary>
    public string? ProjectName { get; init; }

    /// <summary>
    /// Per-project Document Intelligence connection name (ADR-017: <c>conn-docint-contigo-dev</c>
    /// / <c>-demo</c>). Threaded through as the <c>x-ms-document-intelligence-connection</c>
    /// request header on <see cref="Foundry.FoundryOcrClient"/> calls, the same informational-
    /// passthrough convention as <see cref="ProjectName"/>.
    /// </summary>
    public string? DocumentIntelligenceConnection { get; init; }

    /// <summary>
    /// Ceiling on the `answer` role's sampling temperature (ADR-024: "temperature &lt;= 0.2").
    /// <see cref="Foundry.FoundryAnswerClient"/> clamps to <see langword="this"/> value defensively
    /// — <c>Math.Min(AnswerTemperature, 0.2)</c> — so a misconfigured value above the ADR ceiling
    /// can never raise the actual request's temperature above what the ADR fixes; it can only
    /// lower it. Default 0.2, the ADR's own ceiling.
    /// </summary>
    public double AnswerTemperature { get; init; } = 0.2;
}
