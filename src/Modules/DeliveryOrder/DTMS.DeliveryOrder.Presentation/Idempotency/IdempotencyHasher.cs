using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MediatR;
using Microsoft.Extensions.Caching.Distributed;

namespace DTMS.DeliveryOrder.Presentation.Idempotency;

/// <summary>
/// Builds a stable SHA256 hash of a mutation request for Idempotency-Key
/// conflict detection. Hash = <c>METHOD\nPATH\nCanonicalJson(args)</c> where
/// canonical JSON is the framework's default serialization of each argument
/// that is not an injected service (ISender, IDistributedCache, HttpContext, etc).
/// </summary>
public static class IdempotencyHasher
{
    // ASCII Record Separator — between serialized args so adjacent objects
    // can't be confused (`{a:1}{b:2}` vs `{a:1, b:2}`).
    private const char ArgSeparator = '';

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = false,
    };

    public static string Compute(string method, string path, IList<object?> arguments)
    {
        var sb = new StringBuilder();
        sb.Append(method).Append('\n').Append(path).Append('\n');

        foreach (var arg in arguments)
        {
            if (arg is null)
            {
                sb.Append("null");
            }
            else if (IsFrameworkType(arg.GetType()))
            {
                continue;
            }
            else
            {
                sb.Append(JsonSerializer.Serialize(arg, arg.GetType(), JsonOptions));
            }
            sb.Append(ArgSeparator);
        }

        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(sb.ToString()));
        return Convert.ToHexString(bytes);
    }

    private static bool IsFrameworkType(Type t)
    {
        if (typeof(ISender).IsAssignableFrom(t)) return true;
        if (typeof(IDistributedCache).IsAssignableFrom(t)) return true;
        var ns = t.Namespace ?? string.Empty;
        return ns.StartsWith("Microsoft.AspNetCore", StringComparison.Ordinal)
            || ns.StartsWith("Microsoft.Extensions", StringComparison.Ordinal);
    }
}
