using System.Reflection;
using DTMS.Api.Infrastructure.Metrics;
using FluentAssertions;

namespace DTMS.Api.UnitTests;

// A Meter that isn't registered with OpenTelemetry keeps counting and exports
// nothing — no error anywhere. DTMS.Iam.CallbackRefresh and
// DTMS.Infrastructure.Logging both shipped that way. Every class that names its
// meter with a `MeterName` constant must appear in DtmsMeters.All, which
// Program.cs registers.
public class MeterRegistrationTests
{
    [Fact]
    public void EveryDtmsMeter_IsRegisteredForExport()
    {
        var declared = DtmsAssemblies()
            .SelectMany(a => SafeTypes(a))
            .Select(t => t.GetField("MeterName", BindingFlags.Public | BindingFlags.Static))
            .Where(f => f is { IsLiteral: true } && f.FieldType == typeof(string))
            .Select(f => (string)f!.GetRawConstantValue()!)
            .Distinct()
            .ToList();

        declared.Should().NotBeEmpty("the scan must actually find the meters");
        declared.Should().BeSubsetOf(DtmsMeters.All,
            "add the missing meter to DtmsMeters.All or it is never exported");
    }

    private static IEnumerable<Assembly> DtmsAssemblies()
    {
        var seen = new Dictionary<string, Assembly>();
        var queue = new Queue<Assembly>([typeof(DtmsMeters).Assembly]);
        while (queue.Count > 0)
        {
            var asm = queue.Dequeue();
            if (!seen.TryAdd(asm.GetName().Name!, asm)) continue;
            foreach (var reference in asm.GetReferencedAssemblies())
            {
                if (reference.Name?.StartsWith("DTMS", StringComparison.Ordinal) != true) continue;
                if (seen.ContainsKey(reference.Name)) continue;
                queue.Enqueue(Assembly.Load(reference));
            }
        }
        return seen.Values;
    }

    private static IEnumerable<Type> SafeTypes(Assembly a)
    {
        try { return a.GetTypes(); }
        catch (ReflectionTypeLoadException e) { return e.Types.Where(t => t is not null)!; }
    }
}
