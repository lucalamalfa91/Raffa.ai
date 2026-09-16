namespace Raffa.SharedKernel.Storage;

/// <summary>
/// Builds the tenant-prefixed object storage path every <see cref="IDocumentStorage"/>
/// implementation must use (ADR-009). Centralised here so the path scheme is defined exactly
/// once: no implementation constructs a path by hand, so none can accidentally omit or
/// mis-order the tenant prefix, and two different tenants can never collide on the same path.
/// </summary>
public static class DocumentStoragePath
{
    /// <summary>The prefix every object belonging to <paramref name="tenantId"/> starts with —
    /// the one string <see cref="EnsureWithinTenant"/> checks against (ADR-009).</summary>
    public static string TenantPrefix(TenantId tenantId) => $"{tenantId.Value:D}/";

    /// <summary>
    /// The first-page preview path for one document (task E13/F04/US01/T02, R-DOC-08). Deliberately
    /// not a <c>v{n}</c> document-version path: a preview is a derived rendering, replaced in place
    /// whenever the document is reprocessed, and must never be served as if it were the document.
    /// </summary>
    public static string BuildPreview(TenantId tenantId, EntityId documentId) =>
        $"{TenantPrefix(tenantId)}documents/{documentId.Value:D}/preview/page-1.png";

    /// <summary>
    /// The per-page preview path for one page of one document (task E22/F02/US01/T01, ADR-029
    /// round-3 clause 1). Deterministic from <c>(tenantId, documentId, page)</c> and nothing else
    /// — no render id, no timestamp, no content hash, no attempt counter. Two renders of the same
    /// page of the same document produce the <b>same key</b>; the second overwrites the first
    /// ("replaced in place whenever the document is reprocessed").
    /// <br/>
    /// <paramref name="page"/> is 1-based. Passing <c>page &lt; 1</c> throws
    /// <see cref="ArgumentOutOfRangeException"/> — the same guard <see cref="Build"/> applies for
    /// <c>versionNumber</c> (ADR-009 w17 clause 6): NW-73 reaches this path with no route, no
    /// model binder and no 404, so an endpoint-only bound is not a bound.
    /// </summary>
    public static string BuildPreviewPage(TenantId tenantId, EntityId documentId, int page)
    {
        if (page < 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(page), page, "Page number must be 1 or greater.");
        }

        return $"{TenantPrefix(tenantId)}documents/{documentId.Value:D}/preview/page-{page}.png";
    }

    /// <summary>
    /// Fail-closed guard for the read/delete side of <see cref="IDocumentStorage"/>: a path that
    /// does not start with this tenant's own prefix throws rather than being read, deleted, or
    /// quietly reported as "not found" (ADR-009 — a cross-tenant path is a defect, not a miss).
    /// </summary>
    public static void EnsureWithinTenant(TenantId tenantId, string storagePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(storagePath);

        if (!storagePath.StartsWith(TenantPrefix(tenantId), StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "Refusing to access an object storage path outside the caller's tenant prefix " +
                "(ADR-009: object-storage paths are tenant-prefixed and server-derived).");
        }
    }

    public static string Build(TenantId tenantId, EntityId documentId, int versionNumber, string fileName)
    {
        if (versionNumber < 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(versionNumber), versionNumber, "Version number must be 1 or greater.");
        }

        return $"{TenantPrefix(tenantId)}documents/{documentId.Value:D}/v{versionNumber}/{Sanitize(fileName)}";
    }

    /// <summary>
    /// Strips path-separator characters from the file name component so it can never introduce
    /// extra "virtual directory" segments into the blob path (for example a client-supplied name
    /// containing <c>/</c> or <c>\</c>), and falls back to a generic name when blank. Fixed,
    /// platform-independent rules only — <see cref="Path.GetInvalidFileNameChars"/> is
    /// deliberately not used here, since it varies by host OS and this governs a cloud blob key,
    /// not a real filesystem path.
    /// </summary>
    private static string Sanitize(string fileName)
    {
        var trimmed = (fileName ?? string.Empty).Trim();
        if (trimmed.Length == 0)
        {
            return "file";
        }

        var chars = new char[trimmed.Length];
        for (var i = 0; i < trimmed.Length; i++)
        {
            var c = trimmed[i];
            chars[i] = c is '/' or '\\' ? '_' : c;
        }

        return new string(chars);
    }
}
