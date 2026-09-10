using Raffa.Documents.Contracts.Domain;
using Raffa.Documents.Contracts.Infrastructure;
using Raffa.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;

namespace Raffa.Documents.Contracts.Tests;

/// <summary>
/// Proves the Definition of Done for task E02/F02/US01/T01 (us-01-contract-clause-obligation,
/// AC-1/AC-3): every entity this task's own coding objective names — Contract, ContractLineItem,
/// Clause, Obligation, Risk, CorrectionHistory — exists after a code-first migration with a
/// `tenant_id` column (ADR-009), and the one entity this task actually adds
/// (<see cref="ContractLineItem"/>, product spec §6) persists all of its "minimum V1 fields"
/// through EF Core against a real Postgres instance, not just in the C# model.
///
/// Spins up its own disposable Postgres+pgvector container (Testcontainers), matching
/// <see cref="DocumentsContractsMigrationTests"/>.
/// </summary>
public sealed class ContractLineItemSchemaTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("pgvector/pgvector:pg16")
        .Build();

    public Task InitializeAsync() => _postgres.StartAsync();

    public Task DisposeAsync() => _postgres.DisposeAsync().AsTask();

    /// <summary>us-01's own scope (story text) — not every spec §6 entity, only the ones this
    /// story owns.</summary>
    private static readonly string[] Us01EntityTables =
    [
        "contract",
        "contract_line_item",
        "clause",
        "obligation",
        "risk",
        "correction_history",
    ];

    private DocumentsContractsDbContext CreateContext()
    {
        var optionsBuilder = new DbContextOptionsBuilder<DocumentsContractsDbContext>();
        DocumentsContractsDbContextOptions.Configure(optionsBuilder, _postgres.GetConnectionString());
        return new DocumentsContractsDbContext(optionsBuilder.Options);
    }

    [Fact]
    public async Task Migration_creates_every_us01_entity_table_with_a_tenant_id_column()
    {
        await using var db = CreateContext();
        await db.Database.MigrateAsync();

        await db.Database.OpenConnectionAsync();
        try
        {
            foreach (var table in Us01EntityTables)
            {
                var command = db.Database.GetDbConnection().CreateCommand();
                command.CommandText =
                    "SELECT data_type, is_nullable FROM information_schema.columns " +
                    "WHERE table_schema = 'public' AND table_name = @table AND column_name = 'tenant_id'";
                var parameter = command.CreateParameter();
                parameter.ParameterName = "table";
                parameter.Value = table;
                command.Parameters.Add(parameter);

                await using var reader = await command.ExecuteReaderAsync();
                var hasRow = await reader.ReadAsync();

                Assert.True(
                    hasRow,
                    $"[AC-1/ADR-009] Table \"{table}\" has no tenant_id column after migrating.");
                Assert.Equal("uuid", reader.GetString(0));
                Assert.Equal("NO", reader.GetString(1));
            }
        }
        finally
        {
            await db.Database.CloseConnectionAsync();
        }
    }

    [Fact]
    public async Task ContractLineItem_minimum_v1_fields_round_trip_through_ef_core_with_tenant_id()
    {
        await using (var migrateDb = CreateContext())
        {
            await migrateDb.Database.MigrateAsync();
        }

        var tenantId = TenantId.New();
        var contract = new Contract
        {
            TenantId = tenantId,
            Type = ContractDocumentType.OrderForm,
            Status = "Active",
            Currency = "USD",
            CreatedAt = DateTimeOffset.UtcNow,
        };

        // Spec §6 "ContractLineItem": contract_id, product_id, SKU, description, quantity, unit,
        // unit_price, list_price, discount, billing_period, annual_cost, total_cost.
        var lineItem = new ContractLineItem
        {
            TenantId = tenantId,
            ContractId = contract.Id,
            ProductId = EntityId.New(),
            Sku = "SKU-12345",
            Description = "Enterprise plan — 250 named users",
            Quantity = 250m,
            Unit = "seat",
            UnitPrice = 42.50m,
            ListPrice = 50.00m,
            Discount = 15.0m,
            BillingPeriod = "Annual",
            AnnualCost = 10625.00m,
            TotalCost = 31875.00m,
            CreatedAt = DateTimeOffset.UtcNow,
        };

        await using (var writeDb = CreateContext())
        {
            writeDb.Contracts.Add(contract);
            writeDb.ContractLineItems.Add(lineItem);
            await writeDb.SaveChangesAsync();
        }

        // Fresh context/connection: reads back from Postgres, not the change tracker.
        await using var readDb = CreateContext();
        var stored = await readDb.ContractLineItems.SingleAsync(li => li.Id == lineItem.Id);

        Assert.Equal(tenantId, stored.TenantId);
        Assert.Equal(contract.Id, stored.ContractId);
        Assert.Equal(lineItem.ProductId, stored.ProductId);
        Assert.Equal("SKU-12345", stored.Sku);
        Assert.Equal("Enterprise plan — 250 named users", stored.Description);
        Assert.Equal(250m, stored.Quantity);
        Assert.Equal("seat", stored.Unit);
        Assert.Equal(42.50m, stored.UnitPrice);
        Assert.Equal(50.00m, stored.ListPrice);
        Assert.Equal(15.0m, stored.Discount);
        Assert.Equal("Annual", stored.BillingPeriod);
        Assert.Equal(10625.00m, stored.AnnualCost);
        Assert.Equal(31875.00m, stored.TotalCost);
    }

    [Fact]
    public async Task Deleting_the_contract_cascades_to_its_line_items()
    {
        await using (var migrateDb = CreateContext())
        {
            await migrateDb.Database.MigrateAsync();
        }

        var tenantId = TenantId.New();
        var contract = new Contract
        {
            TenantId = tenantId,
            Type = ContractDocumentType.Msa,
            Status = "Active",
            Currency = "USD",
            CreatedAt = DateTimeOffset.UtcNow,
        };
        var lineItem = new ContractLineItem
        {
            TenantId = tenantId,
            ContractId = contract.Id,
            Description = "line item that must not outlive its contract",
            CreatedAt = DateTimeOffset.UtcNow,
        };

        await using (var writeDb = CreateContext())
        {
            writeDb.Contracts.Add(contract);
            writeDb.ContractLineItems.Add(lineItem);
            await writeDb.SaveChangesAsync();
        }

        await using (var deleteDb = CreateContext())
        {
            var toDelete = await deleteDb.Contracts.SingleAsync(c => c.Id == contract.Id);
            deleteDb.Contracts.Remove(toDelete);
            await deleteDb.SaveChangesAsync();
        }

        await using var readDb = CreateContext();
        Assert.False(await readDb.ContractLineItems.AnyAsync(li => li.Id == lineItem.Id));
    }
}
