using AMR.DeliveryPlanning.SharedKernel.Domain;

namespace AMR.DeliveryPlanning.Planning.Domain.Entities;

/// <summary>
/// Represents a dependency between two jobs (e.g., Cross-Dock: inbound must complete before outbound starts).
/// </summary>
public class JobDependency : Entity<Guid>
{
    public Guid PredecessorJobId { get; private set; }
    public Guid SuccessorJobId { get; private set; }
    public string DependencyType { get; private set; } = string.Empty;
    public TimeSpan? MinimumDwell { get; private set; }
    public DateTime CreatedAt { get; private set; }

    private JobDependency() { } // EF Core

    public JobDependency(Guid predecessorJobId, Guid successorJobId, string dependencyType, TimeSpan? minimumDwell = null)
    {
        Id = Guid.NewGuid();
        PredecessorJobId = predecessorJobId;
        SuccessorJobId = successorJobId;
        DependencyType = dependencyType;
        MinimumDwell = minimumDwell;
        CreatedAt = DateTime.UtcNow;
    }
}
