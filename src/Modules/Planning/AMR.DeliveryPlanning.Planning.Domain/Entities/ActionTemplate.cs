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
    public string ActionType { get; private set; } = "STD";
    public int VendorActionId { get; private set; }
    public int Param0 { get; private set; }
    public int Param1 { get; private set; }
    public string? ParamStr { get; private set; }
    public string? Description { get; private set; }
    public bool IsActive { get; private set; } = true;
    public DateTime CreatedAt { get; private set; }
    public DateTime? ModifiedAt { get; private set; }

    private ActionTemplate() { } // EF Core

    public ActionTemplate(
        string name,
        string actionType,
        int vendorActionId,
        int param0,
        int param1,
        string? paramStr = null,
        string? description = null)
    {
        Id = Guid.NewGuid();
        SetName(name);
        SetActionType(actionType);
        VendorActionId = vendorActionId;
        Param0 = param0;
        Param1 = param1;
        ParamStr = string.IsNullOrWhiteSpace(paramStr) ? null : paramStr.Trim();
        Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim();
        IsActive = true;
        CreatedAt = DateTime.UtcNow;
    }

    public void Update(
        string actionType,
        int vendorActionId,
        int param0,
        int param1,
        string? paramStr,
        string? description)
    {
        SetActionType(actionType);
        VendorActionId = vendorActionId;
        Param0 = param0;
        Param1 = param1;
        ParamStr = string.IsNullOrWhiteSpace(paramStr) ? null : paramStr.Trim();
        Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim();
        ModifiedAt = DateTime.UtcNow;
    }

    public void Rename(string newName)
    {
        // Name change is allowed but callers must update any OrderTemplate
        // references separately — Repository uniqueness check guards the new name.
        SetName(newName);
        ModifiedAt = DateTime.UtcNow;
    }

    public void Activate()
    {
        if (IsActive) return;
        IsActive = true;
        ModifiedAt = DateTime.UtcNow;
    }

    public void Deactivate()
    {
        if (!IsActive) return;
        IsActive = false;
        ModifiedAt = DateTime.UtcNow;
    }

    private void SetName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("ActionTemplate Name must not be empty.", nameof(name));
        if (name.Length > 100)
            throw new ArgumentException("ActionTemplate Name must be 100 characters or fewer.", nameof(name));
        Name = name.Trim();
    }

    private void SetActionType(string actionType)
    {
        if (string.IsNullOrWhiteSpace(actionType))
            throw new ArgumentException("ActionType must not be empty.", nameof(actionType));
        if (actionType.Length > 50)
            throw new ArgumentException("ActionType must be 50 characters or fewer.", nameof(actionType));
        ActionType = actionType.Trim();
    }
}
