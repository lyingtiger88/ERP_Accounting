using ERPAccounting.Api.Accounting;
using ERPAccounting.Api.Contracts;
using ERPAccounting.Api.Domain;
using ERPAccounting.Api.Infrastructure;
using ERPAccounting.Api.Security;
using ERPAccounting.Api.Reporting;
using ERPAccounting.Api.SalesInventory;
using ERPAccounting.Api.Treasury;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using System.Text.Json.Serialization;

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
builder.Services.AddScoped<AuthService>();
builder.Services.AddScoped<AccountingService>();
builder.Services.AddScoped<CurrencyAccountingService>();
builder.Services.AddScoped<SalesInventoryService>();
builder.Services.AddScoped<TreasuryService>();
builder.Services.AddScoped<ReportingService>();
builder.Services.AddProblemDetails();
builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.Converters.Add(
        new JsonStringEnumConverter());
});

var app = builder.Build();

app.UseExceptionHandler();

await using (var scope = app.Services.CreateAsyncScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    await db.Database.EnsureCreatedAsync();
    await AccountingSchemaBootstrapper.EnsureExtensionsAsync(db);
    await CurrencySchemaBootstrapper.EnsureAsync(db);
    await SalesInventorySchemaBootstrapper.EnsureAsync(db);
    await TreasurySchemaBootstrapper.EnsureAsync(db);
    await SecuritySchemaBootstrapper.EnsureAsync(db);
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
    CurrencyAccountingService currencyService,
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
        await accountingService.EnsureDefaultFiscalYearAsync(
            created.Company.Id,
            cancellationToken);
        await currencyService.EnsureDefaultsAsync(
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
    AccountingService accountingService,
    CurrencyAccountingService currencyService,
    CancellationToken cancellationToken) =>
{
    var result = await authService.LoginAsync(
        request,
        cancellationToken);

    if (result is null)
    {
        return Results.Unauthorized();
    }

    await accountingService.SeedDefaultAccountsAsync(
        result.CompanyId,
        cancellationToken);
    await accountingService.EnsureDefaultFiscalYearAsync(
        result.CompanyId,
        cancellationToken);
    await currencyService.EnsureDefaultsAsync(
        result.CompanyId,
        cancellationToken);

    return Results.Ok(result);
});

auth.MapPost("/refresh", async (
    RefreshSessionRequest request,
    AuthService authService,
    CancellationToken cancellationToken) =>
{
    var result = await authService.RefreshAsync(
        request,
        cancellationToken);

    return result is null
        ? Results.Unauthorized()
        : Results.Ok(result);
});

auth.MapPost("/logout", async (
    HttpRequest request,
    LogoutRequest payload,
    AuthService authService,
    CancellationToken cancellationToken) =>
{
    var rawHeader = request.Headers.Authorization.ToString();
    var accessToken = rawHeader.StartsWith(
        "Bearer ",
        StringComparison.OrdinalIgnoreCase)
        ? rawHeader["Bearer ".Length..].Trim()
        : null;

    var revoked = await authService.RevokeAsync(
        accessToken,
        payload.RefreshToken,
        cancellationToken);

    return Results.Ok(new { revoked });
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

var admin = app.MapGroup("/api/admin");

admin.MapGet("/users", async (
    HttpRequest request,
    AuthService authService,
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

    if (!CanManageUsers(user))
    {
        return Results.Forbid();
    }

    return Results.Ok(await authService.GetUsersAsync(
        user.CompanyId,
        cancellationToken));
});

admin.MapPost("/users", async (
    HttpRequest request,
    CreateUserRequest payload,
    AuthService authService,
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

    if (!CanManageUsers(user))
    {
        return Results.Forbid();
    }

    try
    {
        return Results.Ok(await authService.CreateUserAsync(
            user.CompanyId,
            payload,
            cancellationToken));
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

admin.MapPut("/users/{userId:guid}", async (
    HttpRequest request,
    Guid userId,
    UpdateUserRequest payload,
    AuthService authService,
    CancellationToken cancellationToken) =>
{
    var actor = await CurrentUserAsync(
        request,
        authService,
        cancellationToken);

    if (actor is null)
    {
        return Results.Unauthorized();
    }

    if (!CanManageUsers(actor))
    {
        return Results.Forbid();
    }

    try
    {
        return Results.Ok(await authService.UpdateUserAsync(
            actor.CompanyId,
            actor.Id,
            userId,
            payload,
            cancellationToken));
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

admin.MapPost("/users/{userId:guid}/reset-password", async (
    HttpRequest request,
    Guid userId,
    ResetUserPasswordRequest payload,
    AuthService authService,
    CancellationToken cancellationToken) =>
{
    var actor = await CurrentUserAsync(
        request,
        authService,
        cancellationToken);

    if (actor is null)
    {
        return Results.Unauthorized();
    }

    if (!CanManageUsers(actor))
    {
        return Results.Forbid();
    }

    try
    {
        await authService.ResetUserPasswordAsync(
            actor.CompanyId,
            userId,
            payload,
            cancellationToken);
        return Results.Ok(new { reset = true });
    }
    catch (ArgumentException ex)
    {
        return Results.BadRequest(new { error = ex.Message });
    }
});

var store = app.MapGroup("/api/sales-inventory");

store.MapPost("/defaults/ensure", async (
    HttpRequest request,
    AuthService authService,
    SalesInventoryService storeService,
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

    try
    {
        await storeService.EnsureDefaultsAsync(
            user.CompanyId,
            cancellationToken);

        return Results.Ok(new { ready = true });
    }
    catch (InvalidOperationException ex)
    {
        return Results.Conflict(new { error = ex.Message });
    }
});

store.MapGet("/products", async (
    HttpRequest request,
    AuthService authService,
    SalesInventoryService storeService,
    CancellationToken cancellationToken) =>
{
    var user = await CurrentUserAsync(
        request,
        authService,
        cancellationToken);

    return user is null
        ? Results.Unauthorized()
        : Results.Ok(await storeService.GetProductsAsync(
            user.CompanyId,
            cancellationToken));
});

store.MapPost("/products", async (
    HttpRequest request,
    CreateProductRequest payload,
    AuthService authService,
    SalesInventoryService storeService,
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

    if (!CanWriteSalesInventory(user))
    {
        return Results.Forbid();
    }

    try
    {
        return Results.Ok(await storeService.CreateProductAsync(
            user.CompanyId,
            payload,
            cancellationToken));
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

store.MapPut("/products/{productId:guid}", async (
    HttpRequest request,
    Guid productId,
    UpdateProductRequest payload,
    AuthService authService,
    SalesInventoryService storeService,
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

    if (!CanWriteSalesInventory(user))
    {
        return Results.Forbid();
    }

    try
    {
        return Results.Ok(await storeService.UpdateProductAsync(
            user.CompanyId,
            productId,
            payload,
            cancellationToken));
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

store.MapGet("/warehouses", async (
    HttpRequest request,
    AuthService authService,
    SalesInventoryService storeService,
    CancellationToken cancellationToken) =>
{
    var user = await CurrentUserAsync(
        request,
        authService,
        cancellationToken);

    return user is null
        ? Results.Unauthorized()
        : Results.Ok(await storeService.GetWarehousesAsync(
            user.CompanyId,
            cancellationToken));
});

store.MapPost("/warehouses", async (
    HttpRequest request,
    CreateWarehouseRequest payload,
    AuthService authService,
    SalesInventoryService storeService,
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

    if (!CanWriteSalesInventory(user))
    {
        return Results.Forbid();
    }

    try
    {
        return Results.Ok(await storeService.CreateWarehouseAsync(
            user.CompanyId,
            payload,
            cancellationToken));
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

store.MapPut("/warehouses/{warehouseId:guid}", async (
    HttpRequest request,
    Guid warehouseId,
    UpdateWarehouseRequest payload,
    AuthService authService,
    SalesInventoryService storeService,
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

    if (!CanWriteSalesInventory(user))
    {
        return Results.Forbid();
    }

    try
    {
        return Results.Ok(await storeService.UpdateWarehouseAsync(
            user.CompanyId,
            warehouseId,
            payload,
            cancellationToken));
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

store.MapGet("/settings", async (
    HttpRequest request,
    AuthService authService,
    SalesInventoryService storeService,
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

    try
    {
        return Results.Ok(await storeService.GetSettingsAsync(
            user.CompanyId,
            cancellationToken));
    }
    catch (InvalidOperationException ex)
    {
        return Results.Conflict(new { error = ex.Message });
    }
});

store.MapPut("/settings", async (
    HttpRequest request,
    UpdateSalesInventorySettingsRequest payload,
    AuthService authService,
    SalesInventoryService storeService,
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

    if (!CanManageSalesInventorySettings(user))
    {
        return Results.Forbid();
    }

    try
    {
        return Results.Ok(await storeService.UpdateSettingsAsync(
            user.CompanyId,
            payload,
            cancellationToken));
    }
    catch (ArgumentException ex)
    {
        return Results.BadRequest(new { error = ex.Message });
    }
});

store.MapGet("/stock", async (
    HttpRequest request,
    Guid? warehouseId,
    AuthService authService,
    SalesInventoryService storeService,
    CancellationToken cancellationToken) =>
{
    var user = await CurrentUserAsync(
        request,
        authService,
        cancellationToken);

    return user is null
        ? Results.Unauthorized()
        : Results.Ok(await storeService.GetStockBalancesAsync(
            user.CompanyId,
            warehouseId,
            cancellationToken));
});

store.MapPost("/stock/adjust", async (
    HttpRequest request,
    CreateStockAdjustmentRequest payload,
    AuthService authService,
    SalesInventoryService storeService,
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

    if (!CanWriteInventory(user))
    {
        return Results.Forbid();
    }

    try
    {
        return Results.Ok(await storeService.AdjustStockAsync(
            user.CompanyId,
            user.Id,
            payload,
            cancellationToken));
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

store.MapGet("/invoices", async (
    HttpRequest request,
    AuthService authService,
    SalesInventoryService storeService,
    CancellationToken cancellationToken) =>
{
    var user = await CurrentUserAsync(
        request,
        authService,
        cancellationToken);

    return user is null
        ? Results.Unauthorized()
        : Results.Ok(await storeService.GetSalesInvoicesAsync(
            user.CompanyId,
            cancellationToken));
});

store.MapPost("/invoices", async (
    HttpRequest request,
    CreateSalesInvoiceRequest payload,
    AuthService authService,
    SalesInventoryService storeService,
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

    if (!CanWriteSales(user))
    {
        return Results.Forbid();
    }

    try
    {
        return Results.Ok(await storeService.CreateSalesInvoiceAsync(
            user.CompanyId,
            user.Id,
            payload,
            cancellationToken));
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

store.MapPost("/invoices/{invoiceId:guid}/post", async (
    HttpRequest request,
    Guid invoiceId,
    AuthService authService,
    SalesInventoryService storeService,
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

    if (!CanWriteSales(user))
    {
        return Results.Forbid();
    }

    try
    {
        return Results.Ok(await storeService.PostSalesInvoiceAsync(
            user.CompanyId,
            user.Id,
            invoiceId,
            cancellationToken));
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


store.MapGet("/products/lookup", async (
    HttpRequest request,
    string code,
    AuthService authService,
    SalesInventoryService storeService,
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

    var product = await storeService.FindProductAsync(
        user.CompanyId,
        code,
        cancellationToken);

    return product is null
        ? Results.NotFound(new { error = "Product was not found." })
        : Results.Ok(product);
});

store.MapGet("/stock/low", async (
    HttpRequest request,
    AuthService authService,
    SalesInventoryService storeService,
    CancellationToken cancellationToken) =>
{
    var user = await CurrentUserAsync(
        request,
        authService,
        cancellationToken);

    return user is null
        ? Results.Unauthorized()
        : Results.Ok(await storeService.GetLowStockAlertsAsync(
            user.CompanyId,
            cancellationToken));
});

store.MapGet("/stock/trace", async (
    HttpRequest request,
    Guid? warehouseId,
    Guid? productId,
    AuthService authService,
    SalesInventoryService storeService,
    CancellationToken cancellationToken) =>
{
    var user = await CurrentUserAsync(
        request,
        authService,
        cancellationToken);

    return user is null
        ? Results.Unauthorized()
        : Results.Ok(await storeService.GetStockTraceBalancesAsync(
            user.CompanyId,
            warehouseId,
            productId,
            cancellationToken));
});

store.MapGet("/purchase-orders", async (
    HttpRequest request,
    AuthService authService,
    SalesInventoryService storeService,
    CancellationToken cancellationToken) =>
{
    var user = await CurrentUserAsync(
        request,
        authService,
        cancellationToken);

    return user is null
        ? Results.Unauthorized()
        : Results.Ok(await storeService.GetPurchaseOrdersAsync(
            user.CompanyId,
            cancellationToken));
});

store.MapPost("/purchase-orders", async (
    HttpRequest request,
    CreatePurchaseOrderRequest payload,
    AuthService authService,
    SalesInventoryService storeService,
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

    if (!CanWriteSalesInventory(user))
    {
        return Results.Forbid();
    }

    try
    {
        return Results.Ok(await storeService.CreatePurchaseOrderAsync(
            user.CompanyId,
            user.Id,
            payload,
            cancellationToken));
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

store.MapPost("/purchase-orders/{orderId:guid}/status", async (
    HttpRequest request,
    Guid orderId,
    SetPurchaseOrderStatusRequest payload,
    AuthService authService,
    SalesInventoryService storeService,
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

    if (!CanWriteSalesInventory(user))
    {
        return Results.Forbid();
    }

    try
    {
        return Results.Ok(await storeService.SetPurchaseOrderStatusAsync(
            user.CompanyId,
            user.Id,
            orderId,
            payload.Status,
            cancellationToken));
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

store.MapGet("/purchases", async (
    HttpRequest request,
    AuthService authService,
    SalesInventoryService storeService,
    CancellationToken cancellationToken) =>
{
    var user = await CurrentUserAsync(
        request,
        authService,
        cancellationToken);

    return user is null
        ? Results.Unauthorized()
        : Results.Ok(await storeService.GetPurchaseReceiptsAsync(
            user.CompanyId,
            cancellationToken));
});

store.MapPost("/purchases", async (
    HttpRequest request,
    CreatePurchaseReceiptRequest payload,
    AuthService authService,
    SalesInventoryService storeService,
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

    if (!CanWriteSalesInventory(user))
    {
        return Results.Forbid();
    }

    try
    {
        return Results.Ok(await storeService.CreatePurchaseReceiptAsync(
            user.CompanyId,
            user.Id,
            payload,
            cancellationToken));
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

store.MapPost("/purchases/{receiptId:guid}/post", async (
    HttpRequest request,
    Guid receiptId,
    AuthService authService,
    SalesInventoryService storeService,
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

    if (!CanWriteSalesInventory(user))
    {
        return Results.Forbid();
    }

    try
    {
        return Results.Ok(await storeService.PostPurchaseReceiptAsync(
            user.CompanyId,
            user.Id,
            receiptId,
            cancellationToken));
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

store.MapGet("/purchase-returns", async (
    HttpRequest request,
    AuthService authService,
    SalesInventoryService storeService,
    CancellationToken cancellationToken) =>
{
    var user = await CurrentUserAsync(
        request,
        authService,
        cancellationToken);

    return user is null
        ? Results.Unauthorized()
        : Results.Ok(await storeService.GetPurchaseReturnsAsync(
            user.CompanyId,
            cancellationToken));
});

store.MapPost("/purchases/{receiptId:guid}/returns", async (
    HttpRequest request,
    Guid receiptId,
    CreatePurchaseReturnRequest payload,
    AuthService authService,
    SalesInventoryService storeService,
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

    if (!CanWriteSalesInventory(user))
    {
        return Results.Forbid();
    }

    try
    {
        return Results.Ok(await storeService.CreatePurchaseReturnAsync(
            user.CompanyId,
            user.Id,
            receiptId,
            payload,
            cancellationToken));
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

store.MapGet("/transfers", async (
    HttpRequest request,
    AuthService authService,
    SalesInventoryService storeService,
    CancellationToken cancellationToken) =>
{
    var user = await CurrentUserAsync(
        request,
        authService,
        cancellationToken);

    return user is null
        ? Results.Unauthorized()
        : Results.Ok(await storeService.GetWarehouseTransfersAsync(
            user.CompanyId,
            cancellationToken));
});

store.MapPost("/transfers", async (
    HttpRequest request,
    CreateWarehouseTransferRequest payload,
    AuthService authService,
    SalesInventoryService storeService,
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

    if (!CanWriteInventory(user))
    {
        return Results.Forbid();
    }

    try
    {
        return Results.Ok(await storeService.CreateWarehouseTransferAsync(
            user.CompanyId,
            user.Id,
            payload,
            cancellationToken));
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

store.MapPost("/transfers/{transferId:guid}/post", async (
    HttpRequest request,
    Guid transferId,
    AuthService authService,
    SalesInventoryService storeService,
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

    if (!CanWriteInventory(user))
    {
        return Results.Forbid();
    }

    try
    {
        return Results.Ok(await storeService.PostWarehouseTransferAsync(
            user.CompanyId,
            user.Id,
            transferId,
            cancellationToken));
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

store.MapGet("/returns", async (
    HttpRequest request,
    AuthService authService,
    SalesInventoryService storeService,
    CancellationToken cancellationToken) =>
{
    var user = await CurrentUserAsync(
        request,
        authService,
        cancellationToken);

    return user is null
        ? Results.Unauthorized()
        : Results.Ok(await storeService.GetSalesReturnsAsync(
            user.CompanyId,
            cancellationToken));
});

store.MapPost("/invoices/{invoiceId:guid}/returns", async (
    HttpRequest request,
    Guid invoiceId,
    CreateSalesReturnRequest payload,
    AuthService authService,
    SalesInventoryService storeService,
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

    if (!CanWriteSales(user))
    {
        return Results.Forbid();
    }

    try
    {
        return Results.Ok(await storeService.CreateSalesReturnAsync(
            user.CompanyId,
            user.Id,
            invoiceId,
            payload,
            cancellationToken));
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


var reports = app.MapGroup("/api/reports");

reports.MapGet("/center", async (
    HttpRequest request,
    DateOnly? from,
    DateOnly? to,
    Guid? warehouseId,
    Guid? productId,
    Guid? detailAccountId,
    AuthService authService,
    ReportingService reportingService,
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

    try
    {
        return Results.Ok(await reportingService.GetReportsCenterAsync(
            user.CompanyId,
            from,
            to,
            warehouseId,
            productId,
            detailAccountId,
            cancellationToken));
    }
    catch (ArgumentException ex)
    {
        return Results.BadRequest(new { error = ex.Message });
    }
});

reports.MapGet("/center.csv", async (
    HttpRequest request,
    DateOnly? from,
    DateOnly? to,
    Guid? warehouseId,
    Guid? productId,
    Guid? detailAccountId,
    AuthService authService,
    ReportingService reportingService,
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

    try
    {
        var report = await reportingService.GetReportsCenterAsync(
            user.CompanyId,
            from,
            to,
            warehouseId,
            productId,
            detailAccountId,
            cancellationToken);

        var fileName = from.HasValue || to.HasValue
            ? $"reports-center-{from?.ToString("yyyyMMdd") ?? "start"}-{to?.ToString("yyyyMMdd") ?? "today"}.csv"
            : "reports-center.csv";

        return Results.File(
            ReportsCenterCsvExporter.Export(report),
            "text/csv; charset=utf-8",
            fileName);
    }
    catch (ArgumentException ex)
    {
        return Results.BadRequest(new { error = ex.Message });
    }
});

var treasury = app.MapGroup("/api/treasury");

treasury.MapGet("/accounts", async (
    HttpRequest request,
    AuthService authService,
    TreasuryService treasuryService,
    CancellationToken cancellationToken) =>
{
    var user = await CurrentUserAsync(
        request,
        authService,
        cancellationToken);

    return user is null
        ? Results.Unauthorized()
        : Results.Ok(await treasuryService.GetAccountsAsync(
            user.CompanyId,
            cancellationToken));
});

treasury.MapPost("/accounts", async (
    HttpRequest request,
    CreateTreasuryAccountRequest payload,
    AuthService authService,
    TreasuryService treasuryService,
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
        return Results.Ok(await treasuryService.CreateAccountAsync(
            user.CompanyId,
            payload,
            cancellationToken));
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

treasury.MapPut("/accounts/{accountId:guid}", async (
    HttpRequest request,
    Guid accountId,
    UpdateTreasuryAccountRequest payload,
    AuthService authService,
    TreasuryService treasuryService,
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
        return Results.Ok(await treasuryService.UpdateAccountAsync(
            user.CompanyId,
            accountId,
            payload,
            cancellationToken));
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

treasury.MapGet("/transactions", async (
    HttpRequest request,
    DateOnly? from,
    DateOnly? to,
    AuthService authService,
    TreasuryService treasuryService,
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

    try
    {
        return Results.Ok(await treasuryService.GetTransactionsAsync(
            user.CompanyId,
            from,
            to,
            cancellationToken));
    }
    catch (ArgumentException ex)
    {
        return Results.BadRequest(new { error = ex.Message });
    }
});

treasury.MapPost("/transactions", async (
    HttpRequest request,
    CreateTreasuryTransactionRequest payload,
    AuthService authService,
    TreasuryService treasuryService,
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
        return Results.Ok(await treasuryService.PostTransactionAsync(
            user.CompanyId,
            user.Id,
            payload,
            cancellationToken));
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

var accounting = app.MapGroup("/api/accounting");

accounting.MapGet("/currencies", async (
    HttpRequest request,
    AuthService authService,
    CurrencyAccountingService currencyService,
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

    await currencyService.EnsureDefaultsAsync(
        user.CompanyId,
        cancellationToken);

    return Results.Ok(await currencyService.GetCurrenciesAsync(
        user.CompanyId,
        cancellationToken));
});

accounting.MapPost("/currencies", async (
    HttpRequest request,
    CreateCurrencyRequest payload,
    AuthService authService,
    CurrencyAccountingService currencyService,
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
        return Results.Ok(await currencyService.CreateCurrencyAsync(
            user.CompanyId,
            user.Id,
            payload,
            cancellationToken));
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

accounting.MapPost("/currencies/{currencyId:guid}/state", async (
    HttpRequest request,
    Guid currencyId,
    SetCurrencyStateRequest payload,
    AuthService authService,
    CurrencyAccountingService currencyService,
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
        return Results.Ok(await currencyService.SetCurrencyStateAsync(
            user.CompanyId,
            user.Id,
            currencyId,
            payload.IsActive,
            cancellationToken));
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

accounting.MapPost("/currencies/base", async (
    HttpRequest request,
    SetBaseCurrencyRequest payload,
    AuthService authService,
    CurrencyAccountingService currencyService,
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
        return Results.Ok(await currencyService.SetBaseCurrencyAsync(
            user.CompanyId,
            user.Id,
            payload.CurrencyId,
            cancellationToken));
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

accounting.MapGet("/currency-rates", async (
    HttpRequest request,
    Guid? currencyId,
    DateOnly? from,
    DateOnly? to,
    int? limit,
    AuthService authService,
    CurrencyAccountingService currencyService,
    CancellationToken cancellationToken) =>
{
    var user = await CurrentUserAsync(
        request,
        authService,
        cancellationToken);

    return user is null
        ? Results.Unauthorized()
        : Results.Ok(await currencyService.GetRatesAsync(
            user.CompanyId,
            currencyId,
            from,
            to,
            limit ?? 300,
            cancellationToken));
});

accounting.MapPost("/currency-rates", async (
    HttpRequest request,
    CreateCurrencyRateRequest payload,
    AuthService authService,
    CurrencyAccountingService currencyService,
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
        return Results.Ok(await currencyService.SaveRateAsync(
            user.CompanyId,
            user.Id,
            payload,
            cancellationToken));
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

accounting.MapGet("/currencies/{currencyId:guid}/accounting-rate", async (
    HttpRequest request,
    Guid currencyId,
    DateOnly? date,
    AuthService authService,
    CurrencyAccountingService currencyService,
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

    try
    {
        var rateDate = date ?? DateOnly.FromDateTime(DateTime.Today);
        var rate = await currencyService.ResolveAccountingRateAsync(
            user.CompanyId,
            currencyId,
            rateDate,
            cancellationToken);

        return Results.Ok(new
        {
            currencyId,
            date = rateDate,
            accountingRate = rate
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

accounting.MapPost("/currency-journals", async (
    HttpRequest request,
    CreateForeignCurrencyJournalRequest payload,
    AuthService authService,
    CurrencyAccountingService currencyService,
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
        return Results.Ok(await currencyService.CreateForeignCurrencyJournalAsync(
            user.CompanyId,
            user.Id,
            payload,
            cancellationToken));
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

accounting.MapGet("/currency-position", async (
    HttpRequest request,
    DateOnly? asOf,
    AuthService authService,
    CurrencyAccountingService currencyService,
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

    try
    {
        return Results.Ok(await currencyService.GetCurrencyPositionAsync(
            user.CompanyId,
            asOf ?? DateOnly.FromDateTime(DateTime.Today),
            cancellationToken));
    }
    catch (InvalidOperationException ex)
    {
        return Results.Conflict(new { error = ex.Message });
    }
});

accounting.MapPost("/currency-revaluation", async (
    HttpRequest request,
    CurrencyRevaluationRequest payload,
    AuthService authService,
    CurrencyAccountingService currencyService,
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
        return Results.Ok(await currencyService.PostRevaluationAsync(
            user.CompanyId,
            user.Id,
            payload,
            cancellationToken));
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

accounting.MapGet("/fiscal-years", async (
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
        : Results.Ok(await accountingService.GetFiscalYearsAsync(
            user.CompanyId,
            cancellationToken));
});

accounting.MapPost("/fiscal-years", async (
    HttpRequest request,
    CreateFiscalYearRequest payload,
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
        var fiscalYear = await accountingService.CreateFiscalYearAsync(
            user.CompanyId,
            payload,
            cancellationToken);

        return Results.Created(
            $"/api/accounting/fiscal-years/{fiscalYear.Id}",
            fiscalYear);
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

accounting.MapGet("/fiscal-years/{fiscalYearId:guid}/periods", async (
    HttpRequest request,
    Guid fiscalYearId,
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

    try
    {
        return Results.Ok(await accountingService.GetFiscalPeriodsAsync(
            user.CompanyId,
            fiscalYearId,
            cancellationToken));
    }
    catch (ArgumentException ex)
    {
        return Results.BadRequest(new { error = ex.Message });
    }
});

accounting.MapPost("/fiscal-years/{fiscalYearId:guid}/periods/ensure-standard", async (
    HttpRequest request,
    Guid fiscalYearId,
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
        return Results.Ok(await accountingService.EnsureStandardFiscalPeriodsAsync(
            user.CompanyId,
            fiscalYearId,
            cancellationToken));
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

accounting.MapPost("/fiscal-periods/{periodId:guid}/state", async (
    HttpRequest request,
    Guid periodId,
    SetFiscalPeriodStateRequest payload,
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
        return Results.Ok(await accountingService.SetFiscalPeriodClosedAsync(
            user.CompanyId,
            user.Id,
            periodId,
            payload.IsClosed,
            cancellationToken));
    }
    catch (ArgumentException ex)
    {
        return Results.BadRequest(new { error = ex.Message });
    }
});

accounting.MapPost("/fiscal-years/{fiscalYearId:guid}/finalize", async (
    HttpRequest request,
    Guid fiscalYearId,
    FinalizeFiscalYearRequest payload,
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
        return Results.Ok(await accountingService.FinalizeFiscalYearAsync(
            user.CompanyId,
            user.Id,
            fiscalYearId,
            payload,
            cancellationToken));
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

accounting.MapPost("/fiscal-years/{fiscalYearId:guid}/reopen-finalized", async (
    HttpRequest request,
    Guid fiscalYearId,
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
        return Results.Ok(await accountingService.ReopenFinalizedFiscalYearAsync(
            user.CompanyId,
            user.Id,
            fiscalYearId,
            cancellationToken));
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

accounting.MapPost("/fiscal-years/{fiscalYearId:guid}/state", async (
    HttpRequest request,
    Guid fiscalYearId,
    SetFiscalYearStateRequest payload,
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
        return Results.Ok(await accountingService.SetFiscalYearClosedAsync(
            user.CompanyId,
            user.Id,
            fiscalYearId,
            payload.IsClosed,
            cancellationToken));
    }
    catch (ArgumentException ex)
    {
        return Results.BadRequest(new { error = ex.Message });
    }
});

accounting.MapGet("/cost-centers", async (
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
        : Results.Ok(await accountingService.GetCostCentersAsync(
            user.CompanyId,
            cancellationToken));
});

accounting.MapPost("/cost-centers", async (
    HttpRequest request,
    CreateCostCenterRequest payload,
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
        var entity = await accountingService.CreateCostCenterAsync(
            user.CompanyId,
            user.Id,
            payload,
            cancellationToken);

        return Results.Created(
            $"/api/accounting/cost-centers/{entity.Id}",
            entity);
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

accounting.MapPut("/cost-centers/{costCenterId:guid}", async (
    HttpRequest request,
    Guid costCenterId,
    UpdateCostCenterRequest payload,
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
        return Results.Ok(await accountingService.UpdateCostCenterAsync(
            user.CompanyId,
            user.Id,
            costCenterId,
            payload,
            cancellationToken));
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

accounting.MapGet("/projects", async (
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
        : Results.Ok(await accountingService.GetAccountingProjectsAsync(
            user.CompanyId,
            cancellationToken));
});

accounting.MapPost("/projects", async (
    HttpRequest request,
    CreateAccountingProjectRequest payload,
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
        var entity = await accountingService.CreateAccountingProjectAsync(
            user.CompanyId,
            user.Id,
            payload,
            cancellationToken);

        return Results.Created(
            $"/api/accounting/projects/{entity.Id}",
            entity);
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

accounting.MapPut("/projects/{projectId:guid}", async (
    HttpRequest request,
    Guid projectId,
    UpdateAccountingProjectRequest payload,
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
        return Results.Ok(await accountingService.UpdateAccountingProjectAsync(
            user.CompanyId,
            user.Id,
            projectId,
            payload,
            cancellationToken));
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

accounting.MapGet("/detail-accounts", async (
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
        : Results.Ok(await accountingService.GetDetailAccountsAsync(
            user.CompanyId,
            cancellationToken));
});

accounting.MapPost("/detail-accounts", async (
    HttpRequest request,
    CreateDetailAccountRequest payload,
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
        var detail = await accountingService.CreateDetailAccountAsync(
            user.CompanyId,
            payload,
            cancellationToken);

        return Results.Created(
            $"/api/accounting/detail-accounts/{detail.Id}",
            detail);
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

accounting.MapPost("/sync/detail-account", async (
    HttpRequest request,
    SyncDetailAccountRequest payload,
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
        return Results.Ok(await accountingService.SyncDetailAccountAsync(
            user.CompanyId,
            user.Id,
            payload,
            cancellationToken));
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

accounting.MapGet("/sync/detail-accounts", async (
    HttpRequest request,
    long? after,
    int? limit,
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

    try
    {
        return Results.Ok(await accountingService.PullDetailAccountChangesAsync(
            user.CompanyId,
            after ?? 0,
            limit ?? 100,
            cancellationToken));
    }
    catch (ArgumentException ex)
    {
        return Results.BadRequest(new { error = ex.Message });
    }
});

accounting.MapGet("/journals", async (
    HttpRequest request,
    DateOnly? from,
    DateOnly? to,
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

    try
    {
        return Results.Ok(await accountingService.GetJournalEntriesAsync(
            user.CompanyId,
            from,
            to,
            cancellationToken));
    }
    catch (ArgumentException ex)
    {
        return Results.BadRequest(new { error = ex.Message });
    }
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

accounting.MapPost("/sync-journal", async (
    HttpRequest request,
    SyncJournalRequest payload,
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
        return Results.Ok(await accountingService.SyncJournalAsync(
            user.CompanyId,
            user.Id,
            payload,
            cancellationToken));
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

accounting.MapPost("/journals/{journalEntryId:guid}/reverse", async (
    HttpRequest request,
    Guid journalEntryId,
    ReverseJournalRequest payload,
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
        return Results.Ok(await accountingService.ReverseJournalAsync(
            user.CompanyId,
            user.Id,
            journalEntryId,
            payload,
            cancellationToken));
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

accounting.MapGet("/audit", async (
    HttpRequest request,
    Guid? entityId,
    int? limit,
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
        : Results.Ok(await accountingService.GetAuditLogsAsync(
            user.CompanyId,
            entityId,
            limit ?? 100,
            cancellationToken));
});

accounting.MapGet("/sync/journals", async (
    HttpRequest request,
    long? after,
    int? limit,
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

    try
    {
        return Results.Ok(await accountingService.PullJournalChangesAsync(
            user.CompanyId,
            after ?? 0,
            limit ?? 100,
            cancellationToken));
    }
    catch (ArgumentException ex)
    {
        return Results.BadRequest(new { error = ex.Message });
    }
});

accounting.MapGet("/detail-ledger", async (
    HttpRequest request,
    Guid detailAccountId,
    DateOnly? from,
    DateOnly? to,
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

    try
    {
        return Results.Ok(await accountingService.GetDetailLedgerAsync(
            user.CompanyId,
            detailAccountId,
            from,
            to,
            cancellationToken));
    }
    catch (ArgumentException ex)
    {
        return Results.BadRequest(new { error = ex.Message });
    }
});

accounting.MapGet("/profit-loss", async (
    HttpRequest request,
    DateOnly? from,
    DateOnly? to,
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

    try
    {
        return Results.Ok(await accountingService.GetProfitLossAsync(
            user.CompanyId,
            from,
            to,
            cancellationToken));
    }
    catch (ArgumentException ex)
    {
        return Results.BadRequest(new { error = ex.Message });
    }
});

accounting.MapGet("/balance-sheet", async (
    HttpRequest request,
    DateOnly? asOf,
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

    var reportDate =
        asOf ?? DateOnly.FromDateTime(DateTime.Today);

    return Results.Ok(await accountingService.GetBalanceSheetAsync(
        user.CompanyId,
        reportDate,
        cancellationToken));
});

accounting.MapGet("/trial-balance", async (
    HttpRequest request,
    DateOnly? from,
    DateOnly? to,
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

    try
    {
        return Results.Ok(await accountingService.GetTrialBalanceAsync(
            user.CompanyId,
            from,
            to,
            cancellationToken));
    }
    catch (ArgumentException ex)
    {
        return Results.BadRequest(new { error = ex.Message });
    }
});

accounting.MapGet("/general-ledger", async (
    HttpRequest request,
    Guid? accountId,
    DateOnly? from,
    DateOnly? to,
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

    try
    {
        return Results.Ok(await accountingService.GetGeneralLedgerAsync(
            user.CompanyId,
            accountId,
            from,
            to,
            cancellationToken));
    }
    catch (ArgumentException ex)
    {
        return Results.BadRequest(new { error = ex.Message });
    }
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

static bool CanManageUsers(AppUser user)
{
    return user.Role is
        UserRole.Owner or
        UserRole.Administrator;
}

static bool CanWriteSalesInventory(AppUser user)
{
    return user.Role is
        UserRole.Owner or
        UserRole.Administrator or
        UserRole.FinancialManager or
        UserRole.Accountant or
        UserRole.InventoryManager or
        UserRole.Sales;
}

static bool CanWriteInventory(AppUser user)
{
    return user.Role is
        UserRole.Owner or
        UserRole.Administrator or
        UserRole.FinancialManager or
        UserRole.Accountant or
        UserRole.InventoryManager;
}

static bool CanWriteSales(AppUser user)
{
    return user.Role is
        UserRole.Owner or
        UserRole.Administrator or
        UserRole.FinancialManager or
        UserRole.Accountant or
        UserRole.Sales;
}

static bool CanManageSalesInventorySettings(AppUser user)
{
    return user.Role is
        UserRole.Owner or
        UserRole.Administrator or
        UserRole.FinancialManager or
        UserRole.Accountant;
}

static bool CanWriteAccounting(AppUser user)
{
    return user.Role is
        UserRole.Owner or
        UserRole.Administrator or
        UserRole.FinancialManager or
        UserRole.Accountant;
}
