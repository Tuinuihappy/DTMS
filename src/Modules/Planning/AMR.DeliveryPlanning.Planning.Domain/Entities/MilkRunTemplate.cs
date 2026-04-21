using AMR.DeliveryPlanning.SharedKernel.Domain;

namespace AMR.DeliveryPlanning.Planning.Domain.Entities;

/// <summary>
/// A recurring route template for Milk-Run deliveries.
/// Contains fixed stops + time windows that get instantiated into Jobs on a schedule.
/// </summary>
public class MilkRunTemplate : AggregateRoot<Guid>
{
    public string TemplateName { get; private set; } = string.Empty;
    public string CronSchedule { get; private set; } = string.Empty;
    public bool IsActive { get; private set; }
    public DateTime CreatedAt { get; private set; }

    private readonly List<MilkRunStop> _stops = new();
    public IReadOnlyCollection<MilkRunStop> Stops => _stops.AsReadOnly();

    private MilkRunTemplate() { } // EF Core

    public MilkRunTemplate(string templateName, string cronSchedule)
    {
        Id = Guid.NewGuid();
        TemplateName = templateName;
        CronSchedule = cronSchedule;
        IsActive = true;
        CreatedAt = DateTime.UtcNow;
    }

    public void AddStop(Guid stationId, int sequenceOrder, TimeSpan? plannedArrivalOffset, TimeSpan dwellTime)
    {
        _stops.Add(new MilkRunStop(Id, stationId, sequenceOrder, plannedArrivalOffset, dwellTime));
    }

    public void Deactivate() => IsActive = false;
    public void Activate() => IsActive = true;
}

/// <summary>
/// A stop within a Milk-Run template.
/// </summary>
public class MilkRunStop : Entity<Guid>
{
    public Guid TemplateId { get; private set; }
    public Guid StationId { get; private set; }
    public int SequenceOrder { get; private set; }
    public TimeSpan? PlannedArrivalOffset { get; private set; }
    public TimeSpan DwellTime { get; private set; }

    private MilkRunStop() { }

    internal MilkRunStop(Guid templateId, Guid stationId, int sequenceOrder, TimeSpan? plannedArrivalOffset, TimeSpan dwellTime)
    {
        Id = Guid.NewGuid();
        TemplateId = templateId;
        StationId = stationId;
        SequenceOrder = sequenceOrder;
        PlannedArrivalOffset = plannedArrivalOffset;
        DwellTime = dwellTime;
    }
}
