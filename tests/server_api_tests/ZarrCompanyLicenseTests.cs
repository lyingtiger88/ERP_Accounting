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
    public async Task LinkUpdate_ChangesOnlySpecifiedCompany()
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
        var rotatedAccount = Guid.NewGuid();
        await service.LinkCompanyAsync(first.Id, owner1.Id, rotatedAccount, default);
        Assert.Equal(2, await db.ZarrCompanyLicenses.CountAsync());
        Assert.Equal(rotatedAccount,
            (await db.ZarrCompanyLicenses.SingleAsync(x => x.CompanyId == first.Id)).AuthUserId);
        Assert.Equal(account2,
            (await db.ZarrCompanyLicenses.SingleAsync(x => x.CompanyId == second.Id)).AuthUserId);
    }

    private sealed class TestHttpClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new HttpClient();
    }
}
