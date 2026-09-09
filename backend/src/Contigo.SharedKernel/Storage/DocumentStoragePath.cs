namespace Contigo.SharedKernel.Storage;

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
