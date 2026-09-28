using System.Security.Cryptography;
using ERPAccounting.Api.Contracts;
using ERPAccounting.Api.Domain;
using ERPAccounting.Api.Infrastructure;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace ERPAccounting.Api.Security;

public sealed class AuthService(
    AppDbContext db,
    PasswordHasher<AppUser> passwordHasher,
    SessionStore sessions)
{
    private static readonly TimeSpan SessionLifetime = TimeSpan.FromMinutes(30);

    public async Task<(Company Company, AppUser User)> BootstrapAsync(
        BootstrapRequest request,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.CompanyName) ||
            string.IsNullOrWhiteSpace(request.Username) ||
            string.IsNullOrWhiteSpace(request.DisplayName) ||
            request.Password.Length < 10)
        {
            throw new ArgumentException(
                "Company, username and display name are required; password must be at least 10 characters.");
        }

        var username = request.Username.Trim();
        var normalizedUsername = username.ToLowerInvariant();

        var exists = await db.Users.AnyAsync(
            x => x.Username.ToLower() == normalizedUsername,
            cancellationToken);

        if (exists)
        {
            throw new InvalidOperationException("Username already exists.");
        }

        var company = new Company
        {
            Name = request.CompanyName.Trim()
        };

        var user = new AppUser
        {
            CompanyId = company.Id,
            Username = username,
            DisplayName = request.DisplayName.Trim(),
            PasswordHash = string.Empty,
            Role = UserRole.Owner
        };

        user.PasswordHash = passwordHasher.HashPassword(user, request.Password);

        db.Companies.Add(company);
        db.Users.Add(user);
        await db.SaveChangesAsync(cancellationToken);

        return (company, user);
    }

    public async Task<LoginResponse?> LoginAsync(
        LoginRequest request,
        CancellationToken cancellationToken = default)
    {
        var normalizedUsername = request.Username.Trim().ToLowerInvariant();

        var user = await db.Users.FirstOrDefaultAsync(
            x => x.IsActive && x.Username.ToLower() == normalizedUsername,
            cancellationToken);

        if (user is null)
        {
            return null;
        }

        var verified = passwordHasher.VerifyHashedPassword(
            user,
            user.PasswordHash,
            request.Password);

        if (verified == PasswordVerificationResult.Failed)
        {
            return null;
        }

        var now = DateTimeOffset.UtcNow;
        var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        var expiresAt = now.Add(SessionLifetime);

        var deviceName = request.DeviceName.Trim();
        var platform = request.Platform.Trim();
        var normalizedDeviceName = deviceName.ToLowerInvariant();
        var normalizedPlatform = platform.ToLowerInvariant();

        var device = await db.Devices.FirstOrDefaultAsync(
            x => x.UserId == user.Id &&
                 x.TrustState != DeviceTrustState.Revoked &&
                 x.DeviceName.ToLower() == normalizedDeviceName &&
                 x.Platform.ToLower() == normalizedPlatform,
            cancellationToken);

        if (device is null)
        {
            device = new TrustedDevice
            {
                CompanyId = user.CompanyId,
                UserId = user.Id,
                DeviceName = deviceName,
                Platform = platform,
                TrustState = DeviceTrustState.Pending,
                LastSeenAt = now
            };

            db.Devices.Add(device);
        }
        else
        {
            device.LastSeenAt = now;
        }

        await db.SaveChangesAsync(cancellationToken);
        sessions.Add(token, user.Id, expiresAt);

        return new LoginResponse(
            token,
            expiresAt,
            user.Id,
            user.CompanyId,
            user.DisplayName,
            user.Role.ToString(),
            user.MfaEnabled);
    }

    public async Task<AppUser?> ResolveAsync(
        string? bearerToken,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(bearerToken))
        {
            return null;
        }

        if (!sessions.TryResolve(bearerToken, out var userId))
        {
            return null;
        }

        return await db.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(
                x => x.Id == userId && x.IsActive,
                cancellationToken);
    }
}
