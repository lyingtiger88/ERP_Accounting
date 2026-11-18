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
    PasswordHasher<AppUser> passwordHasher,
    MfaService? mfaService = null)
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
        if (string.IsNullOrWhiteSpace(request.Username) ||
            string.IsNullOrWhiteSpace(request.DeviceName) ||
            string.IsNullOrWhiteSpace(request.Platform) ||
            request.DeviceName.Trim().Length > 200 ||
            request.Platform.Trim().Length > 80 ||
            (request.InstallationId is not null &&
             (request.InstallationId.Length != 64 ||
              !request.InstallationId.All(Uri.IsHexDigit))))
        {
            return null;
        }

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

        if (user.MfaEnabled &&
            (mfaService is null ||
             !await mfaService.VerifyLoginCodeAsync(user.Id, request.TotpCode, cancellationToken)))
        {
            AddSecurityAudit(user.CompanyId, user.Id, "MFA_LOGIN", "DENIED",
                user.Username, "Missing or invalid TOTP/recovery code.");
            await db.SaveChangesAsync(cancellationToken);
            return null;
        }

        var now = DateTimeOffset.UtcNow;
        DevicePairingChallenge? pairingChallenge = null;
        if (!string.IsNullOrWhiteSpace(request.PairingCode))
        {
            var codeHash = HashToken(request.PairingCode.Trim().ToUpperInvariant());
            // SQLite does not translate DateTimeOffset ordering reliably.
            // The code hash is unique, so fetch at most one candidate and
            // validate its expiry in memory before the atomic one-time consume.
            pairingChallenge = await db.DevicePairingChallenges
                .FirstOrDefaultAsync(x =>
                    x.CompanyId == user.CompanyId &&
                    x.UserId == user.Id &&
                    x.CodeHash == codeHash &&
                    x.UsedAt == null,
                    cancellationToken);

            if (pairingChallenge is null || pairingChallenge.ExpiresAt <= now)
            {
                AddSecurityAudit(user.CompanyId, user.Id, "DEVICE_PAIR", "DENIED",
                    user.Username, "Invalid, expired or already-used pairing code.");
                await db.SaveChangesAsync(cancellationToken);
                return null;
            }

            var consumeAt = DateTimeOffset.UtcNow;
            if (pairingChallenge.ExpiresAt <= consumeAt)
            {
                AddSecurityAudit(user.CompanyId, user.Id, "DEVICE_PAIR", "DENIED",
                    user.Username, "Pairing code expired before it could be consumed.");
                await db.SaveChangesAsync(cancellationToken);
                return null;
            }

            var consumed = await db.DevicePairingChallenges
                .Where(x => x.Id == pairingChallenge.Id && x.UsedAt == null)
                .ExecuteUpdateAsync(
                    setters => setters.SetProperty(x => x.UsedAt, now),
                    cancellationToken);

            if (consumed != 1)
            {
                return null;
            }
        }

        var deviceName = request.DeviceName.Trim();
        var platform = request.Platform.Trim();
        var installationHash = string.IsNullOrWhiteSpace(request.InstallationId)
            ? null : HashToken(request.InstallationId.Trim().ToLowerInvariant());

        // Never inherit trust from a display name. Legacy clients without an
        // installation identifier always receive a separate device record.
        var device = installationHash is null ? null :
            await db.Devices.FirstOrDefaultAsync(x =>
                x.UserId == user.Id && x.InstallationIdHash == installationHash,
                cancellationToken);

        if (device?.TrustState == DeviceTrustState.Revoked && pairingChallenge is null)
        {
            AddSecurityAudit(user.CompanyId, user.Id, "LOGIN", "DENIED",
                user.Username, "Revoked installation requires a new pairing code.");
            await db.SaveChangesAsync(cancellationToken);
            return null;
        }

        if (device is null)
        {
            device = new TrustedDevice
            {
                CompanyId = user.CompanyId,
                UserId = user.Id,
                DeviceName = deviceName,
                Platform = platform,
                InstallationIdHash = installationHash,
                TrustState = pairingChallenge is null
                    ? DeviceTrustState.Pending : DeviceTrustState.Trusted,
                LastSeenAt = now
            };
            db.Devices.Add(device);
        }
        else
        {
            device.DeviceName = deviceName;
            device.Platform = platform;
            device.LastSeenAt = now;
            if (pairingChallenge is not null)
            {
                device.TrustState = DeviceTrustState.Trusted;
                device.RevokedAt = null;
            }
        }

        if (pairingChallenge is not null)
        {
            AddSecurityAudit(user.CompanyId, user.Id, "DEVICE_PAIR", "SUCCESS",
                user.Username, "Device=" + deviceName + "; Platform=" + platform);
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

        if (session.DeviceId is Guid deviceId)
        {
            var device = await db.Devices.FirstOrDefaultAsync(
                x => x.Id == deviceId,
                cancellationToken);

            if (device is null ||
                device.UserId != session.UserId ||
                device.CompanyId != session.CompanyId ||
                device.TrustState == DeviceTrustState.Revoked)
            {
                session.RevokedAt = now;
                session.RevokeReason = "DeviceRevokedOrInvalid";
                await db.SaveChangesAsync(cancellationToken);
                return null;
            }

            device.LastSeenAt = now;
        }

        // Compare-and-swap prevents concurrent requests from rotating the same token.
        var rotated = await db.AuthSessions
            .Where(x => x.Id == session.Id &&
                        x.RefreshTokenHash == refreshHash &&
                        x.RevokedAt == null)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(x => x.AccessTokenHash, HashToken(pair.AccessToken))
                .SetProperty(x => x.RefreshTokenHash, HashToken(pair.RefreshToken))
                .SetProperty(x => x.AccessExpiresAt, pair.AccessExpiresAt)
                .SetProperty(x => x.RefreshExpiresAt, pair.RefreshExpiresAt)
                .SetProperty(x => x.LastRefreshedAt, now), cancellationToken);
        if (rotated != 1)
        {
            return null;
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

        // Defense in depth: a device marked revoked never authorizes a request,
        // even if an older session row has not yet been marked revoked.
        if (session.DeviceId is Guid deviceId &&
            !await db.Devices.AsNoTracking().AnyAsync(
                x => x.Id == deviceId &&
                     x.UserId == session.UserId &&
                     x.CompanyId == session.CompanyId &&
                     x.TrustState != DeviceTrustState.Revoked,
                cancellationToken))
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

    public async Task<DevicePairingCodeResponse> CreateDevicePairingCodeAsync(
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        var user = await db.Users.FirstOrDefaultAsync(
            x => x.Id == userId && x.IsActive,
            cancellationToken)
            ?? throw new InvalidOperationException("Active user session is required.");

        var now = DateTimeOffset.UtcNow;
        await db.DevicePairingChallenges
            .Where(x => x.UserId == userId && x.UsedAt == null)
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(x => x.UsedAt, now),
                cancellationToken);

        var code = Convert.ToHexString(RandomNumberGenerator.GetBytes(8));
        var expiresAt = now.AddMinutes(5);
        db.DevicePairingChallenges.Add(new DevicePairingChallenge
        {
            CompanyId = user.CompanyId,
            UserId = user.Id,
            CodeHash = HashToken(code),
            CreatedAt = now,
            ExpiresAt = expiresAt
        });

        AddSecurityAudit(user.CompanyId, user.Id, "DEVICE_PAIR_CODE", "SUCCESS",
            user.Username, "A one-time device pairing code was issued.");
        await db.SaveChangesAsync(cancellationToken);
        return new DevicePairingCodeResponse(code, expiresAt);
    }

    public async Task<IReadOnlyList<SessionResponse>> GetActiveSessionsAsync(
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        var now = DateTimeOffset.UtcNow;
        await RevokeExpiredSessionsAsync(now, cancellationToken);

        return await db.AuthSessions
            .AsNoTracking()
            .Where(x => x.UserId == userId && x.RevokedAt == null)
            .OrderByDescending(x => x.LastRefreshedAt)
            .Select(x => new SessionResponse(
                x.Id,
                x.DeviceId == null
                    ? null
                    : db.Devices
                        .Where(d => d.Id == x.DeviceId.Value)
                        .Select(d => d.DeviceName)
                        .FirstOrDefault(),
                x.DeviceId == null
                    ? null
                    : db.Devices
                        .Where(d => d.Id == x.DeviceId.Value)
                        .Select(d => d.Platform)
                        .FirstOrDefault(),
                x.CreatedAt,
                x.LastRefreshedAt,
                x.AccessExpiresAt,
                x.RefreshExpiresAt))
            .ToArrayAsync(cancellationToken);
    }

    public async Task<int> RevokeAllSessionsAsync(
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        var now = DateTimeOffset.UtcNow;
        var sessions = await db.AuthSessions
            .Where(x => x.UserId == userId && x.RevokedAt == null)
            .ToArrayAsync(cancellationToken);

        foreach (var session in sessions)
        {
            session.RevokedAt = now;
            session.RevokeReason = "RevokeAllSessions";
        }

        if (sessions.Length == 0)
        {
            return 0;
        }

        AddSecurityAudit(
            sessions[0].CompanyId,
            userId,
            "REVOKE_ALL_SESSIONS",
            "SUCCESS",
            null,
            $"All active sessions revoked. Count={sessions.Length}");
        await db.SaveChangesAsync(cancellationToken);
        return sessions.Length;
    }

    public async Task<IReadOnlyList<DeviceResponse>> GetDevicesAsync(
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        var devices = await db.Devices
            .AsNoTracking()
            .Where(x => x.UserId == userId)
            .ToArrayAsync(cancellationToken);

        return devices
            .OrderByDescending(x => x.LastSeenAt)
            .ThenByDescending(x => x.FirstSeenAt)
            .Select(x => new DeviceResponse(
                x.Id,
                x.DeviceName,
                x.Platform,
                x.TrustState.ToString(),
                x.FirstSeenAt,
                x.LastSeenAt,
                x.InstallationIdHash is null))
            .ToArray();
    }

    public async Task<bool> DeleteDeviceAsync(
        Guid userId,
        Guid deviceId,
        CancellationToken cancellationToken = default)
    {
        var device = await db.Devices.FirstOrDefaultAsync(
            x => x.Id == deviceId && x.UserId == userId,
            cancellationToken);
        if (device is null) return false;

        var sessions = await db.AuthSessions
            .Where(x => x.UserId == userId && x.DeviceId == deviceId)
            .ToArrayAsync(cancellationToken);
        var now = DateTimeOffset.UtcNow;

        foreach (var session in sessions)
        {
            if (session.RevokedAt is null)
            {
                session.RevokedAt = now;
                session.RevokeReason = "DeviceDeleted";
            }
            // Preserve session/audit history without retaining a FK to a
            // device record that the owner explicitly removed.
            session.DeviceId = null;
        }

        var user = await db.Users.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == userId, cancellationToken);

        AddSecurityAudit(
            device.CompanyId,
            userId,
            "DEVICE_DELETE",
            "SUCCESS",
            user?.Username,
            "Device=" + device.DeviceName + "; Platform=" + device.Platform +
            "; RelatedSessions=" + sessions.Length);

        db.Devices.Remove(device);
        await db.SaveChangesAsync(cancellationToken);
        return true;
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
            false,
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
