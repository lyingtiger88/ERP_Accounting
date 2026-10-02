namespace ERPAccounting.Api.Domain;

public sealed class CurrencyDefinition
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public required Guid CompanyId { get; set; }
    public required string Code { get; set; }
    public required string Name { get; set; }
    public string? Symbol { get; set; }
    public int DecimalPlaces { get; set; } = 2;
    public bool IsBase { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class CurrencyExchangeRate
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public required Guid CompanyId { get; set; }
    public required Guid CurrencyId { get; set; }
    public required DateOnly RateDate { get; set; }
    public required string Source { get; set; }
    public decimal BuyRate { get; set; }
    public decimal SellRate { get; set; }
    public decimal AccountingRate { get; set; }
    public required Guid CreatedByUserId { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class JournalLineCurrency
{
    public required Guid JournalLineId { get; set; }
    public required Guid CurrencyId { get; set; }
    public decimal ForeignDebit { get; set; }
    public decimal ForeignCredit { get; set; }
    public decimal ExchangeRate { get; set; }
}
