using ERPAccounting.Api.Contracts;
using ERPAccounting.Api.Domain;
using ERPAccounting.Api.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace ERPAccounting.Api.Accounting;

public sealed class CurrencyAccountingService(
    AppDbContext db,
    AccountingService accountingService)
{
    private static readonly (string Code, string Name, string? Symbol, int Decimals)[] Defaults =
    [
        ("IRR", "ریال ایران", "﷼", 0),
        ("USD", "دلار آمریکا", "$", 2),
        ("EUR", "یورو", "€", 2),
        ("GBP", "پوند بریتانیا", "£", 2),
        ("AED", "درهم امارات", "د.إ", 2),
        ("TRY", "لیر ترکیه", "₺", 2),
        ("CNY", "یوان چین", "¥", 2),
        ("CHF", "فرانک سوئیس", "CHF", 2),
        ("CAD", "دلار کانادا", "C$", 2),
        ("JPY", "ین ژاپن", "¥", 0)
    ];

    public async Task EnsureDefaultsAsync(
        Guid companyId,
        CancellationToken cancellationToken = default)
    {
        var existing = await db.Currencies
            .Where(x => x.CompanyId == companyId)
            .ToArrayAsync(cancellationToken);

        var byCode = existing.ToDictionary(
            x => x.Code,
            StringComparer.OrdinalIgnoreCase);

        foreach (var item in Defaults)
        {
            if (byCode.ContainsKey(item.Code))
            {
                continue;
            }

            var currency = new CurrencyDefinition
            {
                CompanyId = companyId,
                Code = item.Code,
                Name = item.Name,
                Symbol = item.Symbol,
                DecimalPlaces = item.Decimals,
                IsBase = item.Code == "IRR" && existing.All(x => !x.IsBase),
                IsActive = true
            };

            db.Currencies.Add(currency);
            byCode[item.Code] = currency;
        }

        if (!byCode.Values.Any(x => x.IsBase))
        {
            var irr = byCode.GetValueOrDefault("IRR")
                ?? throw new InvalidOperationException(
                    "Default IRR currency could not be created.");

            irr.IsBase = true;
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<CurrencyView>> GetCurrenciesAsync(
        Guid companyId,
        CancellationToken cancellationToken = default)
    {
        return await db.Currencies
            .AsNoTracking()
            .Where(x => x.CompanyId == companyId)
            .OrderByDescending(x => x.IsBase)
            .ThenBy(x => x.Code)
            .Select(x => new CurrencyView(
                x.Id,
                x.Code,
                x.Name,
                x.Symbol,
                x.DecimalPlaces,
                x.IsBase,
                x.IsActive))
            .ToArrayAsync(cancellationToken);
    }

    public async Task<CurrencyDefinition> CreateCurrencyAsync(
        Guid companyId,
        Guid userId,
        CreateCurrencyRequest request,
        CancellationToken cancellationToken = default)
    {
        var code = NormalizeCode(request.Code);
        var name = request.Name.Trim();

        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Currency name is required.");
        }

        if (request.DecimalPlaces is < 0 or > 8)
        {
            throw new ArgumentException(
                "Currency decimal places must be between 0 and 8.");
        }

        var exists = await db.Currencies.AnyAsync(
            x => x.CompanyId == companyId && x.Code == code,
            cancellationToken);

        if (exists)
        {
            throw new InvalidOperationException(
                $"Currency '{code}' already exists.");
        }

        var hasBase = await db.Currencies.AnyAsync(
            x => x.CompanyId == companyId && x.IsBase,
            cancellationToken);

        var currency = new CurrencyDefinition
        {
            CompanyId = companyId,
            Code = code,
            Name = name,
            Symbol = NullIfBlank(request.Symbol),
            DecimalPlaces = request.DecimalPlaces,
            IsBase = !hasBase
        };

        db.Currencies.Add(currency);
        AddAudit(
            companyId,
            userId,
            "Currency",
            currency.Id,
            "CURRENCY_CREATE",
            new
            {
                currency.Code,
                currency.Name,
                currency.IsBase
            });

        await db.SaveChangesAsync(cancellationToken);
        return currency;
    }

    public async Task<CurrencyDefinition> SetCurrencyStateAsync(
        Guid companyId,
        Guid userId,
        Guid currencyId,
        bool isActive,
        CancellationToken cancellationToken = default)
    {
        var currency = await FindCurrencyAsync(
            companyId,
            currencyId,
            cancellationToken);

        if (currency.IsBase && !isActive)
        {
            throw new InvalidOperationException(
                "The base currency cannot be deactivated.");
        }

        currency.IsActive = isActive;

        AddAudit(
            companyId,
            userId,
            "Currency",
            currency.Id,
            isActive ? "CURRENCY_ACTIVATE" : "CURRENCY_DEACTIVATE",
            new { currency.Code });

        await db.SaveChangesAsync(cancellationToken);
        return currency;
    }

    public async Task<CurrencyDefinition> SetBaseCurrencyAsync(
        Guid companyId,
        Guid userId,
        Guid currencyId,
        CancellationToken cancellationToken = default)
    {
        var target = await FindCurrencyAsync(
            companyId,
            currencyId,
            cancellationToken);

        if (!target.IsActive)
        {
            throw new InvalidOperationException(
                "Inactive currency cannot be selected as base currency.");
        }

        if (target.IsBase)
        {
            return target;
        }

        var hasAccountingHistory = await db.JournalEntries.AnyAsync(
            x => x.CompanyId == companyId,
            cancellationToken);

        if (hasAccountingHistory)
        {
            throw new InvalidOperationException(
                "Base currency cannot be changed after accounting journal history exists.");
        }

        var currencies = await db.Currencies
            .Where(x => x.CompanyId == companyId)
            .ToArrayAsync(cancellationToken);

        foreach (var currency in currencies)
        {
            currency.IsBase = currency.Id == target.Id;
        }

        AddAudit(
            companyId,
            userId,
            "Currency",
            target.Id,
            "BASE_CURRENCY_SET",
            new { target.Code });

        await db.SaveChangesAsync(cancellationToken);
        return target;
    }

    public async Task<IReadOnlyList<CurrencyRateView>> GetRatesAsync(
        Guid companyId,
        Guid? currencyId = null,
        DateOnly? from = null,
        DateOnly? to = null,
        int limit = 300,
        CancellationToken cancellationToken = default)
    {
        var take = Math.Clamp(limit, 1, 1000);

        var query = db.CurrencyExchangeRates
            .AsNoTracking()
            .Where(x => x.CompanyId == companyId);

        if (currencyId is Guid id)
        {
            query = query.Where(x => x.CurrencyId == id);
        }

        if (from is DateOnly fromDate)
        {
            query = query.Where(x => x.RateDate >= fromDate);
        }

        if (to is DateOnly toDate)
        {
            query = query.Where(x => x.RateDate <= toDate);
        }

        var rates = await query
            .OrderByDescending(x => x.RateDate)
            .ThenByDescending(x => x.CreatedAt)
            .Take(take)
            .ToArrayAsync(cancellationToken);

        var currencyIds = rates
            .Select(x => x.CurrencyId)
            .Distinct()
            .ToArray();

        var codes = currencyIds.Length == 0
            ? new Dictionary<Guid, string>()
            : await db.Currencies
                .AsNoTracking()
                .Where(x =>
                    x.CompanyId == companyId &&
                    currencyIds.Contains(x.Id))
                .ToDictionaryAsync(
                    x => x.Id,
                    x => x.Code,
                    cancellationToken);

        return rates
            .Select(x => new CurrencyRateView(
                x.Id,
                x.CurrencyId,
                codes.GetValueOrDefault(x.CurrencyId, "?"),
                x.RateDate,
                x.BuyRate,
                x.SellRate,
                x.AccountingRate,
                x.Source,
                x.CreatedAt))
            .ToArray();
    }

    public async Task<CurrencyRateView> SaveRateAsync(
        Guid companyId,
        Guid userId,
        CreateCurrencyRateRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request.BuyRate <= 0 ||
            request.SellRate <= 0 ||
            request.AccountingRate <= 0)
        {
            throw new ArgumentException(
                "Buy, sell and accounting rates must be positive.");
        }

        var currency = await FindCurrencyAsync(
            companyId,
            request.CurrencyId,
            cancellationToken);

        if (!currency.IsActive)
        {
            throw new InvalidOperationException(
                "Cannot add a rate to an inactive currency.");
        }

        var source = NullIfBlank(request.Source) ?? "Manual";

        if (currency.IsBase &&
            (request.BuyRate != 1m ||
             request.SellRate != 1m ||
             request.AccountingRate != 1m))
        {
            throw new ArgumentException(
                "Base-currency exchange rates must equal 1.");
        }

        var rate = await db.CurrencyExchangeRates
            .FirstOrDefaultAsync(
                x =>
                    x.CompanyId == companyId &&
                    x.CurrencyId == currency.Id &&
                    x.RateDate == request.RateDate &&
                    x.Source == source,
                cancellationToken);

        if (rate is null)
        {
            rate = new CurrencyExchangeRate
            {
                CompanyId = companyId,
                CurrencyId = currency.Id,
                RateDate = request.RateDate,
                Source = source,
                BuyRate = request.BuyRate,
                SellRate = request.SellRate,
                AccountingRate = request.AccountingRate,
                CreatedByUserId = userId
            };

            db.CurrencyExchangeRates.Add(rate);
        }
        else
        {
            rate.BuyRate = request.BuyRate;
            rate.SellRate = request.SellRate;
            rate.AccountingRate = request.AccountingRate;
            rate.CreatedByUserId = userId;
            rate.CreatedAt = DateTimeOffset.UtcNow;
        }

        AddAudit(
            companyId,
            userId,
            "Currency",
            currency.Id,
            "CURRENCY_RATE_SET",
            new
            {
                currency.Code,
                rate.RateDate,
                rate.BuyRate,
                rate.SellRate,
                rate.AccountingRate,
                rate.Source
            });

        await db.SaveChangesAsync(cancellationToken);

        return new CurrencyRateView(
            rate.Id,
            currency.Id,
            currency.Code,
            rate.RateDate,
            rate.BuyRate,
            rate.SellRate,
            rate.AccountingRate,
            rate.Source,
            rate.CreatedAt);
    }

    public async Task<ForeignCurrencyJournalResponse> CreateForeignCurrencyJournalAsync(
        Guid companyId,
        Guid userId,
        CreateForeignCurrencyJournalRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request.Lines.Count < 2)
        {
            throw new ArgumentException(
                "Foreign-currency journal must contain at least two lines.");
        }

        if (request.Lines.Any(x =>
            x.ForeignDebit < 0 ||
            x.ForeignCredit < 0 ||
            (x.ForeignDebit > 0 && x.ForeignCredit > 0) ||
            (x.ForeignDebit == 0 && x.ForeignCredit == 0)))
        {
            throw new ArgumentException(
                "Each foreign-currency line must contain either debit or credit.");
        }

        var foreignDebit = request.Lines.Sum(x => x.ForeignDebit);
        var foreignCredit = request.Lines.Sum(x => x.ForeignCredit);

        if (foreignDebit <= 0 || foreignDebit != foreignCredit)
        {
            throw new ArgumentException(
                "Foreign-currency journal is not balanced.");
        }

        var currency = await FindCurrencyAsync(
            companyId,
            request.CurrencyId,
            cancellationToken);

        if (!currency.IsActive)
        {
            throw new InvalidOperationException(
                "Selected currency is inactive.");
        }

        var rate = request.ExchangeRate
            ?? await ResolveAccountingRateAsync(
                companyId,
                currency,
                request.DocumentDate,
                cancellationToken);

        if (rate <= 0)
        {
            throw new ArgumentException(
                "Exchange rate must be positive.");
        }

        if (currency.IsBase && rate != 1m)
        {
            throw new ArgumentException(
                "Base-currency exchange rate must equal 1.");
        }

        var journalLines = request.Lines
            .Select(line => new CreateJournalLineRequest(
                line.AccountId,
                line.Description,
                ConvertToBase(line.ForeignDebit, rate),
                ConvertToBase(line.ForeignCredit, rate),
                line.DetailAccountId,
                line.CostCenterId,
                line.ProjectId,
                currency.Id,
                line.ForeignDebit,
                line.ForeignCredit,
                rate))
            .ToArray();

        var baseDebit = journalLines.Sum(x => x.Debit);
        var baseCredit = journalLines.Sum(x => x.Credit);

        if (baseDebit != baseCredit)
        {
            throw new InvalidOperationException(
                "Converted base-currency journal has a rounding difference. Adjust foreign lines or rate.");
        }

        var entry = await accountingService.PostJournalAsync(
            companyId,
            userId,
            new CreateJournalRequest(
                null,
                request.DocumentDate,
                request.Description,
                journalLines,
                request.FiscalYearId),
            cancellationToken);

        return new ForeignCurrencyJournalResponse(
            entry.Id,
            entry.Number,
            currency.Id,
            currency.Code,
            rate,
            foreignDebit,
            foreignCredit,
            baseDebit,
            baseCredit);
    }

    public async Task<CurrencyPositionReport> GetCurrencyPositionAsync(
        Guid companyId,
        DateOnly asOf,
        CancellationToken cancellationToken = default)
    {
        var currencies = await db.Currencies
            .AsNoTracking()
            .Where(x => x.CompanyId == companyId)
            .ToArrayAsync(cancellationToken);

        var baseCurrency = currencies.FirstOrDefault(x => x.IsBase)
            ?? throw new InvalidOperationException(
                "Company has no base currency.");

        var nonBaseIds = currencies
            .Where(x => !x.IsBase)
            .Select(x => x.Id)
            .ToArray();

        if (nonBaseIds.Length == 0)
        {
            return new CurrencyPositionReport(
                asOf,
                baseCurrency.Code,
                []);
        }

        var currencyLines = await db.JournalLineCurrencies
            .AsNoTracking()
            .Where(x => nonBaseIds.Contains(x.CurrencyId))
            .ToArrayAsync(cancellationToken);

        if (currencyLines.Length == 0)
        {
            return new CurrencyPositionReport(
                asOf,
                baseCurrency.Code,
                []);
        }

        var lineIds = currencyLines
            .Select(x => x.JournalLineId)
            .ToArray();

        var journalLines = await db.JournalLines
            .AsNoTracking()
            .Where(x => lineIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, cancellationToken);

        var accountIds = journalLines.Values
            .Select(x => x.AccountId)
            .Distinct()
            .ToArray();

        var accounts = await db.Accounts
            .AsNoTracking()
            .Where(x =>
                x.CompanyId == companyId &&
                accountIds.Contains(x.Id) &&
                (x.Type == AccountType.Asset ||
                 x.Type == AccountType.Liability))
            .ToDictionaryAsync(x => x.Id, cancellationToken);

        var journalIds = journalLines.Values
            .Select(x => x.JournalEntryId)
            .Distinct()
            .ToArray();

        var eligibleJournalIds = await db.JournalEntries
            .AsNoTracking()
            .Where(x =>
                x.CompanyId == companyId &&
                x.Status == JournalStatus.Posted &&
                x.DocumentDate <= asOf &&
                journalIds.Contains(x.Id))
            .Select(x => x.Id)
            .ToHashSetAsync(cancellationToken);

        var currencyLookup = currencies.ToDictionary(x => x.Id);
        var grouped = currencyLines
            .Where(x =>
                journalLines.TryGetValue(
                    x.JournalLineId,
                    out var line) &&
                eligibleJournalIds.Contains(line.JournalEntryId) &&
                accounts.ContainsKey(line.AccountId))
            .GroupBy(x => new
            {
                x.CurrencyId,
                AccountId = journalLines[x.JournalLineId].AccountId
            })
            .OrderBy(x => currencyLookup[x.Key.CurrencyId].Code)
            .ThenBy(x => accounts[x.Key.AccountId].Code)
            .ToArray();

        var rows = new List<CurrencyPositionRow>();

        foreach (var group in grouped)
        {
            var currency = currencyLookup[group.Key.CurrencyId];
            var account = accounts[group.Key.AccountId];

            var foreignBalance = group.Sum(
                x => x.ForeignDebit - x.ForeignCredit);

            var historicalBase = group.Sum(x =>
            {
                var line = journalLines[x.JournalLineId];
                return line.Debit - line.Credit;
            });

            var currentRate = await ResolveAccountingRateAsync(
                companyId,
                currency,
                asOf,
                cancellationToken);

            var revalued = ConvertToBase(
                foreignBalance,
                currentRate);

            rows.Add(new CurrencyPositionRow(
                currency.Id,
                currency.Code,
                currency.Name,
                account.Id,
                account.Code,
                account.Name,
                foreignBalance,
                historicalBase,
                currentRate,
                revalued,
                revalued - historicalBase));
        }

        return new CurrencyPositionReport(
            asOf,
            baseCurrency.Code,
            rows);
    }

    public async Task<decimal> ResolveAccountingRateAsync(
        Guid companyId,
        Guid currencyId,
        DateOnly date,
        CancellationToken cancellationToken = default)
    {
        var currency = await FindCurrencyAsync(
            companyId,
            currencyId,
            cancellationToken);

        return await ResolveAccountingRateAsync(
            companyId,
            currency,
            date,
            cancellationToken);
    }

    private async Task<decimal> ResolveAccountingRateAsync(
        Guid companyId,
        CurrencyDefinition currency,
        DateOnly date,
        CancellationToken cancellationToken)
    {
        if (currency.IsBase)
        {
            return 1m;
        }

        var rate = await db.CurrencyExchangeRates
            .AsNoTracking()
            .Where(x =>
                x.CompanyId == companyId &&
                x.CurrencyId == currency.Id &&
                x.RateDate <= date)
            .OrderByDescending(x => x.RateDate)
            .ThenByDescending(x => x.CreatedAt)
            .Select(x => (decimal?)x.AccountingRate)
            .FirstOrDefaultAsync(cancellationToken);

        return rate
            ?? throw new InvalidOperationException(
                $"No accounting exchange rate is available for {currency.Code} on or before {date:yyyy-MM-dd}.");
    }

    private async Task<CurrencyDefinition> FindCurrencyAsync(
        Guid companyId,
        Guid currencyId,
        CancellationToken cancellationToken)
    {
        return await db.Currencies.FirstOrDefaultAsync(
            x =>
                x.Id == currencyId &&
                x.CompanyId == companyId,
            cancellationToken)
            ?? throw new ArgumentException(
                "Currency does not exist in this company.");
    }

    private void AddAudit(
        Guid companyId,
        Guid userId,
        string entityType,
        Guid entityId,
        string action,
        object payload)
    {
        db.AccountingAuditLogs.Add(new AccountingAuditLog
        {
            CompanyId = companyId,
            UserId = userId,
            EntityType = entityType,
            EntityId = entityId,
            Action = action,
            PayloadJson = System.Text.Json.JsonSerializer.Serialize(payload)
        });
    }

    private static decimal ConvertToBase(
        decimal foreignAmount,
        decimal rate)
    {
        return Math.Round(
            foreignAmount * rate,
            4,
            MidpointRounding.AwayFromZero);
    }

    private static string NormalizeCode(string value)
    {
        var code = value.Trim().ToUpperInvariant();

        if (code.Length is < 3 or > 12 ||
            code.Any(ch => !char.IsLetterOrDigit(ch)))
        {
            throw new ArgumentException(
                "Currency code must contain 3 to 12 letters or digits.");
        }

        return code;
    }

    private static string? NullIfBlank(string? value)
    {
        var trimmed = value?.Trim();
        return string.IsNullOrWhiteSpace(trimmed)
            ? null
            : trimmed;
    }
}
