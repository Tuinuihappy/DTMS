using System.Net;
using System.Text;
using System.Text.Json;
using DTMS.Iam.Application.Callbacks;
using DTMS.Iam.Infrastructure.Callbacks;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace DTMS.Api.UnitTests;

// Outbound callback-token auto-refresh (Phase S.9). Covers the pure decision
// policy, the JWT exp decoder for the bare token a mint endpoint returns, the
// SSRF allowlist, and the minter's response parsing.
public class RefreshPolicyTests
{
    private static readonly DateTime Now = new(2026, 7, 24, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Perpetual_AutoPath_Skips()
        => RefreshPolicy.Evaluate(hasCurrentToken: true, currentExp: null, force: false, 28800, Now)
            .Should().Be(MintDecision.Skip);

    [Fact]
    public void Perpetual_ForcePath_Rejects()
        => RefreshPolicy.Evaluate(hasCurrentToken: true, currentExp: null, force: true, 28800, Now)
            .Should().Be(MintDecision.RejectPerpetual);

    [Fact]
    public void NotDue_AutoPath_Skips()
        => RefreshPolicy.Evaluate(true, Now.AddHours(20), force: false, 28800 /* 8h */, Now)
            .Should().Be(MintDecision.Skip);

    [Fact]
    public void Due_AutoPath_Mints()
        => RefreshPolicy.Evaluate(true, Now.AddHours(2), force: false, 28800, Now)
            .Should().Be(MintDecision.Mint);

    [Fact]
    public void NotDue_ForcePath_MintsAnyway()
        => RefreshPolicy.Evaluate(true, Now.AddHours(20), force: true, 28800, Now)
            .Should().Be(MintDecision.Mint);

    [Fact]
    public void NoCurrentToken_Mints()
        => RefreshPolicy.Evaluate(hasCurrentToken: false, currentExp: null, force: false, 28800, Now)
            .Should().Be(MintDecision.Mint);

    [Fact]
    public void AcceptsMinted_LaterExp_True()
        => RefreshPolicy.AcceptsMinted(Now, Now.AddHours(1)).Should().BeTrue();

    [Fact]
    public void AcceptsMinted_NotNewer_False()
        => RefreshPolicy.AcceptsMinted(Now, Now).Should().BeFalse();

    [Fact]
    public void AcceptsMinted_PerpetualNew_True()
        => RefreshPolicy.AcceptsMinted(Now, null).Should().BeTrue();
}

public class CallbackTokenInspectorTests
{
    // {"exp": 1784897714} → 2026-07-24T12:55:14Z
    private static string Jwt(long? exp)
    {
        static string B64Url(string s) =>
            Convert.ToBase64String(Encoding.UTF8.GetBytes(s)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        var header = B64Url("""{"alg":"none","typ":"JWT"}""");
        var payload = B64Url(exp is null ? """{"sub":"x"}""" : $$"""{"exp":{{exp}}}""");
        return $"{header}.{payload}.sig";
    }

    [Fact]
    public void ReadExpFromBareJwt_DecodesExp()
        => CallbackTokenInspector.ReadExpFromBareJwt(Jwt(1784897714))
            .Should().Be(DateTimeOffset.FromUnixTimeSeconds(1784897714).UtcDateTime);

    [Fact]
    public void ReadExpFromBareJwt_NoExp_ReturnsNull()
        => CallbackTokenInspector.ReadExpFromBareJwt(Jwt(null)).Should().BeNull();

    [Fact]
    public void ReadExpFromBareJwt_Malformed_ReturnsNull()
        => CallbackTokenInspector.ReadExpFromBareJwt("not-a-jwt").Should().BeNull();

    [Fact]
    public void ReadExpiryFromConfig_UnwrapsTokenField()
    {
        var config = JsonSerializer.Serialize(new { token = Jwt(1784897714) });
        CallbackTokenInspector.ReadExpiryFromConfig(config)
            .Should().Be(DateTimeOffset.FromUnixTimeSeconds(1784897714).UtcDateTime);
    }
}

public class MintUrlValidatorTests
{
    private static readonly string[] Allow = { "10.204.212.28", "wms.internal" };

    [Fact]
    public void AllowlistedHost_Passes()
        => MintUrlValidator.IsAllowed("http://10.204.212.28:15000/auth/login", Allow, out _)
            .Should().BeTrue();

    [Fact]
    public void UnlistedHost_Rejected()
    {
        MintUrlValidator.IsAllowed("http://evil.example/login", Allow, out var err).Should().BeFalse();
        err.Should().Contain("allowlist");
    }

    [Fact]
    public void NonHttpScheme_Rejected()
    {
        MintUrlValidator.IsAllowed("file:///etc/passwd", Allow, out var err).Should().BeFalse();
        err.Should().Contain("scheme");
    }

    [Fact]
    public void EmptyAllowlist_DeniesAll()
        => MintUrlValidator.IsAllowed("http://10.204.212.28/login", Array.Empty<string>(), out _)
            .Should().BeFalse();
}

public class HttpCallbackTokenMinterTests
{
    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly HttpResponseMessage _resp;
        public StubHandler(HttpResponseMessage resp) => _resp = resp;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
            => Task.FromResult(_resp);
    }

    private static HttpCallbackTokenMinter Build(HttpResponseMessage resp)
    {
        var opts = new CallbackTokenRefreshOptions { AllowedMintHosts = { "mint.internal" } };
        var monitor = new StaticOptionsMonitor<CallbackTokenRefreshOptions>(opts);
        return new HttpCallbackTokenMinter(
            new HttpClient(new StubHandler(resp)),
            monitor,
            NullLogger<HttpCallbackTokenMinter>.Instance);
    }

    [Fact]
    public async Task Mint_ExtractsTopLevelToken()
    {
        var resp = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("""{"token":"abc.def.ghi"}""", Encoding.UTF8, "application/json"),
        };
        var minter = Build(resp);
        var settings = new TokenRefreshSettings { TokenUrl = "http://mint.internal/login", Username = "u", Password = "p" };

        (await minter.MintAsync(settings, default)).Should().Be("abc.def.ghi");
    }

    [Fact]
    public async Task Mint_ExtractsDottedTokenPath()
    {
        var resp = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("""{"data":{"token":"nested.jwt.sig"}}""", Encoding.UTF8, "application/json"),
        };
        var minter = Build(resp);
        var settings = new TokenRefreshSettings
        {
            TokenUrl = "http://mint.internal/login", Username = "u", Password = "p", TokenField = "data.token",
        };

        (await minter.MintAsync(settings, default)).Should().Be("nested.jwt.sig");
    }

    [Fact]
    public async Task Mint_UnlistedHost_Throws()
    {
        var minter = Build(new HttpResponseMessage(HttpStatusCode.OK));
        var settings = new TokenRefreshSettings { TokenUrl = "http://evil.example/login", Username = "u", Password = "p" };

        await FluentActions.Awaiting(() => minter.MintAsync(settings, default))
            .Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task Mint_Non2xx_Throws()
    {
        var minter = Build(new HttpResponseMessage(HttpStatusCode.Unauthorized));
        var settings = new TokenRefreshSettings { TokenUrl = "http://mint.internal/login", Username = "u", Password = "p" };

        await FluentActions.Awaiting(() => minter.MintAsync(settings, default))
            .Should().ThrowAsync<HttpRequestException>();
    }
}

// Minimal IOptionsMonitor over a fixed value for tests.
internal sealed class StaticOptionsMonitor<T> : IOptionsMonitor<T>
{
    public StaticOptionsMonitor(T value) => CurrentValue = value;
    public T CurrentValue { get; }
    public T Get(string? name) => CurrentValue;
    public IDisposable? OnChange(Action<T, string?> listener) => null;
}
