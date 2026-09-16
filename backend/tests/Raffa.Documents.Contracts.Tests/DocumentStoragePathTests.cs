using Raffa.SharedKernel;
using Raffa.SharedKernel.Storage;

namespace Raffa.Documents.Contracts.Tests;

/// <summary>
/// Pure unit proof (no database, no storage) for task E01/F06/US01/T01 (us-01-document-upload,
/// AC-1 "no cross-tenant path"): <see cref="DocumentStoragePath.Build"/> always prefixes with
/// the tenant id, so two tenants uploading a file with the identical name/document id can never
/// collide on the same path, and a file name cannot inject extra path segments.
/// </summary>
public sealed class DocumentStoragePathTests
{
    [Fact]
    public void Path_is_prefixed_with_the_tenant_id()
    {
        var tenantId = TenantId.New();
        var documentId = EntityId.New();

        var path = DocumentStoragePath.Build(tenantId, documentId, versionNumber: 1, "contract.pdf");

        Assert.StartsWith($"{tenantId.Value:D}/", path, StringComparison.Ordinal);
    }

    [Fact]
    public void Two_tenants_uploading_the_same_document_id_and_file_name_never_collide()
    {
        // Same document id and file name deliberately reused across tenants: the only thing
        // that can keep the paths apart is the tenant prefix itself.
        var documentId = EntityId.New();
        var tenantA = TenantId.New();
        var tenantB = TenantId.New();

        var pathA = DocumentStoragePath.Build(tenantA, documentId, versionNumber: 1, "contract.pdf");
        var pathB = DocumentStoragePath.Build(tenantB, documentId, versionNumber: 1, "contract.pdf");

        Assert.NotEqual(pathA, pathB);
    }

    [Fact]
    public void File_name_path_separators_cannot_inject_extra_segments()
    {
        var tenantId = TenantId.New();
        var documentId = EntityId.New();

        var path = DocumentStoragePath.Build(tenantId, documentId, versionNumber: 1, "../../etc/passwd");

        // The tenant/documents/document-id/version prefix is always exactly these 4 "/"
        // delimiters before the (sanitised) file name segment — a malicious name cannot add
        // more of them and so can never walk the path outside its own tenant/document prefix.
        var expectedPrefix = $"{tenantId.Value:D}/documents/{documentId.Value:D}/v1/";
        Assert.StartsWith(expectedPrefix, path, StringComparison.Ordinal);
        Assert.DoesNotContain('/', path[expectedPrefix.Length..]);
    }

    [Fact]
    public void Blank_file_name_falls_back_to_a_generic_name()
    {
        var path = DocumentStoragePath.Build(TenantId.New(), EntityId.New(), versionNumber: 1, "   ");

        Assert.EndsWith("/file", path, StringComparison.Ordinal);
    }

    [Fact]
    public void Rejects_a_version_number_below_one()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => DocumentStoragePath.Build(TenantId.New(), EntityId.New(), versionNumber: 0, "contract.pdf"));
    }

    // ---- Task E22/F02/US01/T01 (ADR-029 round-3 clause 1) ----

    [Fact]
    public void BuildPreviewPage_is_deterministic_for_the_same_tenant_document_page()
    {
        var tenantId   = TenantId.New();
        var documentId = EntityId.New();

        var path1 = DocumentStoragePath.BuildPreviewPage(tenantId, documentId, 1);
        var path2 = DocumentStoragePath.BuildPreviewPage(tenantId, documentId, 1);

        Assert.Equal(path1, path2);
    }

    [Fact]
    public void BuildPreviewPage_produces_distinct_paths_for_different_pages()
    {
        var tenantId   = TenantId.New();
        var documentId = EntityId.New();

        var page1 = DocumentStoragePath.BuildPreviewPage(tenantId, documentId, 1);
        var page2 = DocumentStoragePath.BuildPreviewPage(tenantId, documentId, 2);

        Assert.NotEqual(page1, page2);
    }

    [Fact]
    public void BuildPreviewPage_for_page_1_produces_same_path_as_BuildPreview()
    {
        // ADR-029 round-3 clause 1: BuildPreview is unchanged and stays the page-1 path.
        var tenantId   = TenantId.New();
        var documentId = EntityId.New();

        var previewPage1 = DocumentStoragePath.BuildPreviewPage(tenantId, documentId, 1);
        var buildPreview = DocumentStoragePath.BuildPreview(tenantId, documentId);

        Assert.Equal(buildPreview, previewPage1);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void BuildPreviewPage_rejects_page_numbers_below_1(int invalidPage)
    {
        // ADR-009 w17 clause 6: page < 1 throws; an endpoint-only bound is not a bound.
        Assert.Throws<ArgumentOutOfRangeException>(
            () => DocumentStoragePath.BuildPreviewPage(TenantId.New(), EntityId.New(), invalidPage));
    }

    [Fact]
    public void BuildPreviewPage_path_is_under_tenant_prefix_and_guarded_by_EnsureWithinTenant()
    {
        var tenantA = TenantId.New();
        var tenantB = TenantId.New();
        var documentId = EntityId.New();

        var path = DocumentStoragePath.BuildPreviewPage(tenantA, documentId, 3);

        Assert.StartsWith($"{tenantA.Value:D}/", path, StringComparison.Ordinal);

        // The guard accepts this tenant's own path...
        DocumentStoragePath.EnsureWithinTenant(tenantA, path);

        // ...and throws for any other tenant (ADR-009: cross-tenant path is a defect, not a miss).
        Assert.Throws<InvalidOperationException>(() => DocumentStoragePath.EnsureWithinTenant(tenantB, path));
    }
}
