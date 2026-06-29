namespace DTMS.Infrastructure.Caching;

/// <summary>
/// Per-process pod identifier shared between cache writer (when it
/// publishes an invalidation) and subscriber (when it filters out its
/// own echoes). Registered as a singleton so every component on the
/// same pod reads the same value; a fresh Guid is fine because the id
/// only needs to be unique across replicas during one Redis pub/sub
/// session — not persistent across restarts.
/// </summary>
public sealed class PodIdentity
{
    public string Id { get; } = Guid.NewGuid().ToString("N");
}
