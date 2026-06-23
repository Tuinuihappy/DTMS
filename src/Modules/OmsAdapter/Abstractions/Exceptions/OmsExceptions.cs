using System.Net;

namespace AMR.DeliveryPlanning.OmsAdapter.Abstractions.Exceptions;

// Classification of upstream-OMS HTTP failures. Both inherit
// HttpRequestException so existing catch sites stay correct, but the
// concrete type lets MassTransit fast-fail OmsPermanentException to DLQ
// instead of retrying for ~80 minutes against an error that will never
// resolve on its own.
//
// Mapping (see HttpOmsShipmentClient.ThrowMappedException):
//   - 2xx + 409 (on /shipments)               → no throw, success
//   - 408 / 425 / 429 / 5xx                   → OmsTransientException
//   - other 4xx (400/401/403/404/422/...)     → OmsPermanentException
//   - network / DNS / TLS failures            → raw HttpRequestException
//                                               (transient by default)
//
// StatusCode is read off the inherited HttpRequestException.StatusCode
// (HttpStatusCode?). We only add ResponseBody — the OMS error body that
// tells the operator what went wrong (e.g. "LotNo not found: rr.").

public sealed class OmsPermanentException : HttpRequestException
{
    public OmsPermanentException(HttpStatusCode statusCode, string responseBody)
        : base($"OMS rejected request ({(int)statusCode}): {Truncate(responseBody)}", inner: null, statusCode: statusCode)
    {
        ResponseBody = responseBody;
    }

    public string ResponseBody { get; }

    private static string Truncate(string s) =>
        s.Length <= 200 ? s : s[..200] + "…";
}

public sealed class OmsTransientException : HttpRequestException
{
    public OmsTransientException(HttpStatusCode statusCode, string responseBody)
        : base($"OMS transient error ({(int)statusCode}): {Truncate(responseBody)}", inner: null, statusCode: statusCode)
    {
        ResponseBody = responseBody;
    }

    public string ResponseBody { get; }

    private static string Truncate(string s) =>
        s.Length <= 200 ? s : s[..200] + "…";
}
