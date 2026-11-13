using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Raffa.Renewals.Application.Cases;
using Raffa.Renewals.Domain.Cases;
using Raffa.SharedKernel;
using Raffa.SharedKernel.Contracts;
using Raffa.SharedKernel.Text;

namespace Raffa.Renewals.Application.CaseWork;

/// <summary>A file received by a case: its name, type and bytes.</summary>
public sealed record CaseUpload(string FileName, string MimeType, byte[] Bytes);

/// <summary>A supplier email uploaded as a file (<c>.eml</c>/<c>.msg</c>): the file itself, the text Raffa
/// reads as the reply and the attachments it carried.</summary>
public sealed record CaseEmailUpload(CaseUpload Source, string Text, IReadOnlyList<CaseUpload> Attachments);

/// <summary>One row of <c>GET /api/renewals/{id}/files</c>.</summary>
public sealed record CaseFileItem(
    Guid Id,
    string Kind,
    string FileName,
    string MimeType,
    long SizeBytes,
    DateTimeOffset CreatedAt,
    string UploadedBy,
    int? DraftVersion,
    string? Vendor,
    int? ReplyIndex,
    Guid? SourceFileId,
    Guid? DocumentId);

/// <summary>The kinds of <see cref="CaseFileRecord"/>.</summary>
public static class CaseFileKinds
{
    public const string Draft = "draft";
    public const string Signed = "signed";
    public const string Offer = "offer";
    public const string Email = "email";
    public const string Attachment = "attachment";
}

/// <summary>
/// Case files (plan F1, <c>docs/architecture/renewal-in-app-mail-plan.md</c>): every file a case accepts
/// is kept in object storage and listed in <c>case_file</c>, so drafts, the signed contract, offers and
/// uploaded supplier emails (with their attachments) stay downloadable from the case; an attachment can be
/// used as a draft, the signed contract or an offer without leaving Raffa; and when the case closes as
/// renewed the signed contract becomes a document of the contract in Documents.
/// A file is stored only when the engine actually recorded it (a draft sent before the terms were agreed
/// is not kept), in the same unit of work as the case state.
/// </summary>
public sealed partial class RenewalCaseService
{
    private CaseUpload? pendingUpload;
    private IReadOnlyList<CaseUpload> pendingAttachments = [];
    private Guid? pendingSourceFileId;
    private readonly List<(CaseFileRecord Record, byte[] Bytes)> stagedFiles = [];

    /// <summary><see cref="ActWithDocumentAsync"/>, keeping the uploaded bytes as a case file.</summary>
    public Task<CaseResult> ActWithUploadAsync(
        TenantId tenantId, string? callerIdentity, Guid caseId, string type, CaseUpload upload, string text, string? vendor, int? version, CancellationToken ct = default)
    {
        pendingUpload = upload;
        return ActWithDocumentAsync(tenantId, callerIdentity, caseId, type, upload.FileName, text, vendor, version, ct);
    }

    /// <summary>
    /// <c>POST /api/renewals/{id}/vendor-messages/file</c>: a supplier email uploaded as a file is
    /// registered as the supplier's reply (the same step as a pasted reply); the email and its attachments
    /// are kept as case files.
    /// </summary>
    public Task<CaseResult> ReplyWithEmailAsync(
        TenantId tenantId, string? callerIdentity, Guid caseId, CaseEmailUpload email, int? version, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(email.Text))
        {
            return Task.FromResult(CaseResult.Bad("The email has no readable text."));
        }

        pendingUpload = email.Source;
        pendingAttachments = email.Attachments;
        return ActAsync(tenantId, callerIdentity, caseId, "reply", new CaseActionPayload { Text = CaseText.Of(email.Text) }, version, ct);
    }

    /// <summary>
    /// <c>POST /api/renewals/{id}/files/{fileId}/use</c>: a file already in the case (typically an email
    /// attachment) used as a contract draft / the signed contract (<c>addDoc</c>) or an offer
    /// (<c>altOffer</c>). <paramref name="readText"/> extracts its text (the host's parser).
    /// </summary>
    public async Task<CaseResult> UseFileAsync(
        TenantId tenantId,
        string? callerIdentity,
        Guid caseId,
        Guid fileId,
        string type,
        string? vendor,
        int? version,
        Func<string, string, byte[], CancellationToken, Task<string>> readText,
        CancellationToken ct = default)
    {
        if (type is not ("addDoc" or "altOffer"))
        {
            return CaseResult.Bad("A case file can be used as a contract document (addDoc) or an offer (altOffer).");
        }

        var file = await DownloadFileAsync(tenantId, caseId, fileId, ct).ConfigureAwait(false);
        if (file is null)
        {
            return CaseResult.NotFound("No such file in this case.");
        }

        var (record, bytes) = file.Value;
        var text = await readText(record.FileName, record.MimeType, bytes, ct).ConfigureAwait(false);
        pendingSourceFileId = record.Id.Value;
        return await ActWithUploadAsync(
            tenantId, callerIdentity, caseId, type, new CaseUpload(record.FileName, record.MimeType, bytes), text, vendor, version, ct).ConfigureAwait(false);
    }

    /// <summary><c>GET /api/renewals/{id}/files</c>: the case's files, oldest first; null when the case does not exist.</summary>
    public async Task<IReadOnlyList<CaseFileItem>?> FilesAsync(TenantId tenantId, Guid caseId, CancellationToken ct = default)
    {
        using var scope = tenantContext.BeginScope(tenantId);
        var id = new EntityId(caseId);
        if (!await db.RenewalCases.AnyAsync(x => x.Id == id, ct).ConfigureAwait(false))
        {
            return null;
        }

        var rows = await db.CaseFiles.AsNoTracking().Where(x => x.CaseId == id).ToListAsync(ct).ConfigureAwait(false);
        return rows
            .OrderBy(x => x.CreatedAt)
            .ThenBy(x => x.Kind == CaseFileKinds.Attachment ? 1 : 0)
            .Select(x => new CaseFileItem(
                x.Id.Value, x.Kind, x.FileName, x.MimeType, x.SizeBytes, x.CreatedAt, x.UploadedBy,
                x.DraftVersion, x.Vendor, x.ReplyIndex, x.SourceFileId, x.DocumentId))
            .ToList();
    }

    /// <summary><c>GET /api/renewals/{id}/files/{fileId}</c>: the file and its bytes; null when either is missing.</summary>
    public async Task<(CaseFileRecord Record, byte[] Bytes)?> DownloadFileAsync(TenantId tenantId, Guid caseId, Guid fileId, CancellationToken ct = default)
    {
        if (caseFileStorage is null)
        {
            return null;
        }

        using var scope = tenantContext.BeginScope(tenantId);
        var record = await db.CaseFiles.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == new EntityId(fileId) && x.CaseId == new EntityId(caseId), ct)
            .ConfigureAwait(false);
        if (record is null)
        {
            return null;
        }

        var bytes = await caseFileStorage.LoadAsync(tenantId, record.StoragePath, ct).ConfigureAwait(false);
        return bytes is null ? null : (record, bytes);
    }

    /// <summary>
    /// After one engine step: stages the pending upload when the step recorded it — a new draft, the
    /// signed contract, an offer, or the reply an uploaded email was registered as. A step that refused
    /// the document leaves nothing staged.
    /// </summary>
    private void StageUploadedFiles(TenantId tenantId, RenewalCaseRecord record, string type, CaseActionPayload p, RenewalCase prev, RenewalCase next, string by)
    {
        if (pendingUpload is not { } upload || caseFileStorage is null)
        {
            return;
        }

        string? kind = null;
        int? draftVersion = null;
        int? replyIndex = null;
        string? vendor = null;
        switch (type)
        {
            case "addDoc" when next.Docs.Count > prev.Docs.Count:
                kind = CaseFileKinds.Draft;
                draftVersion = next.Docs[^1].V;
                break;
            case "addDoc" when next.Signed is not null && next.Phase == CasePhases.SignConfirm:
                kind = CaseFileKinds.Signed;
                break;
            case "altOffer" when p.Vendor is { Length: > 0 } v && next.Alt?.Offers.ContainsKey(v) == true:
                kind = CaseFileKinds.Offer;
                vendor = v;
                break;
            case "reply" when next.Reps.Count > prev.Reps.Count:
                kind = CaseFileKinds.Email;
                replyIndex = next.Reps[^1].Rid;
                break;
        }

        if (kind is null)
        {
            return;
        }

        var now = clock.UtcNow;
        var main = NewFile(tenantId, record, kind, upload, by, now);
        main.DraftVersion = draftVersion;
        main.Vendor = vendor;
        main.ReplyIndex = replyIndex;
        main.SourceFileId = pendingSourceFileId;
        stagedFiles.Add((main, upload.Bytes));
        if (kind == CaseFileKinds.Email)
        {
            foreach (var attachment in pendingAttachments.Where(a => a.Bytes.Length > 0))
            {
                var row = NewFile(tenantId, record, CaseFileKinds.Attachment, attachment, by, now);
                row.ReplyIndex = replyIndex;
                row.SourceFileId = main.Id.Value;
                stagedFiles.Add((row, attachment.Bytes));
            }
        }

        pendingUpload = null;
        pendingAttachments = [];
        pendingSourceFileId = null;
    }

    private static CaseFileRecord NewFile(TenantId tenantId, RenewalCaseRecord record, string kind, CaseUpload upload, string by, DateTimeOffset now) => new()
    {
        TenantId = tenantId,
        CaseId = record.Id,
        Kind = kind,
        FileName = Truncate(string.IsNullOrWhiteSpace(upload.FileName) ? "file" : Path.GetFileName(upload.FileName), 300),
        MimeType = Truncate(string.IsNullOrWhiteSpace(upload.MimeType) ? "application/octet-stream" : upload.MimeType, 200),
        SizeBytes = upload.Bytes.LongLength,
        Checksum = Convert.ToHexString(SHA256.HashData(upload.Bytes)),
        StoragePath = string.Empty,
        UploadedBy = Truncate(by, 200),
        CreatedAt = now,
    };

    private static string Truncate(string s, int max) => s.Length <= max ? s : s[..max];

    /// <summary>Writes the staged bytes and adds their rows (saved with the case state). Returns the
    /// written paths, so a failed save can remove them.</summary>
    private async Task<List<string>> PersistStagedFilesAsync(TenantId tenantId, CancellationToken ct)
    {
        var written = new List<string>();
        if (caseFileStorage is null)
        {
            return written;
        }

        foreach (var (row, bytes) in stagedFiles)
        {
            using var content = new MemoryStream(bytes, writable: false);
            row.StoragePath = await caseFileStorage.SaveAsync(tenantId, row.Id, 1, row.FileName, content, ct).ConfigureAwait(false);
            written.Add(row.StoragePath);
            db.CaseFiles.Add(row);
        }

        stagedFiles.Clear();
        return written;
    }

    private async Task DeleteStoredAsync(TenantId tenantId, IEnumerable<string> paths)
    {
        if (caseFileStorage is null)
        {
            return;
        }

        foreach (var path in paths)
        {
            try
            {
                await caseFileStorage.DeleteAsync(tenantId, path, CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                if (logger is not null)
                {
                    Microsoft.Extensions.Logging.LoggerExtensions.LogWarning(logger, ex, "A case file could not be removed from storage.");
                }
            }
        }
    }

    /// <summary>
    /// When the case closes as renewed: the last signed contract the case received becomes a document of
    /// the contract (Documents). Best effort — a failure leaves the renewed terms in place and is logged.
    /// </summary>
    private async Task<Guid?> AttachSignedToDocumentsAsync(TenantId tenantId, RenewalCaseRecord record, RenewalCase c, CancellationToken ct)
    {
        if (documentAttacher is null || caseFileStorage is null)
        {
            return null;
        }

        try
        {
            var signed = await db.CaseFiles
                .Where(x => x.CaseId == record.Id && x.Kind == CaseFileKinds.Signed)
                .OrderByDescending(x => x.CreatedAt)
                .FirstOrDefaultAsync(ct)
                .ConfigureAwait(false);
            if (signed is null)
            {
                return null;
            }

            var bytes = await caseFileStorage.LoadAsync(tenantId, signed.StoragePath, ct).ConfigureAwait(false);
            if (bytes is null || bytes.Length == 0)
            {
                return null;
            }

            var documentId = await documentAttacher.AttachSignedAsync(
                tenantId,
                new SignedContractDocument(record.ContractId.Value, signed.FileName, signed.MimeType, bytes, c.Signed?.ConfirmedBy ?? c.Owner, record.Id.Value),
                ct).ConfigureAwait(false);
            signed.DocumentId = documentId;
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
            return documentId;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            if (logger is not null)
            {
                Microsoft.Extensions.Logging.LoggerExtensions.LogWarning(logger, ex, "The signed contract of a renewed case could not be filed in Documents.");
            }

            return null;
        }
    }

    /// <summary>Removes the files of these cases (rows now, bytes best effort).</summary>
    private async Task<List<string>> RemoveCaseFilesAsync(IReadOnlyCollection<EntityId> caseIds, CancellationToken ct)
    {
        var files = await db.CaseFiles.Where(x => caseIds.Contains(x.CaseId)).ToListAsync(ct).ConfigureAwait(false);
        db.CaseFiles.RemoveRange(files);
        return files.Select(f => f.StoragePath).Where(p => p.Length > 0).ToList();
    }
}
