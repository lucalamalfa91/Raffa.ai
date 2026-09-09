namespace Contigo.SharedKernel.Storage;

/// <summary>
/// Tenant-scoped binary object storage for uploaded documents (ADR-005 "Object storage" row,
/// ADR-009, ADR-011). Domain modules depend on this interface only — the concrete adapter
/// (Azure Blob Storage in deployed environments) is wired by the host composition root, so no
/// domain module ever references a cloud storage SDK directly (ADR-002: domain modules must not
/// reference a provider SDK).
///
/// Every implementation MUST derive the stored path from <see cref="DocumentStoragePath"/> (or
/// an equivalent tenant-prefixing scheme) — the path is never accepted from a caller as a raw
/// string. ADR-009: "Object-storage paths must be tenant-prefixed ... issued through a
/// server-side path governed by the same tenant claim, never a client-supplied raw blob URL."
///
/// <para>
/// <see cref="LoadAsync"/> and <see cref="DeleteAsync"/> (task E13/F04/US01/T02) do take a path,
/// because the caller reads it back off the <c>document</c>/<c>document_version</c> row it just
/// queried under RLS — never off a request. They are still fail-closed: every implementation MUST
/// call <see cref="DocumentStoragePath.EnsureWithinTenant"/> first, so a row whose path somehow
/// names another tenant's prefix throws instead of crossing the boundary.
/// </para>
/// </summary>
public interface IDocumentStorage
{
    /// <summary>
    /// Persists <paramref name="content"/> under a tenant-prefixed path derived from
    /// <paramref name="tenantId"/>, <paramref name="documentId"/>, <paramref name="versionNumber"/>
    /// and <paramref name="fileName"/>, and returns the path that was actually written to.
    /// </summary>
    Task<string> SaveAsync(
        TenantId tenantId,
        EntityId documentId,
        int versionNumber,
        string fileName,
        Stream content,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Reads back the bytes previously written at <paramref name="storagePath"/>, or
    /// <see langword="null"/> when nothing is stored there (a document whose blob was deleted out
    /// from under the row, or a path from a database restored without its container).
    /// Throws when <paramref name="storagePath"/> is outside <paramref name="tenantId"/>'s own
    /// prefix (ADR-009 — see <see cref="DocumentStoragePath.EnsureWithinTenant"/>).
    /// </summary>
    Task<byte[]?> LoadAsync(
        TenantId tenantId,
        string storagePath,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes the object at <paramref name="storagePath"/>; a path that holds nothing is not an
    /// error (deletion is idempotent — R-DOC-10 must succeed even when a previous attempt removed
    /// the blob but failed before the rows). Same tenant-prefix rule as <see cref="LoadAsync"/>.
    /// </summary>
    Task DeleteAsync(
        TenantId tenantId,
        string storagePath,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Persists the rendered first-page preview for <paramref name="documentId"/> and returns its
    /// tenant-prefixed path (<see cref="DocumentStoragePath.BuildPreview"/>). Separate from
    /// <see cref="SaveAsync"/> so the preview is never mistaken for a document version and so the
    /// path stays server-derived (R-DOC-08: "never a raw blob URL").
    /// </summary>
    Task<string> SavePreviewAsync(
        TenantId tenantId,
        EntityId documentId,
        Stream content,
        CancellationToken cancellationToken = default);
}
