using System.Security.Claims;
using DTMS.DeliveryOrder.Presentation.Idempotency;
using DTMS.Iam.Application.Authorization;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging.Abstractions;

namespace DeliveryOrder.UnitTests;

/// <summary>
/// An Idempotency-Key only ever replays to the caller that made it: the
/// cache entry is scoped by source system / user, so two callers picking the
/// same key no longer share (or conflict on) one entry.
/// </summary>
public class IdempotencyCallerScopeTests
{
    [Fact]
    public void SystemPrincipal_ScopesBySystemKey()
        => IdempotencyKeyFilter.CallerScope(Http(system: "oms")).Should().Be("sys:oms");

    [Fact]
    public void User_ScopesByEmployeeId_BeforeName()
        => IdempotencyKeyFilter.CallerScope(Http(employeeId: "E1001", name: "Sunisa"))
            .Should().Be("usr:E1001");

    [Fact]
    public void User_WithoutEmployeeId_FallsBackToName()
        => IdempotencyKeyFilter.CallerScope(Http(name: "Sunisa")).Should().Be("usr:Sunisa");

    [Fact]
    public void NoIdentity_IsAnon()
        => IdempotencyKeyFilter.CallerScope(Http()).Should().Be("anon");

    [Fact]
    public async Task SameKey_DifferentSystems_LookUpDifferentEntries()
    {
        var cache = new RecordingCache();
        var filter = new IdempotencyKeyFilter(cache, NullLogger<IdempotencyKeyFilter>.Instance);

        foreach (var system in new[] { "oms", "wms" })
        {
            var http = Http(system: system);
            http.Request.Headers["Idempotency-Key"] = "ORDER-1";
            await filter.InvokeAsync(
                new DefaultEndpointFilterInvocationContext(http),
                _ => ValueTask.FromResult<object?>(null));
        }

        cache.Lookups.Should().Equal("idempotency:v3:sys:oms:ORDER-1", "idempotency:v3:sys:wms:ORDER-1");
    }

    private static HttpContext Http(string? system = null, string? employeeId = null, string? name = null)
    {
        var http = new DefaultHttpContext();
        http.Request.Method = "POST";
        http.Request.Path = "/api/v1/source/delivery-orders";
        if (system is not null)
            http.Items["principal"] = new SystemPrincipal(system, system.ToUpperInvariant());

        var claims = new List<Claim>();
        if (employeeId is not null) claims.Add(new Claim("EmployeeId", employeeId));
        if (name is not null) claims.Add(new Claim(ClaimTypes.Name, name));
        if (claims.Count > 0)
            http.User = new ClaimsPrincipal(new ClaimsIdentity(claims, "test"));
        return http;
    }

    private sealed class RecordingCache : IDistributedCache
    {
        public List<string> Lookups { get; } = [];
        public byte[]? Get(string key) { Lookups.Add(key); return null; }
        public Task<byte[]?> GetAsync(string key, CancellationToken token = default)
        {
            Lookups.Add(key);
            return Task.FromResult<byte[]?>(null);
        }
        public void Set(string key, byte[] value, DistributedCacheEntryOptions options) { }
        public Task SetAsync(string key, byte[] value, DistributedCacheEntryOptions options, CancellationToken token = default)
            => Task.CompletedTask;
        public void Refresh(string key) { }
        public Task RefreshAsync(string key, CancellationToken token = default) => Task.CompletedTask;
        public void Remove(string key) { }
        public Task RemoveAsync(string key, CancellationToken token = default) => Task.CompletedTask;
    }
}
