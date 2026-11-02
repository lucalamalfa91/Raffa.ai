using Raffa.AiFlows.Shared.Parsing;
using Raffa.Api.Infrastructure;
using Raffa.Documents.Contracts.Application.Admission;
using Raffa.Renewals.Application.CaseWork;
using Raffa.SharedKernel.Renewals;
using System.Text;

namespace Raffa.Api;

/// <summary>
/// Company rules (Impostazioni): <c>GET/PUT /api/company-rules</c>, the interview that refines them
/// (<c>POST /api/company-rules/interview</c>, stateless: the client sends the conversation) and the
/// policy upload (<c>POST /api/company-rules/policy</c>: Raffa reads the document and proposes the rules;
/// nothing is saved until the user saves). Any member reads; only a workspace Admin writes (403 otherwise).
/// </summary>
public static class CompanyRulesEndpointExtensions
{
    public sealed record CompanyRulesBody(CompanyRules Rules, bool CanEdit);

    public sealed record SaveRulesRequest(string Text, int? StartWindowDays, string? SourceFile);

    public sealed record InterviewRequest(IReadOnlyList<RuleTurn>? Turns, string? CurrentText, string? Lang);

    public sealed record PolicyProposal(string FileName, string Text, int? StartWindowDays, string Summary);

    private const long MaxPolicyBytes = 10L * 1024 * 1024;

    private static readonly string[] PlainTextExtensions = [".txt", ".md"];

    public static IEndpointRouteBuilder MapCompanyRulesEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/company-rules", GetAsync);
        endpoints.MapPut("/api/company-rules", PutAsync);
        endpoints.MapPost("/api/company-rules/interview", InterviewAsync);
        endpoints.MapPost("/api/company-rules/policy", PolicyAsync);
        return endpoints;
    }

    private static async Task<IResult> WithCaller(
        HttpRequest request, ICallerContext callerContext, CancellationToken ct, Func<CallerTenantResult, Task<IResult>> run)
    {
        var caller = await callerContext.ResolveTenantAsync(request, ct).ConfigureAwait(false);
        if (caller.Failure is not null)
        {
            return caller.Failure;
        }

        using var _ = caller.Scope;
        return await run(caller).ConfigureAwait(false);
    }

    private static string Lang(string? lang) => string.Equals(lang, "it", StringComparison.OrdinalIgnoreCase) ? "it" : "en";

    private static Task<IResult> GetAsync(HttpRequest request, CompanyRulesService service, ICallerContext callerContext, CancellationToken ct) =>
        WithCaller(request, callerContext, ct, async caller => Results.Ok(new CompanyRulesBody(
            await service.GetAsync(caller.TenantId, ct).ConfigureAwait(false),
            await service.CanEditAsync(caller.TenantId, caller.Identity, ct).ConfigureAwait(false))));

    private static Task<IResult> PutAsync(SaveRulesRequest body, HttpRequest request, CompanyRulesService service, ICallerContext callerContext, CancellationToken ct) =>
        WithCaller(request, callerContext, ct, async caller =>
        {
            if (!await service.CanEditAsync(caller.TenantId, caller.Identity, ct).ConfigureAwait(false))
            {
                return Results.Json(new { code = "forbidden", error = "Only the Head of Procurement can change the company rules." }, statusCode: StatusCodes.Status403Forbidden);
            }

            var saved = await service.SaveAsync(caller.TenantId, caller.Identity ?? "—", body.Text, body.StartWindowDays, body.SourceFile, ct).ConfigureAwait(false);
            return saved is null
                ? Results.BadRequest(new { code = "too_long", error = $"The rules are limited to {CompanyRulesDefaults.MaxTextLength} characters." })
                : Results.Ok(new CompanyRulesBody(saved, true));
        });

    private static Task<IResult> InterviewAsync(InterviewRequest body, HttpRequest request, CompanyRulesService service, ICallerContext callerContext, CancellationToken ct) =>
        WithCaller(request, callerContext, ct, async caller =>
        {
            if (!await service.CanEditAsync(caller.TenantId, caller.Identity, ct).ConfigureAwait(false))
            {
                return Results.Json(new { code = "forbidden", error = "Only the Head of Procurement can change the company rules." }, statusCode: StatusCodes.Status403Forbidden);
            }

            return Results.Ok(await service.InterviewAsync(caller.TenantId, body.Turns ?? [], body.CurrentText, Lang(body.Lang), ct).ConfigureAwait(false));
        });

    private static Task<IResult> PolicyAsync(
        HttpRequest request, CompanyRulesService service, HybridDocumentParsingService parser, DocumentAdmissionOptions options,
        ICallerContext callerContext, CancellationToken ct) =>
        WithCaller(request, callerContext, ct, async caller =>
        {
            if (!await service.CanEditAsync(caller.TenantId, caller.Identity, ct).ConfigureAwait(false))
            {
                return Results.Json(new { code = "forbidden", error = "Only the Head of Procurement can change the company rules." }, statusCode: StatusCodes.Status403Forbidden);
            }

            var (form, file, refusal) = await DocumentsEndpointExtensions.ReadUploadFormAsync(request, options, ct).ConfigureAwait(false);
            if (refusal is not null)
            {
                return refusal;
            }

            if (file!.Length > MaxPolicyBytes)
            {
                return Results.Json("Raffa reads policies up to 10 MB.", statusCode: StatusCodes.Status413PayloadTooLarge);
            }

            var fileName = Path.GetFileName(file.FileName);
            var lang = Lang(form!["lang"].ToString());
            string text;
            if (PlainTextExtensions.Contains(Path.GetExtension(fileName), StringComparer.OrdinalIgnoreCase))
            {
                await using var stream = file.OpenReadStream();
                using var reader = new StreamReader(stream, Encoding.UTF8);
                text = await reader.ReadToEndAsync(ct).ConfigureAwait(false);
            }
            else
            {
                var (bytes, format, bytesRefusal) = await DocumentsEndpointExtensions.ReadUploadBytesAsync(file, options, ct).ConfigureAwait(false);
                if (bytesRefusal is not null)
                {
                    return bytesRefusal;
                }

                var parsed = await parser.ParseAsync(fileName, format!.MimeType, bytes!, ct).ConfigureAwait(false);
                if (parsed.IsFailure)
                {
                    return Results.Json(new { code = "unreadable", error = "Raffa could not read this document." }, statusCode: StatusCodes.Status422UnprocessableEntity);
                }

                text = string.Join("\n\n", parsed.Value.Select(p => p.Text));
            }

            if (string.IsNullOrWhiteSpace(text))
            {
                return Results.Json(new { code = "no_text", error = "The document has no readable text." }, statusCode: StatusCodes.Status422UnprocessableEntity);
            }

            var d = await service.DistillAsync(caller.TenantId, fileName, text, lang, ct).ConfigureAwait(false);
            return Results.Ok(new PolicyProposal(fileName, d.Text, d.StartWindowDays, d.Summary));
        });
}
