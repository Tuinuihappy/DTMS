namespace AMR.DeliveryPlanning.SharedKernel.Domain;

public interface IAuditable
{
    DateTime CreatedAt { get; }
    DateTime? UpdatedAt { get; }
    void SetCreatedAt(DateTime createdAt);
    void SetUpdatedAt(DateTime updatedAt);
}
