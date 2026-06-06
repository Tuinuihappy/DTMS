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
    public string Name { get; private set; } = string.Empty;
    public ActionType ActionType { get; private set; } = ActionType.Std;
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
        ActionType actionType,
        int vendorActionId,
        int param0,
        int param1,
        string? paramStr = null,
        string? createdBy = null)
    {
        Id = Guid.NewGuid();
        SetName(name);
        ActionType = actionType;
        VendorActionId = vendorActionId;
        Param0 = param0;
        Param1 = param1;
        ParamStr = string.IsNullOrWhiteSpace(paramStr) ? null : paramStr.Trim();
        IsActive = true;
        CreatedAt = DateTime.UtcNow;
        CreatedBy = NormalizeUser(createdBy);
    }

    public void Update(
        ActionType actionType,
        int vendorActionId,
        int param0,
        int param1,
        string? paramStr,
        string? modifiedBy = null)
    {
        ActionType = actionType;
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

    private static string? NormalizeUser(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
