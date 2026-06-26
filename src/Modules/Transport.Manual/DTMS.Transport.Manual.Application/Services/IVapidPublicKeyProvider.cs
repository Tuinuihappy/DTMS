namespace DTMS.Transport.Manual.Application.Services;

// Same pattern as IPodBucketProvider — narrow indirection so
// Application doesn't depend on IOptions<VapidOptions>. Infrastructure
// registers a single-value provider during DI wire-up.
public interface IVapidPublicKeyProvider
{
    string PublicKey { get; }
}
