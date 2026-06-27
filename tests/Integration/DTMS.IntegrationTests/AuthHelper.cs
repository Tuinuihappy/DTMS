using System.Net.Http.Json;
using System.Net.Http.Headers;

namespace DTMS.IntegrationTests;

public static class AuthHelper
{
    public static async Task<HttpClient> GetAuthenticatedClient(this DtmsWebApplicationFactory factory)
    {
        var client = factory.CreateClient();
        var loginResponse = await client.PostAsJsonAsync("/api/auth/token",
            new { Username = "admin", Password = "admin123" });
        loginResponse.EnsureSuccessStatusCode();
        var tokenResult = await loginResponse.Content.ReadFromJsonAsync<TokenResponse>();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", tokenResult!.Token);
        return client;
    }

    private record TokenResponse(string Token, DateTime ExpiresAt);
}
