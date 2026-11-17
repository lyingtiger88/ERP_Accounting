using ERPAccounting.Api.Contracts;
using ERPAccounting.Api.Domain;
using ERPAccounting.Api.Infrastructure;
using ERPAccounting.Api.Security;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace ERPAccounting.Api.Tests;

public sealed class DevicePairingTests
{
    [Fact]
    public async Task PairingCode_TrustsNewDevice_AndIsSingleUse()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = new AppDbContext(
            new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options);
        await db.Database.EnsureCreatedAsync();

        var company = new Company { Name = "Pairing Test" };
        var hasher = new PasswordHasher<AppUser>();
        var user = new AppUser
        {
            CompanyId = company.Id,
            Username = "pairing-owner",
            DisplayName = "Pairing Owner",
            PasswordHash = string.Empty,
            Role = UserRole.Owner
        };
        user.PasswordHash = hasher.HashPassword(user, "Strong-Test-Password-2026");
        db.Companies.Add(company);
        db.Users.Add(user);
        await db.SaveChangesAsync();

        var service = new AuthService(db, hasher);
        var issued = await service.CreateDevicePairingCodeAsync(user.Id);
        Assert.Equal(16, issued.Code.Length);
        Assert.True(issued.ExpiresAt > DateTimeOffset.UtcNow);

        var login = await service.LoginAsync(new LoginRequest(
            user.Username,
            "Strong-Test-Password-2026",
            "Windows Desktop",
            "windows",
            PairingCode: issued.Code));

        Assert.NotNull(login);
        var device = await db.Devices.SingleAsync(x => x.UserId == user.Id);
        Assert.Equal(DeviceTrustState.Trusted, device.TrustState);

        var reused = await service.LoginAsync(new LoginRequest(
            user.Username,
            "Strong-Test-Password-2026",
            "Second Desktop",
            "windows",
            PairingCode: issued.Code));

        Assert.Null(reused);
        Assert.Equal(1, await db.Devices.CountAsync(x => x.TrustState == DeviceTrustState.Trusted));
    }

    [Fact]
    public async Task LoginWithoutPairingCode_KeepsNewDevicePending()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = new AppDbContext(
            new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options);
        await db.Database.EnsureCreatedAsync();

        var company = new Company { Name = "Unpaired Login Test" };
        var hasher = new PasswordHasher<AppUser>();
        var user = new AppUser
        {
            CompanyId = company.Id,
            Username = "unpaired-owner",
            DisplayName = "Unpaired Owner",
            PasswordHash = string.Empty,
            Role = UserRole.Owner
        };
        user.PasswordHash = hasher.HashPassword(user, "Strong-Test-Password-2026");
        db.Companies.Add(company);
        db.Users.Add(user);
        await db.SaveChangesAsync();

        var service = new AuthService(db, hasher);
        var login = await service.LoginAsync(new LoginRequest(
            user.Username,
            "Strong-Test-Password-2026",
            "New Windows Device",
            "windows"));

        Assert.NotNull(login);
        var device = await db.Devices.SingleAsync(x => x.UserId == user.Id);
        Assert.Equal(DeviceTrustState.Pending, device.TrustState);
    }

    [Fact]
    public async Task IssuingReplacementCode_InvalidatesPreviousCode()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = new AppDbContext(
            new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options);
        await db.Database.EnsureCreatedAsync();

        var company = new Company { Name = "Pairing Replacement Test" };
        var hasher = new PasswordHasher<AppUser>();
        var user = new AppUser
        {
            CompanyId = company.Id,
            Username = "replacement-owner",
            DisplayName = "Replacement Owner",
            PasswordHash = string.Empty,
            Role = UserRole.Owner
        };
        user.PasswordHash = hasher.HashPassword(user, "Strong-Test-Password-2026");
        db.Companies.Add(company);
        db.Users.Add(user);
        await db.SaveChangesAsync();

        var service = new AuthService(db, hasher);
        var first = await service.CreateDevicePairingCodeAsync(user.Id);
        var second = await service.CreateDevicePairingCodeAsync(user.Id);
        Assert.NotEqual(first.Code, second.Code);

        var result = await service.LoginAsync(new LoginRequest(
            user.Username,
            "Strong-Test-Password-2026",
            "Replacement Device",
            "windows",
            PairingCode: first.Code));

        Assert.Null(result);
        var success = await service.LoginAsync(new LoginRequest(
            user.Username,
            "Strong-Test-Password-2026",
            "Replacement Device",
            "windows",
            PairingCode: second.Code));

        Assert.NotNull(success);
        Assert.Equal(DeviceTrustState.Trusted,
            (await db.Devices.SingleAsync(x => x.UserId == user.Id)).TrustState);
    }

    [Fact]
    public async Task PairingCode_IsBoundToIssuingUser()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = new AppDbContext(
            new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options);
        await db.Database.EnsureCreatedAsync();

        var company = new Company { Name = "Pairing Isolation Test" };
        var hasher = new PasswordHasher<AppUser>();
        AppUser CreateUser(string username)
        {
            var value = new AppUser
            {
                CompanyId = company.Id,
                Username = username,
                DisplayName = username,
                PasswordHash = string.Empty,
                Role = UserRole.Owner
            };
            value.PasswordHash = hasher.HashPassword(value, "Strong-Test-Password-2026");
            return value;
        }

        var owner = CreateUser("owner-one");
        var other = CreateUser("owner-two");
        db.Companies.Add(company);
        db.Users.AddRange(owner, other);
        await db.SaveChangesAsync();

        var service = new AuthService(db, hasher);
        var issued = await service.CreateDevicePairingCodeAsync(owner.Id);
        var result = await service.LoginAsync(new LoginRequest(
            other.Username,
            "Strong-Test-Password-2026",
            "Unpaired Device",
            "windows",
            PairingCode: issued.Code));

        Assert.Null(result);
        Assert.Empty(await db.Devices.ToListAsync());
    }
}
