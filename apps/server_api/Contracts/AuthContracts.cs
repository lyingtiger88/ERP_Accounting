namespace ERPAccounting.Api.Contracts;

public sealed record BootstrapRequest(
    string CompanyName,
    string Username,
    string DisplayName,
    string Password);

public sealed record LoginRequest(
    string Username,
    string Password,
    string DeviceName,
    string Platform,
    string? TotpCode = null,
    string? PairingCode = null,
    string? InstallationId = null);

public sealed record LoginResponse(
    string AccessToken,
    DateTimeOffset ExpiresAt,
    Guid UserId,
    Guid CompanyId,
    string DisplayName,
    string Role,
    bool MfaRequired,
    string RefreshToken,
    DateTimeOffset RefreshExpiresAt);

public sealed record RefreshSessionRequest(
    string RefreshToken);

public sealed record LogoutRequest(
    string? RefreshToken = null);

public sealed record DevicePairingCodeResponse(string Code, DateTimeOffset ExpiresAt);

public sealed record DeviceResponse(
    Guid Id,
    string DeviceName,
    string Platform,
    string TrustState,
    DateTimeOffset FirstSeenAt,
    DateTimeOffset? LastSeenAt,
    bool RequiresRePairing = false);

public sealed record MfaCodeRequest(string Code);
public sealed record MfaDisableRequest(string Password, string Code);
public sealed record MfaSetupResponse(string Secret, string OtpauthUri);
public sealed record MfaEnableResponse(bool Enabled, IReadOnlyList<string> RecoveryCodes);

public sealed record SessionResponse(
    Guid Id,
    string? DeviceName,
    string? Platform,
    DateTimeOffset CreatedAt,
    DateTimeOffset LastRefreshedAt,
    DateTimeOffset AccessExpiresAt,
    DateTimeOffset RefreshExpiresAt,
    bool IsCurrent = false);
