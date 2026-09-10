using Raffa.Identity.Workspace.Domain;
using Raffa.SharedKernel;

namespace Raffa.Identity.Workspace.Tests;

/// <summary>
/// Pure unit proof for task E01/F05/US01/T02 of the first-sign-in linking decision
/// (<see cref="WorkspaceUser"/>'s own doc comment: "Resolving [ExternalSubjectId] from the OIDC
/// sub/oid claim on first sign-in ... [is] task E01/F05/US01/T02's job"). No database.
/// </summary>
public sealed class WorkspaceSignInTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 3, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Returns_the_existing_row_unchanged_when_already_linked_to_this_subject()
    {
        var user = new WorkspaceUser
        {
            TenantId = TenantId.New(),
            Email = "a@acme.example",
            ExternalSubjectId = "sub-1",
            CreatedAt = Now,
        };

        var result = WorkspaceSignIn.ResolveSignedInUser(
            existingByExternalSubject: user, existingByEmail: null, externalSubjectId: "sub-1");

        Assert.True(result.IsSuccess);
        Assert.Same(user, result.Value);
    }

    [Fact]
    public void Links_the_external_subject_onto_an_invited_but_never_signed_in_user()
    {
        var invited = new WorkspaceUser
        {
            TenantId = TenantId.New(),
            Email = "a@acme.example",
            ExternalSubjectId = null,
            CreatedAt = Now,
        };

        var result = WorkspaceSignIn.ResolveSignedInUser(
            existingByExternalSubject: null, existingByEmail: invited, externalSubjectId: "sub-1");

        Assert.True(result.IsSuccess);
        Assert.Equal("sub-1", result.Value.ExternalSubjectId);
        Assert.Same(invited, result.Value);
    }

    [Fact]
    public void Fails_when_neither_subject_nor_email_is_recognized()
    {
        var result = WorkspaceSignIn.ResolveSignedInUser(
            existingByExternalSubject: null, existingByEmail: null, externalSubjectId: "sub-1");

        Assert.True(result.IsFailure);
    }

    [Fact]
    public void Linking_the_same_subject_twice_is_idempotent()
    {
        var user = new WorkspaceUser
        {
            TenantId = TenantId.New(),
            Email = "a@acme.example",
            ExternalSubjectId = "sub-1",
            CreatedAt = Now,
        };

        var result = user.LinkExternalSubject("sub-1");

        Assert.True(result.IsSuccess);
        Assert.Equal("sub-1", result.Value.ExternalSubjectId);
    }

    [Fact]
    public void Refuses_to_relink_an_already_linked_user_to_a_different_subject()
    {
        var user = new WorkspaceUser
        {
            TenantId = TenantId.New(),
            Email = "a@acme.example",
            ExternalSubjectId = "sub-1",
            CreatedAt = Now,
        };

        var result = user.LinkExternalSubject("sub-2");

        Assert.True(result.IsFailure);
        Assert.Equal("sub-1", user.ExternalSubjectId); // unchanged.
    }

    [Fact]
    public void Refuses_to_link_a_blank_subject_id()
    {
        var user = new WorkspaceUser { TenantId = TenantId.New(), Email = "a@acme.example", CreatedAt = Now };

        var result = user.LinkExternalSubject("   ");

        Assert.True(result.IsFailure);
    }
}
