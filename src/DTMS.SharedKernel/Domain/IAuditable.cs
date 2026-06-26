namespace AMR.DeliveryPlanning.SharedKernel.Domain;

public interface IAuditable
{
    DateTime CreatedDate { get; }
    DateTime? UpdatedDate { get; }
    void SetCreatedAt(DateTime createdAt);
    void SetUpdatedAt(DateTime updatedAt);
}
