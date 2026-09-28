using System.Security.Cryptography;
using ERPAccounting.Api.Contracts;
using ERPAccounting.Api.Domain;
using ERPAccounting.Api.Infrastructure;
using Microsoft.AspNetCore.Identity;

namespace ERPAccounting.Api.Security;

public sealed class AuthService(InMemoryStore store, PasswordHasher<AppUser> passwordHasher)
{
    private static readonly TimeSpan SessionLifetime = TimeSpan.FromMinutes(30);
    private readonly Dictionary<string, SessionRecord> _sessions = new(StringComparer.Ordinal);

    public (Company Company, AppUser User) Bootstrap(BootstrapRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.CompanyName) ||
            string.IsNullOrWhiteSpace(request.Username) ||
            string.IsNullOrWhiteSpace(request.DisplayName) ||
            request.Password.Length < 10)
        {
            throw new ArgumentException("Company, username and display name are required; password must be at least 10 characters.");
        }

        lock (store.Gate)
        {
            if (store.Users.Values.Any(x =>
                string.Equals(x.Username, request.Username.Trim(), StringComparison.OrdinalIgnoreCase)))
            {
                throw new InvalidOperationException("Username already exists.");
            }

            var company = new Company { Name = request.CompanyName.Trim() };
            var user = new AppUser
            {
                CompanyId = company.Id,
                Username = request.Username.Trim(),
                DisplayName = request.DisplayName.Trim(),
                PasswordHash = string.Empty,
                Role = UserRole.Owner
            };

            user.PasswordHash = passwordHasher.HashPassword(user, request.Password);
            store.Companies[company.Id] = company;
            store.Users[user.Id] = user;

            return (company, user);
        }
    }

    public LoginResponse? Login(LoginRequest request)
    {
        AppUser? user;
        lock (store.Gate)
        {
            user = store.Users.Values.FirstOrDefault(x =>
                x.IsActive &&
                string.Equals(x.Username, request.Username.Trim(), StringComparison.OrdinalIgnoreCase));
        }

        if (user is null)
        {
            return null;
        }

        var verified = passwordHasher.VerifyHashedPassword(user, user.PasswordHash, request.Password);
        if (verified == PasswordVerificationResult.Failed)
        {
            return null;
        }

        var now = DateTimeOffset.UtcNow;
        var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        var expiresAt = now.Add(SessionLifetime);

        lock (store.Gate)
        {
            var device = store.Devices.Values.FirstOrDefault(x =>
                x.UserId == user.Id &&
                x.TrustState != DeviceTrustState.Revoked &&
                string.Equals(x.DeviceName, request.DeviceName.Trim(), StringComparison.OrdinalIgnoreCase) &&
                string.Equals(x.Platform, request.Platform.Trim(), StringComparison.OrdinalIgnoreCase));

            if (device is null)
            {
                device = new TrustedDevice
                {
                    CompanyId = user.CompanyId,
                    UserId = user.Id,
                    DeviceName = request.DeviceName.Trim(),
                    Platform = request.Platform.Trim(),
                    TrustState = DeviceTrustState.Pending,
                    LastSeenAt = now
                };
                store.Devices[device.Id] = device;
            }
            else
            {
                device.LastSeenAt = now;
            }

            _sessions[token] = new SessionRecord(user.Id, expiresAt);
        }

        return new LoginResponse(
            token,
            expiresAt,
            user.Id,
            user.CompanyId,
            user.DisplayName,
            user.Role.ToString(),
            user.MfaEnabled);
    }

    public AppUser? Resolve(string? bearerToken)
    {
        if (string.IsNullOrWhiteSpace(bearerToken))
        {
            return null;
        }

        lock (store.Gate)
        {
            if (!_sessions.TryGetValue(bearerToken, out var session) ||
                session.ExpiresAt <= DateTimeOffset.UtcNow)
            {
                _sessions.Remove(bearerToken);
                return null;
            }

            return store.Users.GetValueOrDefault(session.UserId);
        }
    }

    private sealed record SessionRecord(Guid UserId, DateTimeOffset ExpiresAt);
}
