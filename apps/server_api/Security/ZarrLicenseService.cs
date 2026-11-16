using System.Net.Http.Headers;
using System.Text.Json;
using ERPAccounting.Api.Domain;
using ERPAccounting.Api.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace ERPAccounting.Api.Security;

/// <summary>
/// Verifies a Supabase Auth user before linking it to the current ERP company.
/// Uses the Supabase service-role key ONLY on the ASP.NET server for entitlement
/// checks. The key must be injected through secret configuration, never shipped.
/// </summary>
public sealed class ZarrLicenseService(
    AppDbContext database,
    IHttpClientFactory httpClients,
    IConfiguration configuration)
{
    public const string ProductId = "zarr";

    private string BaseUrl =>
        (configuration["Kaliia:SupabaseUrl"] ??
         "https://yztithydgdvydaktuxlw.supabase.co").TrimEnd('/');

    private string PublishableKey =>
        configuration["Kaliia:PublishableKey"] ??
        "sb_publishable_UAKFYbxgWOCVRDDdv_0JLg_oIIiubmT";

    private string? ServiceRoleKey => configuration["Kaliia:ServiceRoleKey"];

    public async Task<Guid?> VerifyKaliiaUserAsync(
        string accessToken,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(accessToken))
            return null;

        using var client = httpClients.CreateClient();
        client.Timeout = TimeSpan.FromSeconds(12);
        using var request = new HttpRequestMessage(
            HttpMethod.Get, BaseUrl + "/auth/v1/user");
        request.Headers.Authorization =
            new AuthenticationHeaderValue("Bearer", accessToken);
        request.Headers.TryAddWithoutValidation("apikey", PublishableKey);

        using var response = await client.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
            return null;

        using var document = JsonDocument.Parse(
            await response.Content.ReadAsStringAsync(cancellationToken));
        var body = document.RootElement;
        if (!body.TryGetProperty("id", out var idJson) ||
            !Guid.TryParse(idJson.GetString(), out var id))
            return null;

        // Never trust user-editable JWT metadata to prove email confirmation.
        if (!body.TryGetProperty("email_confirmed_at", out var verified) ||
            verified.ValueKind != JsonValueKind.String ||
            string.IsNullOrEmpty(verified.GetString()))
            return null;

        return id;
    }

    public async Task LinkCompanyAsync(
        Guid companyId, Guid ownerId, Guid kaliiaUserId,
        CancellationToken cancellationToken)
    {
        var link = await database.ZarrCompanyLicenses
            .SingleOrDefaultAsync(x => x.CompanyId == companyId, cancellationToken);
        if (link is null)
        {
            database.ZarrCompanyLicenses.Add(new ZarrCompanyLicense
            {
                CompanyId = companyId,
                AuthUserId = kaliiaUserId,
                LinkedByUserId = ownerId,
                LinkedAt = DateTimeOffset.UtcNow,
            });
        }
        else
        {
            link.AuthUserId = kaliiaUserId;
            link.LinkedByUserId = ownerId;
            link.LinkedAt = DateTimeOffset.UtcNow;
        }
        await database.SaveChangesAsync(cancellationToken);
    }

    public Task<bool> HasCompanyLinkAsync(
        Guid companyId, CancellationToken cancellationToken)
        => database.ZarrCompanyLicenses.AsNoTracking()
            .AnyAsync(x => x.CompanyId == companyId, cancellationToken);

    /// <summary>
    /// Always fail closed for writes when enforcement is enabled but the
    /// company is unlinked, Supabase is unreachable, or the license is expired.
    /// </summary>
    public async Task<bool> CanWriteAsync(
        Guid companyId, CancellationToken cancellationToken)
    {
        var link = await database.ZarrCompanyLicenses
            .AsNoTracking()
            .SingleOrDefaultAsync(x => x.CompanyId == companyId, cancellationToken);
        if (link is null || string.IsNullOrWhiteSpace(ServiceRoleKey))
            return false;

        try
        {
            using var http = httpClients.CreateClient();
            http.Timeout = TimeSpan.FromSeconds(12);
            var accountId = Uri.EscapeDataString(link.AuthUserId.ToString());
            var accountUrl = BaseUrl +
                "/rest/v1/zurvan_accounts?select=status&id=eq." + accountId + "&limit=1";
            var accountJson = await GetAdminJsonAsync(http, accountUrl, cancellationToken);
            if (accountJson.ValueKind != JsonValueKind.Array ||
                accountJson.GetArrayLength() != 1 ||
                !accountJson[0].TryGetProperty("status", out var accountStatus) ||
                accountStatus.GetString() != "active")
                return false;

            var url = BaseUrl + "/rest/v1/zurvan_licenses" +
                "?select=status,type,expires_at&account_id=eq." + accountId +
                "&product_id=eq.zarr&limit=1";
            var licenseJson = await GetAdminJsonAsync(http, url, cancellationToken);
            if (licenseJson.ValueKind != JsonValueKind.Array ||
                licenseJson.GetArrayLength() != 1)
                return false;
            var license = licenseJson[0];
            if (!license.TryGetProperty("status", out var status) ||
                status.ValueKind != JsonValueKind.String ||
                status.GetString() != "active")
                return false;

            if (!license.TryGetProperty("type", out var licenseType) ||
                licenseType.ValueKind != JsonValueKind.String)
                return false;

            if (!license.TryGetProperty("expires_at", out var expiry))
                return false;

            // Only an explicit lifetime entitlement may omit expiry.
            // Trial and subscription records must always have a future expiry.
            if (licenseType.GetString() == "lifetime")
                return expiry.ValueKind == JsonValueKind.Null;

            if (licenseType.GetString() is not ("trial" or "subscription") ||
                expiry.ValueKind != JsonValueKind.String ||
                !DateTimeOffset.TryParse(expiry.GetString(), out var end))
                return false;

            return end > DateTimeOffset.UtcNow;
        }
        catch (HttpRequestException) { return false; }
        catch (TaskCanceledException) { return false; }
        catch (JsonException) { return false; }
        catch (InvalidOperationException) { return false; }
    }

    private async Task<JsonElement> GetAdminJsonAsync(
        HttpClient http, string url, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.TryAddWithoutValidation("apikey", ServiceRoleKey);
        request.Headers.Authorization = new AuthenticationHeaderValue(
            "Bearer", ServiceRoleKey);
        using var response = await http.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
        using var document = JsonDocument.Parse(
            await response.Content.ReadAsStringAsync(cancellationToken));
        return document.RootElement.Clone();
    }
}
