namespace AMR.DeliveryPlanning.Api.VendorHealth;

public interface IVendorHealthStore
{
    VendorHealthSnapshot? Get(string vendor);

    void Update(VendorHealthSnapshot snapshot);

    event EventHandler<VendorHealthSnapshot> StatusChanged;
}
