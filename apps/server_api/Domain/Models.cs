namespace ERPAccounting.Api.Domain;

public enum UserRole
{
    Owner,
    Administrator,
    FinancialManager,
    Accountant,
    InventoryManager,
    Sales,
    Viewer
}

public enum DeviceTrustState
{
    Pending,
    Trusted,
    Revoked
}

public sealed class Company
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public required string Name { get; set; }
    public string? LegalName { get; set; }
    public string? TaxId { get; set; }
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
}

public sealed class AppUser
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public required Guid CompanyId { get; init; }
    public required string Username { get; init; }
    public required string DisplayName { get; set; }
    public required string PasswordHash { get; set; }
    public UserRole Role { get; set; } = UserRole.Viewer;
    public bool MfaEnabled { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
}

public sealed class TrustedDevice
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public required Guid CompanyId { get; init; }
    public required Guid UserId { get; init; }
    public required string DeviceName { get; set; }
    public required string Platform { get; init; }
    public string? PublicKeyFingerprint { get; set; }
    public DeviceTrustState TrustState { get; set; } = DeviceTrustState.Pending;
    public DateTimeOffset FirstSeenAt { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? LastSeenAt { get; set; }
    public DateTimeOffset? RevokedAt { get; set; }
}
