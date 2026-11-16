using System.Net;
using System.Text;
using ERPAccounting.Api.Domain;
using ERPAccounting.Api.Infrastructure;
using ERPAccounting.Api.Security;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace ERPAccounting.Api.Tests;

public sealed class ZarrCompanyLicenseTests
{
    [Fact]
    public async Task CompanyLink_IsScopedToCompany_AndMissingSecretFailsClosed()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(connection).Options;
        await using var db = new AppDbContext(options);
        await db.Database.EnsureCreatedAsync();

        var company = new Company { Name = "Zarr Test Company" };
        db.Companies.Add(company);
        var owner = new AppUser
        {
            CompanyId = company.Id,
            Username = "zarr-test-owner",
            DisplayName = "Owner",
            PasswordHash = "test",
            Role = UserRole.Owner
        };
        db.Users.Add(owner);
        await db.SaveChangesAsync();

        var service = new ZarrLicenseService(
            db, new TestHttpClientFactory(), new ConfigurationBuilder().Build());
        Assert.Equal("zarr", ZarrLicenseService.ProductId);
        Assert.False(await service.HasCompanyLinkAsync(company.Id, default));
        Assert.False(await service.CanWriteAsync(company.Id, default));

        var kaliiaUser = Guid.NewGuid();
        await service.LinkCompanyAsync(company.Id, owner.Id, kaliiaUser, default);
        Assert.True(await service.HasCompanyLinkAsync(company.Id, default));
        Assert.False(await service.CanWriteAsync(company.Id, default));

        var link = await db.ZarrCompanyLicenses.SingleAsync();
        Assert.Equal(kaliiaUser, link.AuthUserId);
        Assert.Equal(company.Id, link.CompanyId);
    }

    [Fact]
    public async Task LinkCompanyAsync_RejectsUnapprovedRebinding_AndSameOwnerRetryIsIdempotent()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = new AppDbContext(
            new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options);
        await db.Database.EnsureCreatedAsync();

        var first = new Company { Name = "First" };
        var second = new Company { Name = "Second" };
        db.Companies.AddRange(first, second);
        var owner1 = new AppUser
        {
            CompanyId = first.Id, Username = "owner-first", DisplayName = "A",
            PasswordHash = "test", Role = UserRole.Owner
        };
        var owner2 = new AppUser
        {
            CompanyId = second.Id, Username = "owner-second", DisplayName = "B",
            PasswordHash = "test", Role = UserRole.Owner
        };
        db.Users.AddRange(owner1, owner2);
        await db.SaveChangesAsync();
        var service = new ZarrLicenseService(
            db, new TestHttpClientFactory(), new ConfigurationBuilder().Build());

        var account1 = Guid.NewGuid();
        var account2 = Guid.NewGuid();
        await service.LinkCompanyAsync(first.Id, owner1.Id, account1, default);
        await service.LinkCompanyAsync(second.Id, owner2.Id, account2, default);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.LinkCompanyAsync(first.Id, owner1.Id, Guid.NewGuid(), default));

        // Retrying the already-linked account is safe and leaves the audit identity intact.
        await service.LinkCompanyAsync(first.Id, owner1.Id, account1, default);

        Assert.Equal(2, await db.ZarrCompanyLicenses.CountAsync());
        Assert.Equal(account1,
            (await db.ZarrCompanyLicenses.SingleAsync(x => x.CompanyId == first.Id)).AuthUserId);
        Assert.Equal(account2,
            (await db.ZarrCompanyLicenses.SingleAsync(x => x.CompanyId == second.Id)).AuthUserId);
    }

    [Fact]
    public async Task CanWriteAsync_DoesNotTreatSubscriptionWithoutExpiryAsPerpetual()
    {
        var fixture = await CreateLinkedService(
            """[{"status":"active","type":"subscription","expires_at":null}]""");
        await using var connection = fixture.Connection;
        await using var db = fixture.Database;

        Assert.False(await fixture.Service.CanWriteAsync(fixture.Company.Id, default));
    }

    [Fact]
    public async Task CanWriteAsync_AllowsOnlyExplicitLifetimeWithoutExpiry()
    {
        var fixture = await CreateLinkedService(
            """[{"status":"active","type":"lifetime","expires_at":null}]""");
        await using var connection = fixture.Connection;
        await using var db = fixture.Database;

        Assert.True(await fixture.Service.CanWriteAsync(fixture.Company.Id, default));
    }

    [Fact]
    public async Task CanWriteAsync_RejectsExpiredTrial()
    {
        var fixture = await CreateLinkedService(
            """[{"status":"active","type":"trial","expires_at":"2000-01-01T00:00:00Z"}]""");
        await using var connection = fixture.Connection;
        await using var db = fixture.Database;

        Assert.False(await fixture.Service.CanWriteAsync(fixture.Company.Id, default));
    }

    [Fact]
    public async Task CanWriteAsync_RejectsUnknownLicenseType()
    {
        var fixture = await CreateLinkedService(
            """[{"status":"active","type":"unknown","expires_at":null}]""");
        await using var connection = fixture.Connection;
        await using var db = fixture.Database;

        Assert.False(await fixture.Service.CanWriteAsync(fixture.Company.Id, default));
    }

    private static async Task<(
        SqliteConnection Connection,
        AppDbContext Database,
        Company Company,
        ZarrLicenseService Service)> CreateLinkedService(string licenseJson)
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var db = new AppDbContext(
            new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options);
        await db.Database.EnsureCreatedAsync();

        var company = new Company { Name = "Zarr License Test" };
        db.Companies.Add(company);
        var owner = new AppUser
        {
            CompanyId = company.Id,
            Username = "license-owner-" + Guid.NewGuid().ToString("N"),
            DisplayName = "Owner",
            PasswordHash = "test",
            Role = UserRole.Owner
        };
        db.Users.Add(owner);
        await db.SaveChangesAsync();

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Kaliia:SupabaseUrl"] = "https://supabase.test",
                ["Kaliia:ServiceRoleKey"] = "service-role-test"
            })
            .Build();

        var factory = new StubHttpClientFactory(request =>
        {
            var body = request.RequestUri!.AbsolutePath.EndsWith(
                "/zurvan_accounts", StringComparison.Ordinal)
                ? """[{"status":"active"}]"""
                : licenseJson;
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            };
        });

        var service = new ZarrLicenseService(db, factory, configuration);
        await service.LinkCompanyAsync(company.Id, owner.Id, Guid.NewGuid(), default);
        return (connection, db, company, service);
    }

    private sealed class TestHttpClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new HttpClient();
    }

    private sealed class StubHttpClientFactory(
        Func<HttpRequestMessage, HttpResponseMessage> responder) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) =>
            new(new StubHttpMessageHandler(responder));
    }

    private sealed class StubHttpMessageHandler(
        Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            Task.FromResult(responder(request));
    }
}
