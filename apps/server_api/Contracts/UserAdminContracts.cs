using ERPAccounting.Api.Domain;

namespace ERPAccounting.Api.Contracts;

public sealed record AdminUserView(
    Guid Id,
    string Username,
    string DisplayName,
    UserRole Role,
    bool IsActive,
    bool MfaEnabled,
    DateTimeOffset CreatedAt);

public sealed record CreateUserRequest(
    string Username,
    string DisplayName,
    string Password,
    UserRole Role);

public sealed record UpdateUserRequest(
    string DisplayName,
    UserRole Role,
    bool IsActive);

public sealed record ResetUserPasswordRequest(
    string NewPassword);


public sealed record SecurityAuditView(
    Guid Id,
    Guid? UserId,
    string EventType,
    string Outcome,
    string? Username,
    string? Details,
    DateTimeOffset CreatedAt);
