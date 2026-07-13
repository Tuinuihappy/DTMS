namespace DTMS.DeliveryOrder.Domain;

/// <summary>
/// Well-known source-system slugs referenced from the domain layer.
/// The set is deliberately small: only systems the domain has a
/// hard-coded rule for (Internal = "the DTMS UI / operator", Oms =
/// OMS-notify guards) live here. Any other system is purely data in
/// <c>iam.SystemClients</c> and never appears as a string literal in code.
///
/// <para>Values MUST match the <c>iam.SystemClients.Key</c> slug format —
/// lowercase alphanumeric-and-dash — because middleware, permission
/// resolvers, and the URL <c>{key}</c> segment all pin on identical
/// string compares.</para>
/// </summary>
public static class WellKnownSourceSystems
{
    /// <summary>DTMS UI / operator-created orders. An internal origin, not an
    /// external system — the operator is recorded on the order's CreatedBy,
    /// and this slug marks the order as raised inside DTMS rather than by a
    /// federated source system.</summary>
    public const string Internal = "internal";

    /// <summary>Display name stamped alongside <see cref="Internal"/>. There is
    /// no iam.SystemClients row for an internal origin, so this constant is the
    /// name.</summary>
    public const string InternalDisplayName = "Internal";

    /// <summary>Upstream OMS — OMS-notify guards pin on this exact slug.</summary>
    public const string Oms = "oms";

    /// <summary>SAP integration slug.</summary>
    public const string Sap = "sap";

    /// <summary>ERP integration slug.</summary>
    public const string Erp = "erp";
}
