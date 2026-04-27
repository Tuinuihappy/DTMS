namespace AMR.DeliveryPlanning.Dispatch.Domain.Enums;

public enum TripStatus { Created, InProgress, Paused, Completed, Failed, Cancelled }
public enum TaskStatus { Pending, Dispatched, InProgress, Completed, Failed, Skipped }
public enum TaskType { Move, Lift, Drop, Charge, Park }
