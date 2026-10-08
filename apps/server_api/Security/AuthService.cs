using System.Security.Cryptography;
using System.Text;
using ERPAccounting.Api.Contracts;
using ERPAccounting.Api.Domain;
using ERPAccounting.Api.Infrastructure;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace ERPAccounting.Api.Security;

public sealed class AuthService(
    AppDbContext db,
    PasswordHasher<AppUser> passwordHasher)
{
    private static readonly TimeSpan AccessLifetime =
        TimeSpan.FromMinutes(30);
    private static readonly TimeSpan RefreshLifetime =
        TimeSpan.FromDays(30);

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

        user.PasswordHash = passwordHasher.HashPassword(
            user,
            request.Password);

        db.Companies.Add(company);
        db.Users.Add(user);
        await db.SaveChangesAsync(cancellationToken);

        return (company, user);
    }

    public async Task<LoginResponse?> LoginAsync(
        LoginRequest request,
        CancellationToken cancellationToken = default)
    {
        var normalizedUsername =
            request.Username.Trim().ToLowerInvariant();

        var user = await db.Users.FirstOrDefaultAsync(
            x =>
                x.IsActive &&
                x.Username.ToLower() == normalizedUsername,
            cancellationToken);

        if (user is null)
        {
            AddSecurityAudit(
                null,
                null,
                "LOGIN",
                "DENIED",
                request.Username.Trim(),
                "Unknown or inactive user.");
            await db.SaveChangesAsync(cancellationToken);
            return null;
        }

        var verified = passwordHasher.VerifyHashedPassword(
            user,
            user.PasswordHash,
            request.Password);

        if (verified == PasswordVerificationResult.Failed)
        {
            AddSecurityAudit(
                user.CompanyId,
                user.Id,
                "LOGIN",
                "DENIED",
                user.Username,
                "Password verification failed.");
            await db.SaveChangesAsync(cancellationToken);
            return null;
        }

        var now = DateTimeOffset.UtcNow;
        var deviceName = request.DeviceName.Trim();
        var platform = request.Platform.Trim();
        var normalizedDeviceName = deviceName.ToLowerInvariant();
        var normalizedPlatform = platform.ToLowerInvariant();

        var device = await db.Devices.FirstOrDefaultAsync(
            x =>
                x.UserId == user.Id &&
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

        await RevokeExpiredSessionsAsync(now, cancellationToken);

        var pair = CreateTokenPair(now);
        var session = new AuthSession
        {
            CompanyId = user.CompanyId,
            UserId = user.Id,
            DeviceId = device.Id,
            AccessTokenHash = HashToken(pair.AccessToken),
            RefreshTokenHash = HashToken(pair.RefreshToken),
            AccessExpiresAt = pair.AccessExpiresAt,
            RefreshExpiresAt = pair.RefreshExpiresAt,
            CreatedAt = now,
            LastRefreshedAt = now
        };

        db.AuthSessions.Add(session);
        AddSecurityAudit(
            user.CompanyId,
            user.Id,
            "LOGIN",
            "SUCCESS",
            user.Username,
            "Device=" + deviceName + "; Platform=" + platform);
        await db.SaveChangesAsync(cancellationToken);

        return ToLoginResponse(user, pair);
    }

    public async Task<LoginResponse?> RefreshAsync(
        RefreshSessionRequest request,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.RefreshToken))
        {
            return null;
        }

        var now = DateTimeOffset.UtcNow;
        var refreshHash = HashToken(request.RefreshToken);

        var session = await db.AuthSessions
            .FirstOrDefaultAsync(
                x =>
                    x.RefreshTokenHash == refreshHash &&
                    x.RevokedAt == null,
                cancellationToken);

        if (session is null ||
            session.RefreshExpiresAt <= now)
        {
            if (session is not null && session.RevokedAt is null)
            {
                session.RevokedAt = now;
                session.RevokeReason = "RefreshExpired";
                await db.SaveChangesAsync(cancellationToken);
            }

            return null;
        }

        var user = await db.Users.FirstOrDefaultAsync(
            x => x.Id == session.UserId && x.IsActive,
            cancellationToken);

        if (user is null)
        {
            session.RevokedAt = now;
            session.RevokeReason = "UserInactive";
            await db.SaveChangesAsync(cancellationToken);
            return null;
        }

        var pair = CreateTokenPair(now);

        session.AccessTokenHash = HashToken(pair.AccessToken);
        session.RefreshTokenHash = HashToken(pair.RefreshToken);
        session.AccessExpiresAt = pair.AccessExpiresAt;
        session.RefreshExpiresAt = pair.RefreshExpiresAt;
        session.LastRefreshedAt = now;

        if (session.DeviceId is Guid deviceId)
        {
            var device = await db.Devices.FirstOrDefaultAsync(
                x => x.Id == deviceId,
                cancellationToken);

            if (device is not null)
            {
                if (device.TrustState == DeviceTrustState.Revoked)
                {
                    session.RevokedAt = now;
                    session.RevokeReason = "DeviceRevoked";
                    await db.SaveChangesAsync(cancellationToken);
                    return null;
                }

                device.LastSeenAt = now;
            }
        }

        AddSecurityAudit(
            user.CompanyId,
            user.Id,
            "REFRESH_SESSION",
            "SUCCESS",
            user.Username,
            session.DeviceId.HasValue
                ? "DeviceId=" + session.DeviceId.Value
                : null);
        await db.SaveChangesAsync(cancellationToken);

        return ToLoginResponse(user, pair);
    }

    public async Task<bool> RevokeAsync(
        string? accessToken,
        string? refreshToken,
        CancellationToken cancellationToken = default)
    {
        AuthSession? session = null;

        if (!string.IsNullOrWhiteSpace(accessToken))
        {
            var accessHash = HashToken(accessToken);
            session = await db.AuthSessions.FirstOrDefaultAsync(
                x =>
                    x.AccessTokenHash == accessHash &&
                    x.RevokedAt == null,
                cancellationToken);
        }

        if (session is null &&
            !string.IsNullOrWhiteSpace(refreshToken))
        {
            var refreshHash = HashToken(refreshToken);
            session = await db.AuthSessions.FirstOrDefaultAsync(
                x =>
                    x.RefreshTokenHash == refreshHash &&
                    x.RevokedAt == null,
                cancellationToken);
        }

        if (session is null)
        {
            return false;
        }

        session.RevokedAt = DateTimeOffset.UtcNow;
        session.RevokeReason = "Logout";

        var logoutUser = await db.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(
                x => x.Id == session.UserId,
                cancellationToken);

        AddSecurityAudit(
            session.CompanyId,
            session.UserId,
            "LOGOUT",
            "SUCCESS",
            logoutUser?.Username,
            session.DeviceId.HasValue
                ? "DeviceId=" + session.DeviceId.Value
                : null);
        await db.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<AppUser?> ResolveAsync(
        string? bearerToken,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(bearerToken))
        {
            return null;
        }

        var now = DateTimeOffset.UtcNow;
        var accessHash = HashToken(bearerToken);

        var session = await db.AuthSessions
            .AsNoTracking()
            .FirstOrDefaultAsync(
                x =>
                    x.AccessTokenHash == accessHash &&
                    x.RevokedAt == null,
                cancellationToken);

        if (session is null || session.AccessExpiresAt <= now)
        {
            return null;
        }

        return await db.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(
                x => x.Id == session.UserId && x.IsActive,
                cancellationToken);
    }

    public async Task<IReadOnlyList<SecurityAuditView>>
        GetSecurityAuditAsync(
            Guid companyId,
            int limit = 200,
            CancellationToken cancellationToken = default)
    {
        limit = Math.Clamp(limit, 1, 1000);

        var rows = await db.SecurityAuditLogs
            .AsNoTracking()
            .Where(x => x.CompanyId == companyId)
            .ToArrayAsync(cancellationToken);

        return rows
            .OrderByDescending(x => x.CreatedAt)
            .Take(limit)
            .Select(x => new SecurityAuditView(
                x.Id,
                x.UserId,
                x.EventType,
                x.Outcome,
                x.Username,
                x.Details,
                x.CreatedAt))
            .ToArray();
    }

    public async Task<IReadOnlyList<DeviceResponse>> GetDevicesAsync(
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        return await db.Devices
            .AsNoTracking()
            .Where(x => x.UserId == userId)
            .OrderByDescending(x => x.LastSeenAt)
            .ThenByDescending(x => x.FirstSeenAt)
            .Select(x => new DeviceResponse(
                x.Id,
                x.DeviceName,
                x.Platform,
                x.TrustState.ToString(),
                x.FirstSeenAt,
                x.LastSeenAt))
            .ToArrayAsync(cancellationToken);
    }

    public async Task<bool> RevokeDeviceAsync(
        Guid userId,
        Guid deviceId,
        CancellationToken cancellationToken = default)
    {
        var device = await db.Devices
            .FirstOrDefaultAsync(
                x => x.Id == deviceId && x.UserId == userId,
                cancellationToken);

        if (device is null)
        {
            return false;
        }

        var now = DateTimeOffset.UtcNow;
        device.TrustState = DeviceTrustState.Revoked;
        device.RevokedAt = now;

        var sessions = await db.AuthSessions
            .Where(x =>
                x.UserId == userId &&
                x.DeviceId == deviceId &&
                x.RevokedAt == null)
            .ToArrayAsync(cancellationToken);

        foreach (var session in sessions)
        {
            session.RevokedAt = now;
            session.RevokeReason = "DeviceRevoked";
        }

        var user = await db.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(
                x => x.Id == userId,
                cancellationToken);

        AddSecurityAudit(
            device.CompanyId,
            userId,
            "DEVICE_REVOKE",
            "SUCCESS",
            user?.Username,
            "Device=" + device.DeviceName +
            "; Platform=" + device.Platform);
        await db.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<IReadOnlyList<AdminUserView>> GetUsersAsync(
        Guid companyId,
        CancellationToken cancellationToken = default)
    {
        return await db.Users
            .AsNoTracking()
            .Where(x => x.CompanyId == companyId)
            .OrderBy(x => x.Username)
            .Select(x => new AdminUserView(
                x.Id,
                x.Username,
                x.DisplayName,
                x.Role,
                x.IsActive,
                x.MfaEnabled,
                x.CreatedAt))
            .ToArrayAsync(cancellationToken);
    }

    public async Task<AdminUserView> CreateUserAsync(
        Guid companyId,
        CreateUserRequest request,
        CancellationToken cancellationToken = default)
    {
        var username = request.Username.Trim();
        var displayName = request.DisplayName.Trim();

        if (username.Length == 0 ||
            displayName.Length == 0 ||
            request.Password.Length < 10)
        {
            throw new ArgumentException(
                "Username and display name are required; password must be at least 10 characters.");
        }

        var normalized = username.ToLowerInvariant();
        if (await db.Users.AnyAsync(
                x => x.Username.ToLower() == normalized,
                cancellationToken))
        {
            throw new InvalidOperationException("Username already exists.");
        }

        var user = new AppUser
        {
            CompanyId = companyId,
            Username = username,
            DisplayName = displayName,
            PasswordHash = string.Empty,
            Role = request.Role,
            IsActive = true
        };

        user.PasswordHash = passwordHasher.HashPassword(
            user,
            request.Password);

        db.Users.Add(user);
        AddSecurityAudit(
            companyId,
            null,
            "USER_CREATE",
            "SUCCESS",
            username,
            "Role=" + request.Role);
        await db.SaveChangesAsync(cancellationToken);

        return ToAdminView(user);
    }

    public async Task<AdminUserView> UpdateUserAsync(
        Guid companyId,
        Guid actorUserId,
        Guid userId,
        UpdateUserRequest request,
        CancellationToken cancellationToken = default)
    {
        var user = await db.Users.FirstOrDefaultAsync(
            x => x.Id == userId && x.CompanyId == companyId,
            cancellationToken)
            ?? throw new ArgumentException(
                "User does not exist in this company.");

        var displayName = request.DisplayName.Trim();
        if (displayName.Length == 0)
        {
            throw new ArgumentException("Display name is required.");
        }

        if (user.Id == actorUserId && !request.IsActive)
        {
            throw new InvalidOperationException(
                "You cannot deactivate your own account.");
        }

        if (user.Role == UserRole.Owner &&
            (request.Role != UserRole.Owner || !request.IsActive))
        {
            var otherActiveOwners = await db.Users.CountAsync(
                x =>
                    x.CompanyId == companyId &&
                    x.Id != user.Id &&
                    x.IsActive &&
                    x.Role == UserRole.Owner,
                cancellationToken);

            if (otherActiveOwners == 0)
            {
                throw new InvalidOperationException(
                    "The company must keep at least one active Owner.");
            }
        }

        user.DisplayName = displayName;
        user.Role = request.Role;
        user.IsActive = request.IsActive;

        if (!user.IsActive)
        {
            await RevokeUserSessionsAsync(
                user.Id,
                "UserDeactivated",
                cancellationToken);
        }

        var actor = await db.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(
                x => x.Id == actorUserId,
                cancellationToken);

        AddSecurityAudit(
            companyId,
            actorUserId,
            "USER_UPDATE",
            "SUCCESS",
            actor?.Username,
            "TargetUser=" + user.Username +
            "; Role=" + user.Role +
            "; Active=" + user.IsActive);
        await db.SaveChangesAsync(cancellationToken);
        return ToAdminView(user);
    }

    public async Task ResetUserPasswordAsync(
        Guid companyId,
        Guid userId,
        ResetUserPasswordRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request.NewPassword.Length < 10)
        {
            throw new ArgumentException(
                "Password must be at least 10 characters.");
        }

        var user = await db.Users.FirstOrDefaultAsync(
            x => x.Id == userId && x.CompanyId == companyId,
            cancellationToken)
            ?? throw new ArgumentException(
                "User does not exist in this company.");

        user.PasswordHash = passwordHasher.HashPassword(
            user,
            request.NewPassword);

        await RevokeUserSessionsAsync(
            user.Id,
            "PasswordReset",
            cancellationToken);

        AddSecurityAudit(
            companyId,
            user.Id,
            "PASSWORD_RESET",
            "SUCCESS",
            user.Username,
            "All active sessions revoked.");
        await db.SaveChangesAsync(cancellationToken);
    }

    private async Task RevokeUserSessionsAsync(
        Guid userId,
        string reason,
        CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var sessions = await db.AuthSessions
            .Where(x => x.UserId == userId && x.RevokedAt == null)
            .ToArrayAsync(cancellationToken);

        foreach (var session in sessions)
        {
            session.RevokedAt = now;
            session.RevokeReason = reason;
        }
    }

    private static AdminUserView ToAdminView(AppUser user) =>
        new(
            user.Id,
            user.Username,
            user.DisplayName,
            user.Role,
            user.IsActive,
            user.MfaEnabled,
            user.CreatedAt);

    private void AddSecurityAudit(
        Guid? companyId,
        Guid? userId,
        string eventType,
        string outcome,
        string? username,
        string? details)
    {
        db.SecurityAuditLogs.Add(new SecurityAuditLog
        {
            CompanyId = companyId,
            UserId = userId,
            EventType = eventType,
            Outcome = outcome,
            Username = username,
            Details = details
        });
    }

    private async Task RevokeExpiredSessionsAsync(
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var active = await db.AuthSessions
            .Where(x => x.RevokedAt == null)
            .ToArrayAsync(cancellationToken);

        var expired = active
            .Where(x => x.RefreshExpiresAt <= now)
            .ToArray();

        foreach (var session in expired)
        {
            session.RevokedAt = now;
            session.RevokeReason = "RefreshExpired";
        }
    }

    private static LoginResponse ToLoginResponse(
        AppUser user,
        TokenPair pair)
    {
        return new LoginResponse(
            pair.AccessToken,
            pair.AccessExpiresAt,
            user.Id,
            user.CompanyId,
            user.DisplayName,
            user.Role.ToString(),
            user.MfaEnabled,
            pair.RefreshToken,
            pair.RefreshExpiresAt);
    }

    private static TokenPair CreateTokenPair(
        DateTimeOffset now)
    {
        return new TokenPair(
            NewToken(),
            now.Add(AccessLifetime),
            NewToken(),
            now.Add(RefreshLifetime));
    }

    private static string NewToken() =>
        Convert.ToHexString(
            RandomNumberGenerator.GetBytes(32));

    private static string HashToken(string token) =>
        Convert.ToHexString(
            SHA256.HashData(
                Encoding.UTF8.GetBytes(token)));

    private sealed record TokenPair(
        string AccessToken,
        DateTimeOffset AccessExpiresAt,
        string RefreshToken,
        DateTimeOffset RefreshExpiresAt);
}
