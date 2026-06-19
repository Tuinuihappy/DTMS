# G1 implementation plan — SignalR connection drain protocol

> **Status**: scheduled. Reserved a 2-day work window when picked up.
> **Parent**: [`crash-recovery-workflow-resilience-plan.md`](../crash-recovery-workflow-resilience-plan.md) §11 G1.
> **Motivation**: 2026-06-19 M2 SIGTERM drain test exposed that container exit takes ~149s (vs the 90s `stop_grace_period`) because long-lived SignalR `/hubs/*` WebSocket connections hold the ASP.NET shutdown open. In production K8s this means every rolling deploy SIGKILLs the SignalR client and abandons in-flight vendor HTTP calls.

## The fix in one sentence

Implement a `/admin/drain-start` endpoint that, when called by a K8s `preStop` hook (or our chaos script's new `-WithDrain` switch), flips `/health/ready` to 503, broadcasts a graceful close to every SignalR hub connection, and waits long enough for clients to reconnect to a different pod — so that when SIGTERM lands moments later, the host has no SignalR connections to wait on and exits within seconds.

## Goal / non-goal

**Goal**: container exit ≤ 30s after `drain-start` + SIGTERM sequence, with zero "Disconnected" toasts on the frontend and zero abandoned RIOT3 HTTP calls.

**Non-goal**: K8s migration itself (§4.3). This plan ships the in-process pieces so the K8s migration only needs to add the `preStop` lifecycle hook, not new application code.

---

## Pre-flight (~30 min)

Read once before starting:

- [`crash-recovery-workflow-resilience-plan.md`](../crash-recovery-workflow-resilience-plan.md) §11 G1 — the gap statement
- [`chaos-test-results.md`](../chaos-test-results.md) Phase 5 section — pattern for "deferred but documented" features
- [`src/AMR.DeliveryPlanning.Api/Modules/AdminWorkflowEndpoints.cs`](../../src/AMR.DeliveryPlanning.Api/Modules/AdminWorkflowEndpoints.cs) — the existing admin route group we'll extend
- [`src/AMR.DeliveryPlanning.Api/Program.cs`](../../src/AMR.DeliveryPlanning.Api/Program.cs) section around `AddHealthChecks` — where the readiness probe sits
- [`src/AMR.DeliveryPlanning.Api/Realtime/`](../../src/AMR.DeliveryPlanning.Api/Realtime/) — the SignalR hub setup (we'll add a `Drain/` folder beside the existing observability + filter folders)

Decision check before coding (don't skip — getting these wrong wastes ~half a day):

| Decision | Default | When to pick the alternative |
|---|---|---|
| Drain endpoint path | `/admin/drain-start` | If existing admin auth policy is too heavy for a K8s `curl` from inside the cluster, expose `POST /internal/drain-start` on a sidecar-only listener |
| How to close hub connections | `IHubContext<T>.Clients.All.SendAsync("__drain")` then `connection.Abort()` after 5s | If frontend already handles `connection.onclose`, just abort straight away |
| Drain wait time | 20s before returning 200 | Tune to `terminationGracePeriodSeconds - 10s` once K8s lands |
| `/health/ready` flip | Singleton flag read by a custom `IHealthCheck` | If using `Microsoft.Extensions.Diagnostics.HealthChecks` tags, use a tag-based filter instead |
| Allow drain-start to be undone | No — once called, pod is committed to terminate | Yes only if you need it for tests; document risk |

---

## Phase 1 — Backend drain infrastructure (~5h, single sitting recommended)

### 1.1 `IConnectionDrainService` + impl (~2h)

Files:

- New: `src/AMR.DeliveryPlanning.Api/Realtime/Drain/IConnectionDrainService.cs`
- New: `src/AMR.DeliveryPlanning.Api/Realtime/Drain/ConnectionDrainService.cs`

Shape:

```csharp
public interface IConnectionDrainService
{
    /// <summary>True once StartDrain has been called. Never resets.</summary>
    bool IsDraining { get; }

    /// <summary>Started timestamp (UTC) or null if not draining yet.</summary>
    DateTimeOffset? StartedAt { get; }

    /// <summary>
    /// Begin draining. Idempotent — calling twice does the same as once.
    /// Returns a Task that completes after the broadcast + initial settle window.
    /// </summary>
    Task StartDrainAsync(TimeSpan settleWindow, CancellationToken ct = default);
}

public sealed class ConnectionDrainService : IConnectionDrainService
{
    // Singleton (registered as such in Program.cs).
    private int _draining;                        // 0/1 atomic flag
    private DateTimeOffset? _startedAt;
    private readonly IHubContext<TripsHub> _trips; // example — list ALL hubs
    private readonly ILogger<ConnectionDrainService> _logger;
    private readonly WorkflowMetrics _metrics;

    public ConnectionDrainService(...)  { ... }

    public bool IsDraining => Interlocked.CompareExchange(ref _draining, 0, 0) == 1;
    public DateTimeOffset? StartedAt => _startedAt;

    public async Task StartDrainAsync(TimeSpan settleWindow, CancellationToken ct)
    {
        // Idempotency: first writer wins.
        if (Interlocked.CompareExchange(ref _draining, 1, 0) != 0)
        {
            _logger.LogInformation("[Drain] already started at {When}", _startedAt);
            return;
        }
        _startedAt = DateTimeOffset.UtcNow;

        _logger.LogWarning("[Drain] beginning connection drain — settle window {Settle}", settleWindow);
        _metrics.RecordDrainStarted();

        // Broadcast a graceful "we're going down" event. Frontend's
        // SignalR client treats this as "reconnect now, not later".
        try
        {
            await _trips.Clients.All.SendAsync("__drain", cancellationToken: ct);
            // ... repeat for every hub in the app ...
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Drain] broadcast failed — proceeding anyway");
        }

        // Settle: give clients time to receive the message + reconnect elsewhere.
        // The host's overall shutdown budget owns the upper bound on this.
        await Task.Delay(settleWindow, ct);

        _logger.LogWarning("[Drain] settle window elapsed — host shutdown will follow");
    }
}
```

Register in `Program.cs`:

```csharp
builder.Services.AddSingleton<IConnectionDrainService, ConnectionDrainService>();
```

### 1.2 Wire drain state to `/health/ready` (~30 min)

Add a custom health check that reports Unhealthy when `IsDraining`:

```csharp
public sealed class DrainHealthCheck : IHealthCheck
{
    private readonly IConnectionDrainService _drain;
    public DrainHealthCheck(IConnectionDrainService drain) => _drain = drain;

    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context, CancellationToken ct = default)
        => Task.FromResult(_drain.IsDraining
            ? HealthCheckResult.Unhealthy(
                $"draining since {_drain.StartedAt:o}")
            : HealthCheckResult.Healthy());
}
```

Register in `Program.cs` `AddHealthChecks()` chain — tag it `"ready"` so it's part of `/health/ready` but not `/health`:

```csharp
.AddCheck<DrainHealthCheck>("drain", tags: new[] { "ready" })
```

### 1.3 `DrainAwareHubFilter` — reject new hub connections during drain (~1.5h)

So a client racing to reconnect to a draining pod gets immediately rejected and reroutes to another pod.

File: `src/AMR.DeliveryPlanning.Api/Realtime/Drain/DrainAwareHubFilter.cs`

```csharp
public sealed class DrainAwareHubFilter : IHubFilter
{
    private readonly IConnectionDrainService _drain;
    public DrainAwareHubFilter(IConnectionDrainService drain) => _drain = drain;

    public ValueTask<object?> InvokeMethodAsync(
        HubInvocationContext invocationContext,
        Func<HubInvocationContext, ValueTask<object?>> next)
    {
        if (_drain.IsDraining)
            throw new HubException("server is draining — reconnect");
        return next(invocationContext);
    }

    public Task OnConnectedAsync(HubLifetimeContext context,
        Func<HubLifetimeContext, Task> next)
    {
        if (_drain.IsDraining)
            throw new HubException("server is draining — please connect to a different pod");
        return next(context);
    }
}
```

Register globally:

```csharp
builder.Services.AddSignalR(o => { ... })
    .AddHubOptions<TripsHub>(o => o.AddFilter<DrainAwareHubFilter>())
    // ... repeat per hub OR register a global filter ...
```

### 1.4 Endpoint — `POST /admin/drain-start` (~1h)

Add to `src/AMR.DeliveryPlanning.Api/Modules/AdminWorkflowEndpoints.cs` next to `/replan`:

```csharp
group.MapPost("/drain-start", async (
    [FromServices] IConnectionDrainService drain,
    [FromServices] IConfiguration config,
    CancellationToken ct) =>
{
    var settle = TimeSpan.FromSeconds(
        config.GetValue("Shutdown:DrainSettleSeconds", 20));
    await drain.StartDrainAsync(settle, ct);
    return Results.Ok(new
    {
        draining = drain.IsDraining,
        startedAt = drain.StartedAt,
        settleSeconds = settle.TotalSeconds
    });
})
.WithName("AdminDrainStart")
.WithSummary("Begin pod drain: flip /health/ready Unhealthy + close hub connections + wait settle window.");
```

### Phase 1 acceptance

- [ ] Unit test: `ConnectionDrainService.StartDrainAsync` twice = second call short-circuits, `IsDraining` stays `true`, no double broadcast
- [ ] Unit test: `DrainHealthCheck` returns Unhealthy iff `IsDraining`
- [ ] Unit test: `DrainAwareHubFilter` throws when drain started, allows when not
- [ ] Integration smoke: `POST /admin/drain-start` → `GET /health/ready` returns 503
- [ ] Build clean, all existing tests still pass

---

## Phase 2 — Hub-side integration (~4h)

### 2.1 Enumerate all hubs (~30 min)

Drain must broadcast to **every** hub. Find them first:

```powershell
# from repo root
Get-ChildItem -Recurse -Filter "*Hub.cs" -Path src/ | Select-Object FullName
```

For each hub found, the drain service constructor needs `IHubContext<ThatHub>` injected. Avoid hand-typing — use a generic registration loop or just list them explicitly (probably 2-4 hubs).

### 2.2 Generalise the broadcast loop (~1h)

Instead of one `IHubContext<T>` field per hub, take `IEnumerable<IHubBroadcaster>` where `IHubBroadcaster` is a marker that each hub implements via DI registration:

```csharp
public interface IDrainBroadcaster
{
    string HubName { get; }
    Task BroadcastDrainAsync(CancellationToken ct);
}

public sealed class HubBroadcaster<THub> : IDrainBroadcaster where THub : Hub
{
    private readonly IHubContext<THub> _hub;
    public HubBroadcaster(IHubContext<THub> hub) => _hub = hub;
    public string HubName => typeof(THub).Name;
    public Task BroadcastDrainAsync(CancellationToken ct)
        => _hub.Clients.All.SendAsync("__drain", cancellationToken: ct);
}
```

Register one per hub:

```csharp
builder.Services.AddSingleton<IDrainBroadcaster, HubBroadcaster<TripsHub>>();
builder.Services.AddSingleton<IDrainBroadcaster, HubBroadcaster<FleetHub>>();
// ...
```

Drain service then iterates `IEnumerable<IDrainBroadcaster>`.

### 2.3 Connection tracking (~1.5h)

To know when "all clients have left", track open connections per hub. Use an `IHubFilter.OnConnectedAsync` / `OnDisconnectedAsync` pair to bump a counter:

```csharp
public sealed class ConnectionCounter
{
    private long _count;
    public long Open => Interlocked.Read(ref _count);
    public void OnOpened() => Interlocked.Increment(ref _count);
    public void OnClosed() => Interlocked.Decrement(ref _count);
}
```

Singleton; injected into the drain service so `StartDrainAsync` can wait *up to settle window* for `Open` to hit 0 instead of always waiting the full window. Faster shutdown when clients reconnect quickly.

### 2.4 Metrics (~1h)

Add to `WorkflowMetrics`:

```csharp
public void RecordDrainStarted();
public void RecordDrainCompleted(double durationSeconds, long clientsClosed);
public void RecordHubConnectionsOpened(string hubName);
public void RecordHubConnectionsClosed(string hubName);
public Gauge HubConnectionsOpen(string hubName);  // observable gauge
```

### Phase 2 acceptance

- [ ] All hubs broadcast `__drain` on `StartDrainAsync`
- [ ] Connection counter accurate: open new SignalR client → +1, close → -1
- [ ] `StartDrainAsync` returns when either settle elapses **or** all clients drop, whichever first
- [ ] Metrics emit on drain + connection events
- [ ] Manual test in docker: open dashboard, `POST /admin/drain-start`, observe `__drain` event in browser devtools

---

## Phase 3 — Frontend reconnect handling (~2h)

### 3.1 SignalR client config (~30 min)

File: wherever the hub connection is built in `frontend/lib/` (search for `HubConnectionBuilder`).

```typescript
const connection = new HubConnectionBuilder()
  .withUrl('/hubs/trips', { ... })
  .withAutomaticReconnect([0, 2000, 5000, 10000])  // try immediately, 2s, 5s, 10s
  .build();
```

### 3.2 Handle the `__drain` event (~30 min)

Treat it as "reconnect now, not later":

```typescript
connection.on('__drain', () => {
  // Don't show error; just trigger reconnect immediately.
  // SignalR's automatic reconnect handles the actual reconnection.
  connection.stop().then(() => connection.start());
});
```

### 3.3 UX during reconnect (~1h)

Show a small spinner / "Reconnecting…" indicator, not the existing "Disconnected" error toast. Hide the indicator once reconnected.

Use the SignalR lifecycle hooks:

```typescript
connection.onreconnecting(() => setBanner('Reconnecting…'));
connection.onreconnected(() => clearBanner());
connection.onclose(() => setBanner('Disconnected — refresh page'));  // only after retries exhausted
```

### Phase 3 acceptance

- [ ] Manual test: open dashboard against pod-A, `curl POST /admin/drain-start` on pod-A, browser shows "Reconnecting…" briefly (≤ 5s) then live updates resume from pod-B
- [ ] No "Disconnected" error toast appears
- [ ] Hub events arrive on the new connection without missing the events that fired during the reconnect window (validates upstream replay behaviour, not strictly drain — but it's the right time to check)

---

## Phase 4 — Tests + verification (~4h)

### 4.1 Integration test class (~2h)

File: `tests/Integration/AMR.DeliveryPlanning.IntegrationTests/SignalRDrainTests.cs`

```csharp
public class SignalRDrainTests : IClassFixture<DtmsWebApplicationFactory>
{
    private readonly DtmsWebApplicationFactory _factory;

    [Fact]
    public async Task DrainStart_FlipsHealthReadyTo503()
    {
        var client = _factory.CreateClient();
        var before = await client.GetAsync("/health/ready");
        before.StatusCode.Should().Be(HttpStatusCode.OK);

        var drainResp = await client.PostAsync("/api/v1/admin/drain-start", null);
        drainResp.StatusCode.Should().Be(HttpStatusCode.OK);

        var after = await client.GetAsync("/health/ready");
        after.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
    }

    [Fact]
    public async Task DrainStart_TwiceIsIdempotent() { /* ... */ }

    [Fact]
    public async Task DrainStart_BroadcastsToConnectedHubClient()
    {
        var hubUrl = _factory.Server.BaseAddress + "hubs/trips";
        var hubConn = new HubConnectionBuilder().WithUrl(hubUrl, o => { ... }).Build();
        var drainReceived = new TaskCompletionSource<bool>();
        hubConn.On("__drain", () => drainReceived.TrySetResult(true));
        await hubConn.StartAsync();

        await _factory.CreateClient().PostAsync("/api/v1/admin/drain-start", null);

        var got = await Task.WhenAny(drainReceived.Task, Task.Delay(5000));
        got.Should().Be(drainReceived.Task);
    }

    [Fact]
    public async Task DrainStart_RejectsNewHubConnections() { /* ... */ }
}
```

### 4.2 Extend chaos script with `-WithDrain` (~1.5h)

File: `scripts/chaos/kill-mid-pipeline.ps1`

Add:

```powershell
param(
    # ... existing params ...
    [switch]$WithDrain  # NEW
)

function Invoke-Drain {
    if (-not $WithDrain) { return }
    Write-Step "calling /admin/drain-start"
    try {
        $r = Invoke-WebRequest -Uri "$ApiUrl/api/v1/admin/drain-start" `
            -Method Post -UseBasicParsing -TimeoutSec 30
        Write-Ok "drain-start returned $($r.StatusCode)"
    } catch {
        Write-Warn "drain-start failed: $($_.Exception.Message)"
    }
    Start-Sleep -Seconds 25  # settle window + small buffer
}

# Inside the kill loop, before docker kill:
Invoke-Drain
docker kill --signal=SIGTERM $ContainerName
```

### 4.3 Re-run M2 SIGTERM test with `-WithDrain` (~30 min)

Acceptance: container exit ≤ 30s (down from 149s). Document the run in `chaos-test-results.md`.

### Phase 4 acceptance

- [ ] 4 integration tests pass
- [ ] Chaos script `-WithDrain` switch lands cleanly
- [ ] M2 re-run shows ≤ 30s exit
- [ ] No "Disconnected" toast in browser during the re-run

---

## Phase 5 — Documentation + K8s manifest stub (~2h)

### 5.1 Update plan docs

- `crash-recovery-workflow-resilience-plan.md` §11 G1 → mark ✅ with commit link, fold into a one-line entry
- `crash-recovery-workflow-resilience-plan.md` §8 item 2 → ⚠️ partial → ✅
- `chaos-test-results.md` → add a "Drain-enabled run (date)" subsection with the 30s number
- `docs/plans/g1-signalr-drain-protocol.md` (this file) → annotate "✅ shipped" at the top

### 5.2 K8s manifest stub (~30 min, deferred but stub it)

Create `deploy/k8s/api-deployment.yaml.stub` with the `preStop` hook block, even though K8s migration is a separate plan:

```yaml
spec:
  containers:
    - name: api
      # ...
      lifecycle:
        preStop:
          exec:
            command:
              - sh
              - -c
              - |
                curl -sS -X POST http://localhost:8080/api/v1/admin/drain-start || true
                sleep 25
      # ...
  terminationGracePeriodSeconds: 60
```

The `.stub` suffix signals "design intent, not consumed by anything yet". When K8s migration starts, rename to `.yaml`.

### Phase 5 acceptance

- [ ] All four docs updated
- [ ] K8s manifest stub committed
- [ ] Cross-references between plan §8 / §11 / G1 plan all consistent

---

## Definition of done

- [ ] Container exits ≤ 30s under SIGTERM with `-WithDrain` flow (vs 149s baseline)
- [ ] No `HTTP GET /hubs/* responded 101 in <huge>ms` log lines during shutdown
- [ ] Frontend reconnects in ≤ 5s with no error toast
- [ ] All 4 G1 integration tests green
- [ ] Plan §8 item 2 and §11 G1 marked ✅
- [ ] K8s manifest stub in place
- [ ] Effort total stayed within ~18h (overshoot by > 50% = pause and re-scope)

---

## Off-ramps

- **If Phase 1.1 takes > 3h**: simplify — drop `ConnectionCounter`, always wait the full settle window. Ship the 80% version.
- **If hub enumeration in Phase 2.1 finds > 4 hubs**: don't write `HubBroadcaster<T>` for each — use reflection to enumerate registered `IHubContext<>` and broadcast from a single service. Slower to write, faster than 4 manual registrations.
- **If frontend changes block on coordination with the UI team**: ship backend-only (Phases 1, 2, 4, 5) — the broadcast becomes a no-op but the readiness flip + new-connection-rejection still gives most of the value. Frontend can land in a follow-up commit.
- **If integration tests can't trigger real SignalR clients from xUnit**: use `Microsoft.AspNetCore.SignalR.Client` against `TestServer.Server.CreateHandler()` — works in current MS SignalR; if blocked, drop to unit tests on the drain service only and rely on the chaos script's M2 re-run as the integration signal.

---

## Risks tracker (fill in during the work)

| Risk (anticipated) | Likelihood | Status |
|---|---|---|
| Connection close racing with new message broadcast | Medium | unknown |
| Health check tagging conflicts with existing `/health/ready` aggregation | Medium | unknown |
| Frontend reconnect storm under multi-pod drain | Low | unknown |
| Old browsers ignore `__drain` event | Low | unknown |

---

## When this plan is picked up

Open `docs/plans/g1-signalr-drain-protocol.md`. The pre-flight section names the 5 files to skim, the decision-check table has the picks already defaulted, and each phase is sized to fit a single sitting. Two focused days from cold start to "✅ shipped".
