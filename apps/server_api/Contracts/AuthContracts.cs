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
    string Platform);

public sealed record LoginResponse(
    string AccessToken,
    DateTimeOffset ExpiresAt,
    Guid UserId,
    Guid CompanyId,
    string DisplayName,
    string Role,
    bool MfaRequired);

public sealed record DeviceResponse(
    Guid Id,
    string DeviceName,
    string Platform,
    string TrustState,
    DateTimeOffset FirstSeenAt,
    DateTimeOffset? LastSeenAt);
