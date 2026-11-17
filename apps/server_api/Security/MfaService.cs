using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ERPAccounting.Api.Domain;
using ERPAccounting.Api.Infrastructure;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace ERPAccounting.Api.Security;

public sealed class MfaService(
    AppDbContext db,
    IDataProtectionProvider protectionProvider,
    PasswordHasher<AppUser> passwordHasher)
{
    private readonly IDataProtector protector =
        protectionProvider.CreateProtector("Zarr.UserMfa.Totp.v1");

    public async Task<Contracts.MfaSetupResponse> BeginSetupAsync(
        Guid userId, string username, CancellationToken ct)
    {
        var encoded = Base32Encode(RandomNumberGenerator.GetBytes(20));
        var row = await db.MfaCredentials.SingleOrDefaultAsync(x => x.UserId == userId, ct);
        if (row is null)
        {
            row = new UserMfaCredential { UserId = userId };
            db.MfaCredentials.Add(row);
        }
        row.PendingProtectedSecret = protector.Protect(encoded);
        row.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
        var issuer = Uri.EscapeDataString("Zarr");
        var label = Uri.EscapeDataString("Zarr:" + username);
        var uri = $"otpauth://totp/{label}?secret={encoded}&issuer={issuer}&algorithm=SHA1&digits=6&period=30";
        return new Contracts.MfaSetupResponse(encoded, uri);
    }

    public async Task<Contracts.MfaEnableResponse> ConfirmSetupAsync(
        Guid userId, string code, CancellationToken ct)
    {
        var row = await db.MfaCredentials.SingleOrDefaultAsync(x => x.UserId == userId, ct)
            ?? throw new InvalidOperationException("Start MFA setup first.");
        if (string.IsNullOrWhiteSpace(row.PendingProtectedSecret))
            throw new InvalidOperationException("No pending MFA setup exists.");
        var secret = protector.Unprotect(row.PendingProtectedSecret);
        var acceptedStep = MatchTotpStep(secret, code, DateTimeOffset.UtcNow);
        if (acceptedStep is null)
            throw new ArgumentException("Authenticator code is invalid.");
        var recovery = Enumerable.Range(0, 10)
            .Select(_ => Convert.ToHexString(RandomNumberGenerator.GetBytes(8)))
            .ToArray();
        row.ProtectedSecret = row.PendingProtectedSecret;
        row.PendingProtectedSecret = null;
        row.RecoveryCodeHashesJson = JsonSerializer.Serialize(recovery.Select(HashCode));
        row.LastAcceptedTimeStep = acceptedStep;
        row.EnabledAt = DateTimeOffset.UtcNow;
        row.UpdatedAt = DateTimeOffset.UtcNow;
        var user = await db.Users.SingleAsync(x => x.Id == userId, ct);
        user.MfaEnabled = true;
        db.SecurityAuditLogs.Add(new SecurityAuditLog {
            CompanyId = user.CompanyId, UserId = user.Id, EventType = "MFA_ENABLE",
            Outcome = "SUCCESS", Username = user.Username, Details = "TOTP enabled; recovery codes generated."
        });
        await db.SaveChangesAsync(ct);
        return new Contracts.MfaEnableResponse(true, recovery);
    }

    public async Task<bool> VerifyLoginCodeAsync(Guid userId, string? code, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(code)) return false;
        var row = await db.MfaCredentials.SingleOrDefaultAsync(x => x.UserId == userId, ct);
        if (row is null || row.EnabledAt is null || string.IsNullOrWhiteSpace(row.ProtectedSecret))
            return false;
        var secret = protector.Unprotect(row.ProtectedSecret);
        var acceptedStep = MatchTotpStep(secret, code, DateTimeOffset.UtcNow);
        if (acceptedStep is not null)
        {
            if (row.LastAcceptedTimeStep is long previous && acceptedStep <= previous)
                return false;
            row.LastAcceptedTimeStep = acceptedStep;
            row.UpdatedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(ct);
            return true;
        }
        var hashes = JsonSerializer.Deserialize<List<string>>(row.RecoveryCodeHashesJson) ?? [];
        var hash = HashCode(NormalizeRecoveryCode(code));
        var index = hashes.FindIndex(x => CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(x), Encoding.UTF8.GetBytes(hash)));
        if (index < 0) return false;
        hashes.RemoveAt(index);
        row.RecoveryCodeHashesJson = JsonSerializer.Serialize(hashes);
        row.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
        return true;
    }

    public async Task DisableAsync(AppUser user, string password, string code, CancellationToken ct)
    {
        var verified = passwordHasher.VerifyHashedPassword(user, user.PasswordHash, password);
        if (verified == PasswordVerificationResult.Failed)
            throw new ArgumentException("Password is incorrect.");
        if (!await VerifyLoginCodeAsync(user.Id, code, ct))
            throw new ArgumentException("Authenticator or recovery code is invalid.");
        var row = await db.MfaCredentials.SingleOrDefaultAsync(x => x.UserId == user.Id, ct);
        if (row is not null) db.MfaCredentials.Remove(row);
        user.MfaEnabled = false;
        db.SecurityAuditLogs.Add(new SecurityAuditLog {
            CompanyId = user.CompanyId, UserId = user.Id, EventType = "MFA_DISABLE",
            Outcome = "SUCCESS", Username = user.Username, Details = "MFA disabled after password and second-factor verification."
        });
        await db.SaveChangesAsync(ct);
    }

    private static long? MatchTotpStep(string base32Secret, string code, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(code) || code.Length != 6 || code.Any(c => c < '0' || c > '9')) return null;
        var secret = Base32Decode(base32Secret);
        var step = now.ToUnixTimeSeconds() / 30;
        for (var offset = -1; offset <= 1; offset++)
        {
            var counter = BitConverter.GetBytes(System.Net.IPAddress.HostToNetworkOrder(step + offset));
            using var hmac = new HMACSHA1(secret);
            var hash = hmac.ComputeHash(counter);
            var index = hash[^1] & 0x0f;
            var binary = ((hash[index] & 0x7f) << 24) |
                         ((hash[index + 1] & 0xff) << 16) |
                         ((hash[index + 2] & 0xff) << 8) |
                         (hash[index + 3] & 0xff);
            var expected = (binary % 1_000_000).ToString("D6");
            if (CryptographicOperations.FixedTimeEquals(
                Encoding.ASCII.GetBytes(expected), Encoding.ASCII.GetBytes(code))) return step + offset;
        }
        return null;
    }

    private static string Base32Encode(byte[] data)
    {
        const string alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
        var output = new StringBuilder();
        var buffer = 0; var bits = 0;
        foreach (var value in data)
        {
            buffer = (buffer << 8) | value; bits += 8;
            while (bits >= 5) { output.Append(alphabet[(buffer >> (bits - 5)) & 31]); bits -= 5; }
        }
        if (bits > 0) output.Append(alphabet[(buffer << (5 - bits)) & 31]);
        return output.ToString();
    }

    private static byte[] Base32Decode(string value)
    {
        const string alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
        var output = new List<byte>(); var buffer = 0; var bits = 0;
        foreach (var c in value.TrimEnd('=').ToUpperInvariant())
        {
            var index = alphabet.IndexOf(c);
            if (index < 0) throw new FormatException("Invalid Base32 secret.");
            buffer = (buffer << 5) | index; bits += 5;
            if (bits >= 8) { output.Add((byte)((buffer >> (bits - 8)) & 255)); bits -= 8; }
        }
        return output.ToArray();
    }

    private static string NormalizeRecoveryCode(string value) =>
        new(value.Where(char.IsLetterOrDigit).Select(char.ToUpperInvariant).ToArray());

    private static string HashCode(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(NormalizeRecoveryCode(value))));
}
