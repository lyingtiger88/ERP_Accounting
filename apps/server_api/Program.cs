using ERPAccounting.Api.Accounting;
using ERPAccounting.Api.Contracts;
using ERPAccounting.Api.Domain;
using ERPAccounting.Api.Infrastructure;
using ERPAccounting.Api.Security;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

var databaseProvider = builder.Configuration["Database:Provider"]?.Trim()
    ?? "Sqlite";

builder.Services.AddDbContext<AppDbContext>(options =>
{
    if (databaseProvider.Equals("Postgres", StringComparison.OrdinalIgnoreCase) ||
        databaseProvider.Equals("PostgreSQL", StringComparison.OrdinalIgnoreCase))
    {
        var postgresConnection = builder.Configuration.GetConnectionString("Postgres")
            ?? throw new InvalidOperationException(
                "ConnectionStrings:Postgres is required when Database:Provider=Postgres.");

        options.UseNpgsql(postgresConnection);
        return;
    }

    if (databaseProvider.Equals("Sqlite", StringComparison.OrdinalIgnoreCase))
    {
        var configured = builder.Configuration.GetConnectionString("Sqlite");
        string sqliteConnection;

        if (!string.IsNullOrWhiteSpace(configured))
        {
            sqliteConnection = configured;
        }
        else
        {
            var dataDirectory = Path.Combine(
                builder.Environment.ContentRootPath,
                "data");

            Directory.CreateDirectory(dataDirectory);

            var databasePath = Path.Combine(
                dataDirectory,
                "erp_accounting.db");

            sqliteConnection = $"Data Source={databasePath}";
        }

        options.UseSqlite(sqliteConnection);
        return;
    }

    throw new InvalidOperationException(
        $"Unsupported Database:Provider '{databaseProvider}'. Use Sqlite or Postgres.");
});

builder.Services.AddSingleton<PasswordHasher<AppUser>>();
builder.Services.AddSingleton<SessionStore>();
builder.Services.AddScoped<AuthService>();
builder.Services.AddScoped<AccountingService>();
builder.Services.AddProblemDetails();

var app = builder.Build();

app.UseExceptionHandler();

await using (var scope = app.Services.CreateAsyncScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    await db.Database.EnsureCreatedAsync();
}

app.MapGet("/", () => Results.Ok(new
{
    product = "ERP Accounting",
    phase = "Phase 1 Foundation",
    persistence = databaseProvider,
    status = "ok",
    utc = DateTimeOffset.UtcNow
}));

app.MapGet("/health", async (
    AppDbContext db,
    CancellationToken cancellationToken) =>
{
    var database = await db.Database.CanConnectAsync(cancellationToken);

    return Results.Ok(new
    {
        status = database ? "healthy" : "degraded",
        database,
        provider = databaseProvider,
        utc = DateTimeOffset.UtcNow
    });
});

var auth = app.MapGroup("/api/auth");

auth.MapPost("/bootstrap", async (
    BootstrapRequest request,
    AuthService authService,
    AccountingService accountingService,
    CancellationToken cancellationToken) =>
{
    try
    {
        var created = await authService.BootstrapAsync(
            request,
            cancellationToken);

        await accountingService.SeedDefaultAccountsAsync(
            created.Company.Id,
            cancellationToken);

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

auth.MapPost("/login", async (
    LoginRequest request,
    AuthService authService,
    CancellationToken cancellationToken) =>
{
    var result = await authService.LoginAsync(
        request,
        cancellationToken);

    return result is null
        ? Results.Unauthorized()
        : Results.Ok(result);
});

app.MapGet("/api/me", async (
    HttpRequest request,
    AuthService authService,
    CancellationToken cancellationToken) =>
{
    var user = await CurrentUserAsync(
        request,
        authService,
        cancellationToken);

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

accounting.MapGet("/accounts", async (
    HttpRequest request,
    AuthService authService,
    AccountingService accountingService,
    CancellationToken cancellationToken) =>
{
    var user = await CurrentUserAsync(
        request,
        authService,
        cancellationToken);

    return user is null
        ? Results.Unauthorized()
        : Results.Ok(await accountingService.GetAccountsAsync(
            user.CompanyId,
            cancellationToken));
});

accounting.MapPost("/accounts", async (
    HttpRequest request,
    CreateAccountRequest payload,
    AuthService authService,
    AccountingService accountingService,
    CancellationToken cancellationToken) =>
{
    var user = await CurrentUserAsync(
        request,
        authService,
        cancellationToken);

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
        var account = await accountingService.CreateAccountAsync(
            user.CompanyId,
            payload,
            cancellationToken);

        return Results.Created(
            $"/api/accounting/accounts/{account.Id}",
            account);
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

accounting.MapGet("/journals", async (
    HttpRequest request,
    AuthService authService,
    AccountingService accountingService,
    CancellationToken cancellationToken) =>
{
    var user = await CurrentUserAsync(
        request,
        authService,
        cancellationToken);

    return user is null
        ? Results.Unauthorized()
        : Results.Ok(await accountingService.GetJournalEntriesAsync(
            user.CompanyId,
            cancellationToken));
});

accounting.MapPost("/journals", async (
    HttpRequest request,
    CreateJournalRequest payload,
    AuthService authService,
    AccountingService accountingService,
    CancellationToken cancellationToken) =>
{
    var user = await CurrentUserAsync(
        request,
        authService,
        cancellationToken);

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
        var journal = await accountingService.PostJournalAsync(
            user.CompanyId,
            user.Id,
            payload,
            cancellationToken);

        return Results.Created(
            $"/api/accounting/journals/{journal.Id}",
            journal);
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

accounting.MapGet("/trial-balance", async (
    HttpRequest request,
    AuthService authService,
    AccountingService accountingService,
    CancellationToken cancellationToken) =>
{
    var user = await CurrentUserAsync(
        request,
        authService,
        cancellationToken);

    return user is null
        ? Results.Unauthorized()
        : Results.Ok(await accountingService.GetTrialBalanceAsync(
            user.CompanyId,
            cancellationToken));
});

accounting.MapGet("/general-ledger", async (
    HttpRequest request,
    Guid? accountId,
    AuthService authService,
    AccountingService accountingService,
    CancellationToken cancellationToken) =>
{
    var user = await CurrentUserAsync(
        request,
        authService,
        cancellationToken);

    return user is null
        ? Results.Unauthorized()
        : Results.Ok(await accountingService.GetGeneralLedgerAsync(
            user.CompanyId,
            accountId,
            cancellationToken));
});

app.Run();

static async Task<AppUser?> CurrentUserAsync(
    HttpRequest request,
    AuthService authService,
    CancellationToken cancellationToken)
{
    var rawHeader = request.Headers.Authorization.ToString();
    var token = rawHeader.StartsWith(
        "Bearer ",
        StringComparison.OrdinalIgnoreCase)
        ? rawHeader["Bearer ".Length..].Trim()
        : null;

    return await authService.ResolveAsync(
        token,
        cancellationToken);
}

static bool CanWriteAccounting(AppUser user)
{
    return user.Role is
        UserRole.Owner or
        UserRole.Administrator or
        UserRole.FinancialManager or
        UserRole.Accountant;
}
