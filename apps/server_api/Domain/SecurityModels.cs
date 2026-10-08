namespace ERPAccounting.Api.Domain;

public sealed class AuthSession
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public required Guid CompanyId { get; set; }
    public required Guid UserId { get; set; }
    public Guid? DeviceId { get; set; }
    public required string AccessTokenHash { get; set; }
    public required string RefreshTokenHash { get; set; }
    public required DateTimeOffset AccessExpiresAt { get; set; }
    public required DateTimeOffset RefreshExpiresAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset LastRefreshedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? RevokedAt { get; set; }
    public string? RevokeReason { get; set; }
}
