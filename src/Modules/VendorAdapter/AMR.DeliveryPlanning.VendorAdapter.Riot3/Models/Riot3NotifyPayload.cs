using System.Text.Json.Serialization;

namespace AMR.DeliveryPlanning.VendorAdapter.Riot3.Models;

// Matches RIOT3.0 API v4 callback — POST {our endpoint} /api/v4/notify
// Spec uses a nested structure: top-level event metadata + task / subTask
// / vehicleInfo objects depending on the notify type.
public class Riot3NotifyPayload
{
    // "taskNotify" | "subTaskNotify" | "vehicleNotify"
    [JsonPropertyName("type")]
    public string Type { get; set; } = string.Empty;

    // TASK_CREATE, TASK_FINISHED, TASK_FAILED, TASK_CANCELED, TASK_HANG,
    // TASK_HANG_TO_CONTINUE, TASK_HELD, TASK_HELD_TO_CONTINUE,
    // TASK_PROCESSING, TASK_REJECTED, TASK_QUEUEING,
    // SUB_TASK_PROCESSING, SUB_TASK_CANCELED, SUB_TASK_FAILED, SUB_TASK_FINISHED
    [JsonPropertyName("taskEventType")]
    public string? TaskEventType { get; set; }

    // VEHICLE_*, MODE_*, SYSTEM_*, MT_*
    [JsonPropertyName("vehicleEventType")]
    public string? VehicleEventType { get; set; }

    [JsonPropertyName("task")]
    public Riot3NotifyTask? Task { get; set; }

    [JsonPropertyName("subTask")]
    public Riot3NotifySubTask? SubTask { get; set; }

    [JsonPropertyName("vehicleInfo")]
    public Riot3NotifyVehicleInfo? VehicleInfo { get; set; }
}

public class Riot3NotifyTask
{
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    // RIOT-side order key
    [JsonPropertyName("key")]
    public string? Key { get; set; }

    // Our upper-system reference (we set this to TripId / JobId)
    [JsonPropertyName("upperKey")]
    public string? UpperKey { get; set; }

    [JsonPropertyName("orderSequenceKey")]
    public string? OrderSequenceKey { get; set; }

    [JsonPropertyName("orderType")]
    public string? OrderType { get; set; }

    [JsonPropertyName("state")]
    public string? State { get; set; }

    [JsonPropertyName("priority")]
    public int? Priority { get; set; }

    [JsonPropertyName("appointVehicleKey")]
    public string? AppointVehicleKey { get; set; }

    [JsonPropertyName("processingVehicle")]
    public Riot3NotifyProcessingVehicle? ProcessingVehicle { get; set; }

    [JsonPropertyName("progress")]
    public int? Progress { get; set; }

    [JsonPropertyName("changeStateTime")]
    public string? ChangeStateTime { get; set; }

    [JsonPropertyName("createTime")]
    public string? CreateTime { get; set; }

    [JsonPropertyName("finalTime")]
    public string? FinalTime { get; set; }

    [JsonPropertyName("hangReason")]
    public string? HangReason { get; set; }

    [JsonPropertyName("cancelReason")]
    public string? CancelReason { get; set; }

    [JsonPropertyName("failReason")]
    public Riot3FailResult? FailReason { get; set; }

    [JsonPropertyName("tags")]
    public List<string>? Tags { get; set; }
}

public class Riot3NotifySubTask
{
    [JsonPropertyName("key")]
    public string? Key { get; set; }

    // Reference back to the parent task (RIOT-side order key)
    [JsonPropertyName("taskKey")]
    public string? TaskKey { get; set; }

    // "MOVE" | "ACT"
    [JsonPropertyName("subTaskType")]
    public string? SubTaskType { get; set; }

    [JsonPropertyName("category")]
    public string? Category { get; set; }

    [JsonPropertyName("actionType")]
    public string? ActionType { get; set; }

    [JsonPropertyName("actionName")]
    public string? ActionName { get; set; }

    [JsonPropertyName("actionDescription")]
    public string? ActionDescription { get; set; }

    [JsonPropertyName("state")]
    public string? State { get; set; }

    [JsonPropertyName("failResult")]
    public Riot3FailResult? FailResult { get; set; }

    [JsonPropertyName("actResult")]
    public Riot3ActResult? ActResult { get; set; }

    [JsonPropertyName("station")]
    public Riot3NotifyStation? Station { get; set; }

    [JsonPropertyName("startedTime")]
    public string? StartedTime { get; set; }

    [JsonPropertyName("finishedTime")]
    public string? FinishedTime { get; set; }
}

public class Riot3NotifyStation
{
    // Spec wraps a "station" object inside subTask.station; the inner
    // object carries id/name/coordinates. Surface the common fields.
    [JsonPropertyName("station")]
    public Riot3StationInfo? Station { get; set; }
}

public class Riot3StationInfo
{
    [JsonPropertyName("id")]
    public int? Id { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("type")]
    public int? Type { get; set; }
}

public class Riot3NotifyProcessingVehicle
{
    [JsonPropertyName("key")]
    public string? Key { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }
}

public class Riot3NotifyVehicleInfo
{
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("key")]
    public string? Key { get; set; }

    [JsonPropertyName("operatingMode")]
    public string? OperatingMode { get; set; }

    [JsonPropertyName("systemState")]
    public string? SystemState { get; set; }

    [JsonPropertyName("connectionState")]
    public string? ConnectionState { get; set; }

    [JsonPropertyName("paused")]
    public bool? Paused { get; set; }

    [JsonPropertyName("driving")]
    public bool? Driving { get; set; }

    [JsonPropertyName("batteryState")]
    public Riot3NotifyBatteryState? BatteryState { get; set; }

    [JsonPropertyName("safetyState")]
    public Riot3NotifySafetyState? SafetyState { get; set; }

    [JsonPropertyName("errors")]
    public List<Riot3NotifyError>? Errors { get; set; }
}

public class Riot3NotifyBatteryState
{
    // Percentage 0-100 (spec uses double)
    [JsonPropertyName("batteryCharge")]
    public double? BatteryCharge { get; set; }

    [JsonPropertyName("batteryVoltage")]
    public double? BatteryVoltage { get; set; }

    [JsonPropertyName("batteryHealth")]
    public int? BatteryHealth { get; set; }

    [JsonPropertyName("charging")]
    public bool? Charging { get; set; }

    [JsonPropertyName("reach")]
    public int? Reach { get; set; }
}

public class Riot3NotifySafetyState
{
    // "AUTOACK" | "MANUAL" | "REMOTE" | "NONE"
    [JsonPropertyName("eStop")]
    public string? EStop { get; set; }

    [JsonPropertyName("fieldViolation")]
    public bool? FieldViolation { get; set; }
}

public class Riot3NotifyError
{
    [JsonPropertyName("errorType")]
    public string? ErrorType { get; set; }

    [JsonPropertyName("errorCode")]
    public string? ErrorCode { get; set; }

    [JsonPropertyName("errorDescription")]
    public string? ErrorDescription { get; set; }

    [JsonPropertyName("errorLevel")]
    public string? ErrorLevel { get; set; }
}

public class Riot3FailResult
{
    [JsonPropertyName("errorCode")]
    public string? ErrorCode { get; set; }

    [JsonPropertyName("errorDescription")]
    public string? ErrorDescription { get; set; }

    [JsonPropertyName("resultCode")]
    public string? ResultCode { get; set; }

    [JsonPropertyName("handleErrorCode")]
    public string? HandleErrorCode { get; set; }

    [JsonPropertyName("handleErrorDepict")]
    public string? HandleErrorDepict { get; set; }
}

public class Riot3ActResult
{
    [JsonPropertyName("code")]
    public string? Code { get; set; }

    [JsonPropertyName("resultDescription")]
    public string? ResultDescription { get; set; }
}
