using ERPAccounting.Api.Accounting;
using ERPAccounting.Api.Contracts;
using ERPAccounting.Api.Domain;
using ERPAccounting.Api.Infrastructure;
using ERPAccounting.Api.SalesInventory;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace ERPAccounting.Api.Tests;

public sealed class SalesInventoryIdempotencyTests
{
    [Fact]
    public async Task ReplayedStoreCreateRequests_ReturnOriginalDocuments_AndRejectChangedPayloads()
    {
        await using var fixture = await TestFixture.CreateAsync();
        var accounting = new AccountingService(fixture.Db);
        await accounting.SeedDefaultAccountsAsync(fixture.Company.Id);
        var fiscalYear = await accounting.EnsureDefaultFiscalYearAsync(
            fixture.Company.Id);

        var store = new SalesInventoryService(fixture.Db, accounting);
        await store.EnsureDefaultsAsync(fixture.Company.Id);

        var warehouse = await fixture.Db.Warehouses.SingleAsync(
            x => x.CompanyId == fixture.Company.Id);
        var destination = new Warehouse
        {
            CompanyId = fixture.Company.Id,
            Code = "SECOND",
            Name = "Second warehouse"
        };
        var product = new StoreProduct
        {
            CompanyId = fixture.Company.Id,
            Sku = "IDEMP-001",
            Name = "Idempotency test product",
            Kind = ProductKind.Inventory,
            TrackInventory = true,
            SalesPrice = 100m,
            DefaultPurchasePrice = 60m
        };
        fixture.Db.Warehouses.Add(destination);
        fixture.Db.StoreProducts.Add(product);
        await fixture.Db.SaveChangesAsync();

        var documentDate = fiscalYear.StartDate.AddDays(5);
        var invoiceRequest = new CreateSalesInvoiceRequest(
            fiscalYear.Id,
            documentDate,
            warehouse.Id,
            null,
            SalesPaymentType.Cash,
            "idempotent invoice",
            new[]
            {
                new SalesInvoiceLineRequest(
                    product.Id, 1m, 100m, 0m, 0m)
            });

        var invoiceFirst = await store.CreateSalesInvoiceAsync(
            fixture.Company.Id,
            fixture.User.Id,
            invoiceRequest,
            "outbox-invoice-1");
        var invoiceReplay = await store.CreateSalesInvoiceAsync(
            fixture.Company.Id,
            fixture.User.Id,
            invoiceRequest,
            "outbox-invoice-1");

        Assert.Equal(invoiceFirst.Id, invoiceReplay.Id);
        Assert.Single(await fixture.Db.SalesInvoices.ToArrayAsync());

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => store.CreateSalesInvoiceAsync(
                fixture.Company.Id,
                fixture.User.Id,
                invoiceRequest with { Description = "changed payload" },
                "outbox-invoice-1"));

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => store.CreateSalesInvoiceAsync(
                fixture.Company.Id,
                fixture.OtherUser.Id,
                invoiceRequest,
                "outbox-invoice-1"));

        var receiptRequest = new CreatePurchaseReceiptRequest(
            fiscalYear.Id,
            documentDate,
            warehouse.Id,
            null,
            PurchasePaymentType.Cash,
            "idempotent receipt",
            new[]
            {
                new PurchaseReceiptLineRequest(
                    product.Id, 1m, 60m, 0m, 0m)
            });

        var receiptFirst = await store.CreatePurchaseReceiptAsync(
            fixture.Company.Id,
            fixture.User.Id,
            receiptRequest,
            "outbox-receipt-1");
        var receiptReplay = await store.CreatePurchaseReceiptAsync(
            fixture.Company.Id,
            fixture.User.Id,
            receiptRequest,
            "outbox-receipt-1");

        Assert.Equal(receiptFirst.Id, receiptReplay.Id);
        Assert.Single(await fixture.Db.PurchaseReceipts.ToArrayAsync());

        var transferRequest = new CreateWarehouseTransferRequest(
            documentDate,
            warehouse.Id,
            destination.Id,
            "idempotent transfer",
            new[]
            {
                new WarehouseTransferLineRequest(product.Id, 1m)
            });

        var transferFirst = await store.CreateWarehouseTransferAsync(
            fixture.Company.Id,
            fixture.User.Id,
            transferRequest,
            "outbox-transfer-1");
        var transferReplay = await store.CreateWarehouseTransferAsync(
            fixture.Company.Id,
            fixture.User.Id,
            transferRequest,
            "outbox-transfer-1");

        Assert.Equal(transferFirst.Id, transferReplay.Id);
        Assert.Single(await fixture.Db.WarehouseTransfers.ToArrayAsync());

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => store.CreateWarehouseTransferAsync(
                fixture.Company.Id,
                fixture.User.Id,
                transferRequest,
                "outbox-invoice-1"));

        await Assert.ThrowsAsync<ArgumentException>(
            () => store.CreateSalesInvoiceAsync(
                fixture.Company.Id,
                fixture.User.Id,
                invoiceRequest,
                " "));

        await Assert.ThrowsAsync<ArgumentException>(
            () => store.CreateSalesInvoiceAsync(
                fixture.Company.Id,
                fixture.User.Id,
                invoiceRequest,
                new string('k', 101)));

        await Assert.ThrowsAsync<ArgumentException>(
            () => store.CreateSalesInvoiceAsync(
                fixture.Company.Id,
                fixture.User.Id,
                invoiceRequest with { Lines = Array.Empty<SalesInvoiceLineRequest>() },
                "outbox-invalid-request"));

        Assert.False(await fixture.Db.StoreSyncReceipts.AnyAsync(
            x => x.IdempotencyKey == "outbox-invalid-request"));

        // Legacy callers without an idempotency key retain their previous behavior.
        await store.CreateSalesInvoiceAsync(
            fixture.Company.Id,
            fixture.User.Id,
            invoiceRequest with { Description = "legacy no-key request" });

        Assert.Equal(2, await fixture.Db.SalesInvoices.CountAsync());
        Assert.Equal(
            3,
            await fixture.Db.StoreSyncReceipts.CountAsync());

        var uniqueIndexes = await fixture.Db.Database
            .SqlQueryRaw<string>(
                """
                SELECT name AS Value
                FROM pragma_index_list('store_sync_receipts')
                WHERE "unique" = 1
                """)
            .ToListAsync();
        Assert.Contains(
            "IX_store_sync_receipts_CompanyId_IdempotencyKey",
            uniqueIndexes);
    }

    private sealed class TestFixture : IAsyncDisposable
    {
        private TestFixture(
            SqliteConnection connection,
            AppDbContext db,
            Company company,
            AppUser user)
        {
            Connection = connection;
            Db = db;
            Company = company;
            User = user;
        }

        private SqliteConnection Connection { get; }
        public AppDbContext Db { get; }
        public Company Company { get; }
        public AppUser User { get; }
        public AppUser OtherUser { get; private set; } = null!;

        public static async Task<TestFixture> CreateAsync()
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();

            var options = new DbContextOptionsBuilder<AppDbContext>()
                .UseSqlite(connection)
                .Options;

            var db = new AppDbContext(options);
            await db.Database.EnsureCreatedAsync();
            await SalesInventorySchemaBootstrapper.EnsureAsync(db);

            var company = new Company { Name = "Test Company" };
            db.Companies.Add(company);

            var user = new AppUser
            {
                CompanyId = company.Id,
                Username = "owner",
                DisplayName = "Owner",
                PasswordHash = "test-hash",
                Role = UserRole.Owner
            };
            var otherUser = new AppUser
            {
                CompanyId = company.Id,
                Username = "second-owner",
                DisplayName = "Second Owner",
                PasswordHash = "test-hash",
                Role = UserRole.Owner
            };
            db.Users.AddRange(user, otherUser);
            await db.SaveChangesAsync();

            return new TestFixture(connection, db, company, user)
            {
                OtherUser = otherUser
            };
        }

        public async ValueTask DisposeAsync()
        {
            await Db.DisposeAsync();
            await Connection.DisposeAsync();
        }
    }
}
