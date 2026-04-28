using System.Net.Http.Json;
using System.Net.Http.Headers;

namespace AMR.DeliveryPlanning.IntegrationTests;

/// <summary>
/// Helper to authenticate HttpClient with JWT token for integration tests.
/// </summary>
public static class AuthHelper
{
    /// <summary>Returns an HttpClient authenticated as the seeded admin (SystemTenantId).</summary>
    public static async Task<HttpClient> GetAuthenticatedClient(this DtmsWebApplicationFactory factory)
        => await factory.GetClientForTenantAsync(null, "admin", "admin123");

    /// <summary>
    /// Registers a fresh user for <paramref name="tenantId"/> using the admin account,
    /// then returns an HttpClient authenticated as that user.
    /// </summary>
    public static async Task<HttpClient> GetClientForTenantAsync(
        this DtmsWebApplicationFactory factory, Guid tenantId)
    {
        // Step 1: Get admin client to register a new tenant user.
        var adminClient = await factory.GetClientForTenantAsync(null, "admin", "admin123");

        var username = $"tenant_{tenantId:N}"[..30];
        var password = "TenantPass1!";

        // Register the tenant user (admin-only endpoint); X-Tenant-Id sets their tenant.
        var registerRequest = new HttpRequestMessage(HttpMethod.Post, "/api/auth/register")
        {
            Content = JsonContent.Create(new { Username = username, Password = password, Role = "Operator" })
        };
        registerRequest.Headers.Add("X-Tenant-Id", tenantId.ToString());
        var regResp = await adminClient.SendAsync(registerRequest);
        // 400 is acceptable when the user was already registered in a previous test run.
        if (!regResp.IsSuccessStatusCode && regResp.StatusCode != System.Net.HttpStatusCode.BadRequest)
            throw new InvalidOperationException($"Register failed: {regResp.StatusCode} {await regResp.Content.ReadAsStringAsync()}");

        // Step 2: Authenticate as the tenant user to get a tenant-scoped JWT.
        return await factory.GetClientForTenantAsync(tenantId, username, password);
    }

    // ── private helper ─────────────────────────────────────────────────────────

    private static async Task<HttpClient> GetClientForTenantAsync(
        this DtmsWebApplicationFactory factory, Guid? tenantId, string username, string password)
    {
        var client = factory.CreateClient();
        var loginResponse = await client.PostAsJsonAsync("/api/auth/token",
            new { Username = username, Password = password });
        loginResponse.EnsureSuccessStatusCode();
        var tokenResult = await loginResponse.Content.ReadFromJsonAsync<TokenResponse>();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", tokenResult!.Token);
        return client;
    }

    private record TokenResponse(string Token, DateTime ExpiresAt);
}
