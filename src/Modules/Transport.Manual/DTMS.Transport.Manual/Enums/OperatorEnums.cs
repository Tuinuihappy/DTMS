namespace DTMS.Transport.Manual.Domain.Enums;

// Operator-app status — orthogonal to whether the operator has an
// active trip. "Active" = available for assignment; "OnLeave" = scheduled
// time off; "Deactivated" = no longer with the company / soft-delete.
public enum OperatorStatus
{
    Active = 0,
    OnLeave = 1,
    Deactivated = 2,
}

// Per ADR-014 — Operator role is read from External Auth JWT `role` claim.
// Reflected in DTMS only for filter / authz hints; canonical authorization
// happens via ASP.NET policy on the JWT, not on this enum.
public enum OperatorRole
{
    Operator = 0,        // standard warehouse staff
    Supervisor = 1,      // can approve geofence overrides for own warehouse
    Admin = 2,           // dispatcher console — also flagged as Admin in JWT
}

// Operator certifications — required for handling specific cargo types.
// Items / orders with matching hazard / temperature flags get filtered
// to only operators carrying the right cert at assignment time.
public enum CertificationType
{
    Hazmat = 0,           // hazardous materials handling
    ColdChain = 1,        // temperature-sensitive cargo
    Forklift = 2,         // heavy lifting equipment operation
    HighValue = 3,        // high-value cargo (jewelry, electronics)
}

// Push subscription platform — single table polymorphic by this column
// (per ADR-013). PWA today = WebPush; future RN migration adds Fcm + Apns.
public enum PushPlatform
{
    WebPush = 0,
    Fcm = 1,
    Apns = 2,
}

// Geofence override request lifecycle (per ADR-016).
public enum OverrideRequestStatus
{
    Pending = 0,        // operator submitted, dispatcher hasn't decided
    Approved = 1,
    Denied = 2,
    Expired = 3,        // dispatcher took too long; operator must re-request
}
