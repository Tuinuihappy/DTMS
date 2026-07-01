namespace DTMS.DeliveryOrder.Domain;

/// <summary>
/// Well-known source-system slugs referenced from the domain layer.
/// The set is deliberately small: only systems the domain has a
/// hard-coded rule for (Manual = "the DTMS UI", Oms = OMS-notify guards)
/// live here. Any other system is purely data in <c>iam.SystemClients</c>
/// and never appears as a string literal in code.
///
/// <para>Values MUST match the <c>iam.SystemClients.Key</c> slug format —
/// lowercase alphanumeric-and-dash — because middleware, permission
/// resolvers, and the URL <c>{key}</c> segment all pin on identical
/// string compares.</para>
/// </summary>
public static class WellKnownSourceSystems
{
    /// <summary>DTMS UI / operator-created orders.</summary>
    public const string Manual = "manual";

    /// <summary>Upstream OMS — OMS-notify guards pin on this exact slug.</summary>
    public const string Oms = "oms";

    /// <summary>SAP integration slug.</summary>
    public const string Sap = "sap";

    /// <summary>ERP integration slug.</summary>
    public const string Erp = "erp";
}
