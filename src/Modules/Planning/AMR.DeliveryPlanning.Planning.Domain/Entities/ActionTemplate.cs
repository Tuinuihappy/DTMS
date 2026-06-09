using AMR.DeliveryPlanning.Planning.Domain.Enums;
using AMR.DeliveryPlanning.SharedKernel.Domain;

namespace AMR.DeliveryPlanning.Planning.Domain.Entities;

// Mirrors the RIOT3 "action template" catalog entry — the named recipe a robot
// executes for one ACT mission. Each template captures the vendor's
// actionType + parameter quartet (id, param0, param1, optional string).
//
// Identified by Name (case-insensitive, unique) because:
//   - Operations team works with human-readable names in templates
//   - OrderTemplate.Steps reference action templates by Name, not Guid,
//     so exports/imports stay portable across environments
//
// The entity matches the RIOT3 POST /api/v4/order/action-templates payload:
//   { actionName, actionType, actionParameters: [{key,value},...] }
// but stores the four well-known parameter slots (id, param0, param1,
// paramStr) as columns to keep validation + queries simple. The
// actionParameters array can be reconstructed on the way out to the vendor.
public class ActionTemplate : AggregateRoot<Guid>
{
    // Default RIOT3 wire `actionType` for "custom" templates that pack their
    // logic into the id+param0+param1 quartet. Centralised here so the domain
    // owns the default rather than scattering literals across the API/frontend.
    public const string DefaultActionType = "standardRobotsCustom";

    public string Name { get; private set; } = string.Empty;
    // DTMS-local category (STD/ACT) — coarse grouping used in queries +
    // UI badges. Distinct from `ActionType` below, which is the RIOT3
    // wire string.
    public ActionCategory ActionCategory { get; private set; } = ActionCategory.Std;
    // The literal `actionType` string DTMS sends to RIOT3 on every ACT
    // mission resolved from this template (e.g. "standardRobotsCustom").
    // Matches the RIOT3 field name exactly so the wire shape round-trips
    // through DTMS without renaming.
    public string ActionType { get; private set; } = DefaultActionType;
    public int VendorActionId { get; private set; }
    public int Param0 { get; private set; }
    public int Param1 { get; private set; }
    public string? ParamStr { get; private set; }
    public bool IsActive { get; private set; } = true;
    public DateTime CreatedAt { get; private set; }
    public DateTime? ModifiedAt { get; private set; }
    public string? CreatedBy { get; private set; }
    public string? ModifiedBy { get; private set; }

    private ActionTemplate() { } // EF Core

    public ActionTemplate(
        string name,
        ActionCategory actionCategory,
        int vendorActionId,
        int param0,
        int param1,
        string? paramStr = null,
        string? createdBy = null,
        string? actionType = null)
    {
        Id = Guid.NewGuid();
        SetName(name);
        ActionCategory = actionCategory;
        SetActionType(actionType);
        VendorActionId = vendorActionId;
        Param0 = param0;
        Param1 = param1;
        ParamStr = string.IsNullOrWhiteSpace(paramStr) ? null : paramStr.Trim();
        IsActive = true;
        CreatedAt = DateTime.UtcNow;
        CreatedBy = NormalizeUser(createdBy);
    }

    public void Update(
        ActionCategory actionCategory,
        int vendorActionId,
        int param0,
        int param1,
        string? paramStr,
        string? modifiedBy = null,
        string? actionType = null)
    {
        ActionCategory = actionCategory;
        SetActionType(actionType);
        VendorActionId = vendorActionId;
        Param0 = param0;
        Param1 = param1;
        ParamStr = string.IsNullOrWhiteSpace(paramStr) ? null : paramStr.Trim();
        ModifiedAt = DateTime.UtcNow;
        ModifiedBy = NormalizeUser(modifiedBy);
    }

    public void Rename(string newName, string? modifiedBy = null)
    {
        // Name change is allowed but callers must update any OrderTemplate
        // references separately — Repository uniqueness check guards the new name.
        SetName(newName);
        ModifiedAt = DateTime.UtcNow;
        ModifiedBy = NormalizeUser(modifiedBy);
    }

    public void Activate(string? modifiedBy = null)
    {
        if (IsActive) return;
        IsActive = true;
        ModifiedAt = DateTime.UtcNow;
        ModifiedBy = NormalizeUser(modifiedBy);
    }

    public void Deactivate(string? modifiedBy = null)
    {
        if (!IsActive) return;
        IsActive = false;
        ModifiedAt = DateTime.UtcNow;
        ModifiedBy = NormalizeUser(modifiedBy);
    }

    private void SetName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("ActionTemplate Name must not be empty.", nameof(name));
        if (name.Length > 100)
            throw new ArgumentException("ActionTemplate Name must be 100 characters or fewer.", nameof(name));
        Name = name.Trim();
    }

    // Null/blank collapses to the default — keeps the API surface friendly
    // (operators don't have to think about this field for the common case).
    private void SetActionType(string? value)
    {
        var v = string.IsNullOrWhiteSpace(value) ? DefaultActionType : value.Trim();
        if (v.Length > 50)
            throw new ArgumentException("ActionType must be 50 characters or fewer.", nameof(value));
        ActionType = v;
    }

    private static string? NormalizeUser(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
