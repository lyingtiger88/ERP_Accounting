using ERPAccounting.Api.Contracts;
using ERPAccounting.Api.Domain;
using ERPAccounting.Api.Infrastructure;
using ERPAccounting.Api.Security;
using Microsoft.AspNetCore.Identity;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSingleton<InMemoryStore>();
builder.Services.AddSingleton<PasswordHasher<AppUser>>();
builder.Services.AddSingleton<AuthService>();
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

auth.MapPost("/bootstrap", (BootstrapRequest request, AuthService authService) =>
{
    try
    {
        var created = authService.Bootstrap(request);
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
    var rawHeader = request.Headers.Authorization.ToString();
    var token = rawHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)
        ? rawHeader["Bearer ".Length..].Trim()
        : null;

    var user = authService.Resolve(token);
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

app.Run();
