namespace DTMS.Iam.Application.Callbacks;

/// <summary>
/// Phase C (multi-source) — resolves an <see cref="ICallbackPayloadFormatter"/>
/// from a RUNTIME payload-format key (a subscription row's
/// <c>PayloadFormatKey</c>). Application-layer code cannot use
/// <c>[FromKeyedServices]</c> for this — the attribute needs a compile-time
/// const, which is exactly the OMS-pinning this phase removes — and injecting
/// a raw <c>IServiceProvider</c> would smuggle service-locator into a layer
/// that has none today. The implementation lives in the composition root
/// (DTMS.Api), where keyed resolution is idiomatic.
/// </summary>
public interface ICallbackFormatterResolver
{
    /// <summary>
    /// The formatter registered under <paramref name="payloadFormatKey"/>.
    /// Throws (fail fast) when no formatter is registered for the key — a
    /// subscription row naming an unregistered formatter is a config bug.
    /// </summary>
    ICallbackPayloadFormatter Resolve(string payloadFormatKey);
}
