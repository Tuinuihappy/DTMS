using DTMS.Iam.Application.Callbacks;
using Microsoft.Extensions.DependencyInjection;

namespace DTMS.Api.Infrastructure.Callbacks;

/// <summary>
/// Phase C — composition-root implementation of
/// <see cref="ICallbackFormatterResolver"/>: a thin veneer over keyed-service
/// resolution so Application-layer resend handlers can pick a formatter from
/// a subscription row's <c>PayloadFormatKey</c> at runtime, the same way the
/// fan-out consumers here do with <c>GetRequiredKeyedService</c> directly.
/// </summary>
public sealed class KeyedCallbackFormatterResolver : ICallbackFormatterResolver
{
    private readonly IServiceProvider _sp;

    public KeyedCallbackFormatterResolver(IServiceProvider sp) => _sp = sp;

    public ICallbackPayloadFormatter Resolve(string payloadFormatKey)
        => _sp.GetRequiredKeyedService<ICallbackPayloadFormatter>(payloadFormatKey);
}
