namespace DTMS.Iam.Application.Callbacks;

/// <summary>
/// Phase S.3.1b — turns a DTMS integration event into the wire-shape a
/// specific external system expects. Resolved by keyed DI: the key is
/// <c>iam.SystemEventSubscriptions.PayloadFormatKey</c>, the same string
/// the admin stored when wiring the subscription.
///
/// <para>Naming convention: <c>{system}.{shape}.v{n}</c> — e.g.
/// <c>oms.shipment.v1</c>, <c>wms.fulfillment.v2</c>. Multiple
/// subscribers can share one formatter if their schemas match.</para>
///
/// <para>Implementations must be deterministic — the same input event
/// must produce byte-identical output, because the outbox row's
/// content is captured at producer time and never re-formatted on
/// dispatch retry.</para>
/// </summary>
public interface ICallbackPayloadFormatter
{
    Task<CallbackPayload> FormatAsync(object integrationEvent, CancellationToken ct);
}

/// <summary>
/// Bytes + media type pair handed off to
/// <see cref="ISourceCallbackDispatcher"/> at outbound time. ContentType
/// lives next to the body so a formatter can switch to non-JSON
/// (CSV, protobuf, XML) without changing the dispatcher.
///
/// <para>Phase S.5 (B2) — <see cref="RelativePath"/> and
/// <see cref="HttpMethod"/> let a formatter override the dispatcher's default
/// <c>POST {CallbackBaseUrl}/events</c> so systems that expect REST-style
/// paths (e.g. OMS's <c>/api/shipments</c> + <c>/api/shipments/{id}/arrived</c>)
/// keep their existing contract. Both null = use the default. The formatter
/// resolves any path token (e.g. shipmentId) itself, so the dispatcher never
/// templates — it just concatenates the resolved path.</para>
/// </summary>
public sealed record CallbackPayload(
    string ContentType,
    byte[] Body,
    string? RelativePath = null,
    string? HttpMethod = null);
