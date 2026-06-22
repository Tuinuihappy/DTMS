# ADR-006: Transport Mode Feature Flag Mechanism

- **Status**: Accepted
- **Date**: 2026-06-22
- **Deciders**: Architecture team
- **Related**: [ADR-001](adr-001-multi-mode-transport-split.md), all phases

## Context

หลัง Phase 5 จะมี 3 transport modes (Amr, Manual, Fleet) ที่ register ผ่าน `AddTransportXxx()` extension methods แต่ละ deployment อาจไม่ต้องการเปิดทุก mode:

| Deployment scenario | Modes enabled |
|---|---|
| Smart warehouse (in-house AMR only) | Amr |
| Last-mile + manual delivery | Manual + Fleet |
| Full enterprise multi-mode | Amr + Manual + Fleet |
| Dev / staging | All enabled |
| Disaster recovery (degraded) | Selectively disable broken mode |

ปัญหาที่ต้องตัดสิน:
1. Feature flag mechanism — config file? runtime DB? external service?
2. Granularity — per mode? per provider (Kerry vs Flash)? per feature within mode?
3. Validation — flag combination ที่ผิด (เช่น "ห้ามเปิด Manual ถ้าไม่มี geofence configured")
4. Change propagation — ปิด/เปิด mode ต้อง restart หรือไม่?
5. Audit — ใครเปลี่ยน flag เมื่อไหร่?

## Decision

ใช้ **3-level flag hierarchy** ผ่าน `IConfiguration` (appsettings + env vars + Key Vault):

### Level 1: Per-Mode Enable Flag (Required)

```json
{
  "TransportModes": {
    "Amr":    { "Enabled": true,  ... },
    "Manual": { "Enabled": true,  ... },
    "Fleet":  { "Enabled": false, ... }
  }
}
```

- `Enabled: false` → `AddTransportXxx()` early-returns ก่อน register services
- ไม่มี service registration = ไม่มี background services, ไม่มี endpoints
- Composition root อ่านง่าย, deterministic

### Level 2: Per-Provider Enable Flag (Fleet only)

```json
{
  "TransportModes": {
    "Fleet": {
      "Enabled": true,
      "Providers": {
        "Kerry": { "Enabled": true,  "BaseUrl": "...", "ApiKey": "..." },
        "Flash": { "Enabled": false, "BaseUrl": "...", "ApiKey": "..." }
      }
    }
  }
}
```

- `FleetProviderFactory` register แค่ providers ที่ enabled
- Order dispatch → provider selector มองเห็นแค่ enabled providers

### Level 3: Per-Feature Sub-Flags (within mode)

```json
{
  "TransportModes": {
    "Manual": {
      "Enabled": true,
      "Features": {
        "GeofenceEnforcement": true,        // reject pickup outside geofence
        "PhotoRequired": true,               // POD must include photo
        "SignatureRequired": true,           // POD must include signature
        "OfflineSyncEnabled": true,         // accept batch event replay
        "PresenceHeartbeatRequired": true
      },
      "Sla": {
        "AcknowledgmentMinutes": 15,
        "PickupMinutes": 60,
        "DropMinutes": 180
      }
    }
  }
}
```

- Sub-flags = behavior toggles ภายใน mode
- Strongly-typed `ManualModeOptions` class with validation
- Read-only at startup (DI binding)

### Startup Validation

```csharp
// in TransportManualServiceCollectionExtensions
public static IServiceCollection AddTransportManual(this IServiceCollection services, IConfiguration config)
{
    var options = config.GetSection("TransportModes:Manual").Get<ManualModeOptions>();
    if (options is null || !options.Enabled) return services;

    // Validate combinations
    if (options.Features.PhotoRequired && string.IsNullOrEmpty(options.PodStorage?.BucketName))
        throw new InvalidOperationException("Manual.Features.PhotoRequired=true requires Manual.PodStorage.BucketName");

    if (options.Sla.AcknowledgmentMinutes >= options.Sla.PickupMinutes)
        throw new InvalidOperationException("Acknowledgment SLA must be less than Pickup SLA");

    services.Configure<ManualModeOptions>(config.GetSection("TransportModes:Manual"));
    // ... register services
    return services;
}
```

Validation failures → app fails to start (fail fast > silent misconfiguration)

## Pattern: Mode-Aware Endpoint Registration

Endpoints conditionally registered:

```csharp
// in Program.cs
var enabledModes = builder.Configuration
    .GetSection("TransportModes")
    .GetChildren()
    .Where(c => c.GetValue<bool>("Enabled"))
    .Select(c => c.Key)
    .ToList();

if (enabledModes.Contains("Manual")) {
    app.MapOperatorEndpoints();   // /api/operator/*
}

if (enabledModes.Contains("Fleet")) {
    app.MapFleetEndpoints();       // /api/transport-fleet/*
    app.MapFleetWebhooks();        // /api/webhooks/fleet/*
}

// AMR webhooks always available (legacy)
if (enabledModes.Contains("Amr")) {
    app.MapRiot3Webhooks();        // /api/webhooks/riot3/*
}
```

## Frontend Awareness

Frontend reads enabled modes from `/api/system/capabilities` endpoint:

```typescript
// frontend/lib/api/system.ts
export type SystemCapabilities = {
  enabledModes: TransportMode[];
  features: {
    geofenceEnforcement?: boolean;
    photoRequired?: boolean;
  };
};

// Conditional UI rendering
const { data: caps } = useSWR('/api/system/capabilities', fetcher);

{caps.enabledModes.includes('Manual') && (
  <ManualOperatorBoardLink />
)}
```

## Alternatives Considered

### Alternative A: Runtime feature flag service (LaunchDarkly / Unleash / Statsig)

**Pros:**
- Change flags without restart
- A/B testing capability
- Audit log built-in
- Per-user/region targeting

**Cons:**
- External dependency (cost + reliability)
- Network call per flag check (latency)
- Overkill for infrastructure-level toggles (modes registered at startup)
- Team experience: ต่ำ

**Rejected because:** transport mode is **infrastructure decision** — change requires schema migration anyway (e.g., enable Manual → need operator tables). Runtime toggle ก็ต้อง restart เพื่อ wire up DI ใหม่อยู่ดี

### Alternative B: Database-driven flags

Store flags in `system_settings` table; reload periodically

**Pros:**
- Change without redeploy (just UPDATE)
- Per-tenant configuration possible

**Cons:**
- Same problem as Alternative A — DI registered at startup
- Bootstrap problem (need DB to know if DB-using features enabled)
- Adds DB query to startup path

**Rejected because:** complexity > benefit for current scale (single-tenant)

### Alternative C: Per-module solution build (compile-time flag)

Conditional `#if` directives or separate solutions per deployment

**Pros:**
- Zero runtime overhead
- Dead code eliminated from binary

**Cons:**
- 3+ build pipelines
- Test matrix explosion
- Pre-launch agility lost

**Rejected because:** all modes ship in same binary; flag controls registration not compilation

### Alternative D: Single boolean (enable all or none)

**Pros:** Simple

**Cons:**
- ไม่ตรง requirement (deployment scenarios)
- ไม่ allow degraded mode (ปิด Manual ระหว่าง mobile app outage)

**Rejected:** Too coarse

## Consequences

### Positive

- ✓ Composition root reads as declarative spec ("we enable X, Y, Z")
- ✓ `Enabled: false` = zero runtime cost (no background services, no endpoints, no DB queries)
- ✓ Validation at startup catches misconfiguration before traffic
- ✓ Cap config maps cleanly to .NET `IOptions<T>` pattern
- ✓ Test matrix manageable: 3 modes × {enabled, disabled} = test focused config combos
- ✓ Strongly-typed options classes (`ManualModeOptions`, `FleetProviderOptions`) catch typos at compile

### Negative

- ✗ Flag change requires restart (acceptable — modes ต่างกันที่ deploy infrastructure)
- ✗ No per-user/region targeting (acceptable — we have 1 region)
- ✗ Config file มี secrets (mitigated: Key Vault references via `${KERRY_API_KEY}`)
- ✗ Frontend needs `/api/system/capabilities` endpoint (small extra surface)

### Neutral

- Audit: relies on Git history of appsettings.json + deployment logs (not real-time)
- A/B testing: not supported (ใช้ separate tool ถ้าจำเป็นในอนาคต)

## Config File Structure (Full Example)

```json
{
  "TransportModes": {
    "Amr": {
      "Enabled": true,
      "Vendor": "Riot3",
      "Riot3": {
        "BaseUrl": "https://riot3.example.com",
        "ApiKey": "${RIOT3_API_KEY}",
        "WebhookSecret": "${RIOT3_WEBHOOK_SECRET}",
        "PollingIntervalSeconds": 5,
        "HealthCheckIntervalSeconds": 60
      }
    },
    "Manual": {
      "Enabled": true,
      "Features": {
        "GeofenceEnforcement": true,
        "PhotoRequired": true,
        "SignatureRequired": true,
        "OfflineSyncEnabled": true,
        "PresenceHeartbeatRequired": true
      },
      "Sla": {
        "AcknowledgmentMinutes": 15,
        "PickupMinutes": 60,
        "DropMinutes": 180,
        "WatchdogScanIntervalMinutes": 1
      },
      "PushNotification": {
        "Gateway": "Fcm",
        "FirebaseCredentialsPath": "${FIREBASE_CREDENTIALS_PATH}",
        "ProjectId": "dtms-production-th"
      },
      "PodStorage": {
        "Provider": "S3",
        "BucketName": "dtms-pod",
        "Region": "ap-southeast-1"
      }
    },
    "Fleet": {
      "Enabled": false,
      "Providers": {
        "Kerry": {
          "Enabled": false,
          "BaseUrl": "https://api.kerryexpress.com",
          "ApiKey": "${KERRY_API_KEY}",
          "WebhookSecret": "${KERRY_WEBHOOK_SECRET}",
          "Priority": 1
        },
        "Flash": {
          "Enabled": false,
          "BaseUrl": "https://api.flashexpress.com",
          "ApiKey": "${FLASH_API_KEY}",
          "WebhookSecret": "${FLASH_WEBHOOK_SECRET}",
          "Priority": 2
        }
      },
      "ReconciliationIntervalMinutes": 10
    }
  }
}
```

## Strongly-Typed Options

```csharp
// in Transport.Manual.Application
public sealed class ManualModeOptions
{
    public bool Enabled { get; init; }
    public ManualFeaturesOptions Features { get; init; } = new();
    public ManualSlaOptions Sla { get; init; } = new();
    public PushNotificationOptions PushNotification { get; init; } = new();
    public PodStorageOptions PodStorage { get; init; } = new();
}

public sealed class ManualSlaOptions
{
    public int AcknowledgmentMinutes { get; init; } = 15;
    public int PickupMinutes { get; init; } = 60;
    public int DropMinutes { get; init; } = 180;
    public int WatchdogScanIntervalMinutes { get; init; } = 1;

    public TimeSpan AckTimeout => TimeSpan.FromMinutes(AcknowledgmentMinutes);
    public TimeSpan PickupTimeout => TimeSpan.FromMinutes(PickupMinutes);
    public TimeSpan DropTimeout => TimeSpan.FromMinutes(DropMinutes);
}
```

## Capabilities Endpoint

```csharp
app.MapGet("/api/system/capabilities", (IConfiguration config) =>
{
    var enabledModes = config.GetSection("TransportModes")
        .GetChildren()
        .Where(c => c.GetValue<bool>("Enabled"))
        .Select(c => c.Key)
        .ToList();

    return new {
        enabledModes,
        manualFeatures = enabledModes.Contains("Manual")
            ? config.GetSection("TransportModes:Manual:Features").Get<Dictionary<string, bool>>()
            : null,
        fleetProviders = enabledModes.Contains("Fleet")
            ? config.GetSection("TransportModes:Fleet:Providers")
                .GetChildren()
                .Where(p => p.GetValue<bool>("Enabled"))
                .Select(p => p.Key)
                .ToList()
            : null
    };
});
```

## Acceptance Criteria

- [ ] `IConfiguration`-driven flag pattern ใช้ใน Phase 1+
- [ ] Strongly-typed options classes per mode (`AmrModeOptions`, `ManualModeOptions`, `FleetModeOptions`)
- [ ] Startup validation throws on invalid combinations
- [ ] `/api/system/capabilities` endpoint expose enabled modes
- [ ] Frontend `useCapabilities()` hook conditionally renders UI
- [ ] Integration tests cover: all-on, AMR-only, Manual-only, Fleet-only combinations

## References

- .NET IOptions pattern: https://learn.microsoft.com/dotnet/core/extensions/options
- 12-factor app config: https://12factor.net/config
- [Phase 4 ManualModeOptions usage](../phases/phase-4-transport-manual.md)
