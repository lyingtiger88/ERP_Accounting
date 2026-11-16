using System.Net;
using System.Text;
using System.Security.Cryptography;
using System.Text.Json;
using ERPAccounting.Api.Domain;
using ERPAccounting.Api.Infrastructure;
using ERPAccounting.Api.Security;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace ERPAccounting.Api.Tests;

public sealed class ZarrCompanyEntitlementTests
{
    [Fact]
    public async Task CompanyMode_DeniesAccountScopedLicenseAndWrongCompanyResponse()
    {
        await using var dbConnection = new SqliteConnection("Data Source=:memory:");
        await dbConnection.OpenAsync();
        await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(dbConnection).Options);
        await db.Database.EnsureCreatedAsync();
        var company = new Company { Name = "One" };
        db.Companies.Add(company);
        var owner = new AppUser
        {
            CompanyId = company.Id, Username = "company-mode-owner",
            DisplayName = "Owner", PasswordHash = "test", Role = UserRole.Owner
        };
        db.Users.Add(owner);
        await db.SaveChangesAsync();

        var account = Guid.NewGuid();
        var seen = new List<string>();
        var factory = new StubHttpClientFactory(req =>
        {
            seen.Add(req.RequestUri!.AbsolutePath);
            if (req.RequestUri.AbsolutePath.EndsWith("/zarr_bind_company"))
                return JsonResponse("""{"state":"active"}""");

            // Even an otherwise active account has no authority to write
            // on behalf of the wrong ERP company ID.
            return JsonResponse(
                """{"product_id":"zarr","company_id":"00000000-0000-0000-0000-000000000001","can_write":true}""");
        });

        var service = new ZarrLicenseService(db, factory, Config());
        await service.LinkCompanyAsync(company.Id, owner.Id, account, default);
        Assert.False(await service.CanWriteAsync(company.Id, default));
        Assert.Contains("/rest/v1/rpc/zarr_company_license_status", seen);
        Assert.DoesNotContain("/rest/v1/zurvan_licenses", seen);
    }

    [Fact]
    public async Task CompanyMode_ValidCompanyEntitlementAllowsWrite()
    {
        await using var dbConnection = new SqliteConnection("Data Source=:memory:");
        await dbConnection.OpenAsync();
        await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(dbConnection).Options);
        await db.Database.EnsureCreatedAsync();
        var company = new Company { Name = "Two" };
        db.Companies.Add(company);
        var owner = new AppUser
        {
            CompanyId = company.Id, Username = "company-mode-owner-two",
            DisplayName = "Owner", PasswordHash = "test", Role = UserRole.Owner
        };
        db.Users.Add(owner);
        await db.SaveChangesAsync();

        var factory = new StubHttpClientFactory(req =>
            req.RequestUri!.AbsolutePath.EndsWith("/zarr_bind_company")
                ? JsonResponse("""{"state":"active"}""")
                : JsonResponse(
                    "{\"product_id\":\"zarr\",\"company_id\":\"" +
                    company.Id + "\",\"can_write\":true}"));
        var service = new ZarrLicenseService(db, factory, Config());
        await service.LinkCompanyAsync(company.Id, owner.Id, Guid.NewGuid(), default);
        Assert.True(await service.CanWriteAsync(company.Id, default));
    }

    [Fact]
    public async Task CompanyMode_TrialRequiresExistingCompanyLink()
    {
        await using var dbConnection = new SqliteConnection("Data Source=:memory:");
        await dbConnection.OpenAsync();
        await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(dbConnection).Options);
        await db.Database.EnsureCreatedAsync();
        var service = new ZarrLicenseService(
            db, new StubHttpClientFactory(_ => JsonResponse("{}")), Config());
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.StartCompanyTrialAsync(Guid.NewGuid(), default));
    }

    [Fact]
    public void OfflineEntitlement_IsSignedAndBoundToCompanyForAtMostSevenDays()
    {
        using var rsa = RSA.Create(3072);
        var privatePem = rsa.ExportPkcs8PrivateKeyPem();
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Kaliia:SupabaseUrl"] = "https://supabase.test",
                ["Kaliia:ServiceRoleKey"] = "test-server-only-key",
                ["Kaliia:UseCompanyEntitlements"] = "true",
                ["Kaliia:EntitlementPrivateKeyPem"] = privatePem,
                ["Kaliia:EntitlementKeyId"] = "zarr-rs256-2026-01"
            }).Build();

        using var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();
        using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(connection).Options);
        var service = new ZarrLicenseService(
            db, new StubHttpClientFactory(_ => JsonResponse("{}")), config);
        var companyId = Guid.NewGuid();
        using var entitlement = JsonDocument.Parse(
            """{"status":"active","can_write":true,"generation":4,"expires_at":"2026-12-30T00:00:00Z"}""");

        var token = service.CreateSignedOfflineEntitlement(
            entitlement.RootElement, companyId);
        var parts = token.Split('.');
        Assert.Equal(3, parts.Length);
        var signingInput = Encoding.ASCII.GetBytes(parts[0] + "." + parts[1]);
        var signature = DecodeBase64Url(parts[2]);
        Assert.True(rsa.VerifyData(
            signingInput, signature, HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1));

        using var payload = JsonDocument.Parse(DecodeBase64Url(parts[1]));
        var claims = payload.RootElement;
        Assert.Equal(companyId.ToString(), claims.GetProperty("company_id").GetString());
        Assert.Equal("zarr", claims.GetProperty("product_id").GetString());
        Assert.Equal(4, claims.GetProperty("generation").GetInt32());
        Assert.True(claims.GetProperty("exp").GetInt64() <=
            claims.GetProperty("iat").GetInt64() + TimeSpan.FromDays(7).TotalSeconds);
    }

    private static byte[] DecodeBase64Url(string value)
    {
        var normalized = value.Replace('-', '+').Replace('_', '/');
        normalized = normalized.PadRight((normalized.Length + 3) / 4 * 4, '=');
        return Convert.FromBase64String(normalized);
    }

    private static IConfiguration Config() => new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Kaliia:SupabaseUrl"] = "https://supabase.test",
            ["Kaliia:ServiceRoleKey"] = "test-server-only-key",
            ["Kaliia:UseCompanyEntitlements"] = "true"
        }).Build();

    private static HttpResponseMessage JsonResponse(string content) =>
        new(HttpStatusCode.OK)
        {
            Content = new StringContent(content, Encoding.UTF8, "application/json")
        };

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
            HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(responder(request));
    }
}
