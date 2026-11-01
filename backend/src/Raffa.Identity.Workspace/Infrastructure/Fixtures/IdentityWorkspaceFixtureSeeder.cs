using Microsoft.Extensions.DependencyInjection;
using Raffa.Identity.Workspace.Domain;
using Raffa.SharedKernel;
using Raffa.SharedKernel.Fixtures;
using Raffa.SharedKernel.Tenancy;

namespace Raffa.Identity.Workspace.Infrastructure.Fixtures;

/// <summary>
/// AskRaffa 4.0 fixture seeder (fixture mode only): the workspace "Helvetia Foods AG" with its
/// five default roles and the two members of the prototype — Luca Lamalfa (Head of Procurement,
/// the approver: Admin + Procurement) and Marta Keller (Buyer: Procurement). Each member is linked
/// to the external subject the <c>Fixture</c> authentication scheme puts in the <c>oid</c> claim,
/// so the API resolves the caller exactly as it does for a signed-in user. Same in both scenarios.
/// </summary>
public sealed class IdentityWorkspaceFixtureSeeder(IdentityWorkspaceDbContext dbContext, ITenantContext tenantContext)
    : IFixtureScenarioSeeder
{
    public int Order => FixtureSeedOrder.IdentityWorkspace;

    public Task WipeAsync(FixtureSeedContext context, CancellationToken cancellationToken) => Task.CompletedTask;

    public async Task SeedAsync(FixtureSeedContext context, string scenario, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        using var _ = tenantContext.BeginScope(context.TenantId);

        dbContext.Workspaces.Add(new WorkspaceTenant
        {
            Id = new EntityId(context.TenantId.Value),
            TenantId = context.TenantId,
            Name = context.TenantName,
            CreatedAt = context.Now,
            Industry = "Food & beverage",
            Country = "CH",
            Currency = "CHF",
        });

        var roles = Enum.GetValues<WorkspaceRoleName>().ToDictionary(
            name => name,
            name => new WorkspaceRole
            {
                Id = context.Id("workspace-role", name),
                TenantId = context.TenantId,
                Name = name,
                CreatedAt = context.Now,
            });
        dbContext.WorkspaceRoles.AddRange(roles.Values);

        foreach (var member in context.Users)
        {
            var user = new WorkspaceUser
            {
                Id = context.Id("workspace-user", member.Key),
                TenantId = context.TenantId,
                Email = member.Email,
                DisplayName = member.DisplayName,
                ExternalSubjectId = member.ExternalSubjectId,
                CreatedAt = context.Now,
            };
            dbContext.WorkspaceUsers.Add(user);

            var roleNames = member.Role == "admin"
                ? new[] { WorkspaceRoleName.Admin, WorkspaceRoleName.Procurement }
                : new[] { WorkspaceRoleName.Procurement };
            foreach (var roleName in roleNames)
            {
                dbContext.WorkspaceMemberships.Add(new WorkspaceMembership
                {
                    Id = context.Id("workspace-membership", $"{member.Key}:{roleName}"),
                    TenantId = context.TenantId,
                    WorkspaceUserId = user.Id,
                    WorkspaceRoleId = roles[roleName].Id,
                    CreatedAt = context.Now,
                });
            }
        }

        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }
}

public static class IdentityWorkspaceFixtureServiceCollectionExtensions
{
    /// <summary>Registers the workspace fixture seeder (call only when fixtures are enabled).</summary>
    public static IServiceCollection AddIdentityWorkspaceFixtureSeeder(this IServiceCollection services)
    {
        services.AddScoped<IFixtureScenarioSeeder, IdentityWorkspaceFixtureSeeder>();
        return services;
    }
}
