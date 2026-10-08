namespace ERPAccounting.Api.Domain;

public sealed class SecurityAuditLog
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid? CompanyId { get; set; }
    public Guid? UserId { get; set; }
    public required string EventType { get; set; }
    public required string Outcome { get; set; }
    public string? Username { get; set; }
    public string? Details { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
