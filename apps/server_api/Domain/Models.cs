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
    public Guid Id { get; set; } = Guid.NewGuid();
    public required string Name { get; set; }
    public string? LegalName { get; set; }
    public string? TaxId { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class AppUser
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public required Guid CompanyId { get; set; }
    public required string Username { get; set; }
    public required string DisplayName { get; set; }
    public required string PasswordHash { get; set; }
    public UserRole Role { get; set; } = UserRole.Viewer;
    public bool MfaEnabled { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class TrustedDevice
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public required Guid CompanyId { get; set; }
    public required Guid UserId { get; set; }
    public required string DeviceName { get; set; }
    public required string Platform { get; set; }
    public string? PublicKeyFingerprint { get; set; }
    public DeviceTrustState TrustState { get; set; } = DeviceTrustState.Pending;
    public DateTimeOffset FirstSeenAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? LastSeenAt { get; set; }
    public DateTimeOffset? RevokedAt { get; set; }
}
