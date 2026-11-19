using System.Security.Cryptography;
using System.Text;
using ERPAccounting.Api.Domain;
using ERPAccounting.Api.Infrastructure;
using ERPAccounting.Api.Security;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace ERPAccounting.Api.Tests;

public sealed class MfaServiceTests
{
    [Fact]
    public async Task Enrollment_RequiresValidTotp_AndRecoveryCodesAreSingleUse()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = new AppDbContext(
            new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options);
        await db.Database.EnsureCreatedAsync();

        var company = new Company { Name = "MFA Test" };
        var hasher = new PasswordHasher<AppUser>();
        var user = new AppUser
        {
            CompanyId = company.Id,
            Username = "mfa-owner",
            DisplayName = "MFA Owner",
            PasswordHash = "placeholder",
            Role = UserRole.Owner
        };
        user.PasswordHash = hasher.HashPassword(user, "Strong-Test-Password-2026");
        db.Companies.Add(company);
        db.Users.Add(user);
        await db.SaveChangesAsync();

        var service = new MfaService(db, new EphemeralDataProtectionProvider(), hasher);
        var setup = await service.BeginSetupAsync(user.Id, user.Username, default);
        Assert.StartsWith("otpauth://totp/", setup.OtpauthUri);
        var code = CurrentTotp(setup.Secret);
        var enabled = await service.ConfirmSetupAsync(user.Id, code, default);

        Assert.True(enabled.Enabled);
        Assert.Equal(10, enabled.RecoveryCodes.Count);
        Assert.True(user.MfaEnabled);
        Assert.False(await service.VerifyLoginCodeAsync(user.Id, code, default));

        var recovery = enabled.RecoveryCodes[0];
        Assert.True(await service.VerifyLoginCodeAsync(user.Id, recovery, default));
        Assert.False(await service.VerifyLoginCodeAsync(user.Id, recovery, default));
    }


    [Fact]
    public async Task RepeatedInvalidCodes_LockMfaVerification_AndRejectValidCodeDuringLockout()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = new AppDbContext(
            new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options);
        await db.Database.EnsureCreatedAsync();

        var company = new Company { Name = "MFA Lockout Test" };
        var hasher = new PasswordHasher<AppUser>();
        var user = new AppUser
        {
            CompanyId = company.Id,
            Username = "mfa-lockout-owner",
            DisplayName = "MFA Lockout Owner",
            PasswordHash = "placeholder",
            Role = UserRole.Owner
        };
        user.PasswordHash = hasher.HashPassword(user, "Strong-Test-Password-2026");
        db.Companies.Add(company);
        db.Users.Add(user);
        await db.SaveChangesAsync();

        var service = new MfaService(db, new EphemeralDataProtectionProvider(), hasher);
        var setup = await service.BeginSetupAsync(user.Id, user.Username, default);
        var validCode = CurrentTotp(setup.Secret);
        await service.ConfirmSetupAsync(user.Id, validCode, default);

        for (var attempt = 0; attempt < 5; attempt++)
            Assert.False(await service.VerifyLoginCodeAsync(user.Id, "not-a-valid-code", default));

        // The lockout must reject even a correct TOTP until the lock expires.
        Assert.False(await service.VerifyLoginCodeAsync(user.Id, CurrentTotp(setup.Secret), default));

        var credential = await db.MfaCredentials.SingleAsync(x => x.UserId == user.Id);
        Assert.NotNull(credential.LockedUntil);
        Assert.True(credential.LockedUntil > DateTimeOffset.UtcNow);
        Assert.Equal(0, credential.FailedAttempts);
    }

    private static string CurrentTotp(string secret)
    {
        var key = Base32Decode(secret);
        var counter = BitConverter.GetBytes(
            System.Net.IPAddress.HostToNetworkOrder(DateTimeOffset.UtcNow.ToUnixTimeSeconds() / 30));
        using var hmac = new HMACSHA1(key);
        var hash = hmac.ComputeHash(counter);
        var offset = hash[^1] & 0x0f;
        var binary = ((hash[offset] & 0x7f) << 24) |
                     ((hash[offset + 1] & 0xff) << 16) |
                     ((hash[offset + 2] & 0xff) << 8) |
                     (hash[offset + 3] & 0xff);
        return (binary % 1_000_000).ToString("D6");
    }

    private static byte[] Base32Decode(string value)
    {
        const string alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
        var bytes = new List<byte>();
        var buffer = 0;
        var bits = 0;
        foreach (var c in value)
        {
            buffer = (buffer << 5) | alphabet.IndexOf(c);
            bits += 5;
            if (bits >= 8)
            {
                bytes.Add((byte)((buffer >> (bits - 8)) & 255));
                bits -= 8;
            }
        }
        return bytes.ToArray();
    }
}
