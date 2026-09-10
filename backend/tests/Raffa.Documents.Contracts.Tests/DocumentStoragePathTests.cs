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
}
