using ERPAccounting.Api.Accounting;
using ERPAccounting.Api.Contracts;
using ERPAccounting.Api.Domain;
using ERPAccounting.Api.Infrastructure;
using ERPAccounting.Api.Security;
using Microsoft.AspNetCore.Identity;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSingleton<InMemoryStore>();
builder.Services.AddSingleton<PasswordHasher<AppUser>>();
builder.Services.AddSingleton<AuthService>();
builder.Services.AddSingleton<AccountingService>();
builder.Services.AddProblemDetails();

var app = builder.Build();

app.UseExceptionHandler();

app.MapGet("/", () => Results.Ok(new
{
    product = "ERP Accounting",
    phase = "Phase 1 Foundation",
    status = "ok",
    utc = DateTimeOffset.UtcNow
}));

app.MapGet("/health", () => Results.Ok(new
{
    status = "healthy",
    utc = DateTimeOffset.UtcNow
}));

var auth = app.MapGroup("/api/auth");

auth.MapPost("/bootstrap", (
    BootstrapRequest request,
    AuthService authService,
    AccountingService accountingService) =>
{
    try
    {
        var created = authService.Bootstrap(request);
        accountingService.SeedDefaultAccounts(created.Company.Id);

        return Results.Created($"/api/companies/{created.Company.Id}", new
        {
            companyId = created.Company.Id,
            userId = created.User.Id,
            created.User.Username,
            role = created.User.Role.ToString()
        });
    }
    catch (ArgumentException ex)
    {
        return Results.BadRequest(new { error = ex.Message });
    }
    catch (InvalidOperationException ex)
    {
        return Results.Conflict(new { error = ex.Message });
    }
});

auth.MapPost("/login", (LoginRequest request, AuthService authService) =>
{
    var result = authService.Login(request);
    return result is null
        ? Results.Unauthorized()
        : Results.Ok(result);
});

app.MapGet("/api/me", (HttpRequest request, AuthService authService) =>
{
    var user = CurrentUser(request, authService);
    return user is null
        ? Results.Unauthorized()
        : Results.Ok(new
        {
            user.Id,
            user.CompanyId,
            user.Username,
            user.DisplayName,
            role = user.Role.ToString(),
            user.MfaEnabled
        });
});

var accounting = app.MapGroup("/api/accounting");

accounting.MapGet("/accounts", (
    HttpRequest request,
    AuthService authService,
    AccountingService accountingService) =>
{
    var user = CurrentUser(request, authService);
    return user is null
        ? Results.Unauthorized()
        : Results.Ok(accountingService.GetAccounts(user.CompanyId));
});

accounting.MapPost("/accounts", (
    HttpRequest request,
    CreateAccountRequest payload,
    AuthService authService,
    AccountingService accountingService) =>
{
    var user = CurrentUser(request, authService);
    if (user is null)
    {
        return Results.Unauthorized();
    }

    if (!CanWriteAccounting(user))
    {
        return Results.Forbid();
    }

    try
    {
        var account = accountingService.CreateAccount(user.CompanyId, payload);
        return Results.Created($"/api/accounting/accounts/{account.Id}", account);
    }
    catch (ArgumentException ex)
    {
        return Results.BadRequest(new { error = ex.Message });
    }
    catch (InvalidOperationException ex)
    {
        return Results.Conflict(new { error = ex.Message });
    }
});

accounting.MapGet("/journals", (
    HttpRequest request,
    AuthService authService,
    AccountingService accountingService) =>
{
    var user = CurrentUser(request, authService);
    return user is null
        ? Results.Unauthorized()
        : Results.Ok(accountingService.GetJournalEntries(user.CompanyId));
});

accounting.MapPost("/journals", (
    HttpRequest request,
    CreateJournalRequest payload,
    AuthService authService,
    AccountingService accountingService) =>
{
    var user = CurrentUser(request, authService);
    if (user is null)
    {
        return Results.Unauthorized();
    }

    if (!CanWriteAccounting(user))
    {
        return Results.Forbid();
    }

    try
    {
        var journal = accountingService.PostJournal(
            user.CompanyId,
            user.Id,
            payload);

        return Results.Created($"/api/accounting/journals/{journal.Id}", journal);
    }
    catch (ArgumentException ex)
    {
        return Results.BadRequest(new { error = ex.Message });
    }
    catch (InvalidOperationException ex)
    {
        return Results.Conflict(new { error = ex.Message });
    }
});

app.Run();

static AppUser? CurrentUser(HttpRequest request, AuthService authService)
{
    var rawHeader = request.Headers.Authorization.ToString();
    var token = rawHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)
        ? rawHeader["Bearer ".Length..].Trim()
        : null;

    return authService.Resolve(token);
}

static bool CanWriteAccounting(AppUser user)
{
    return user.Role is
        UserRole.Owner or
        UserRole.Administrator or
        UserRole.FinancialManager or
        UserRole.Accountant;
}
