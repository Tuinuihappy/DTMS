namespace DTMS.Api.VendorHealth;

public interface IVendorHealthStore
{
    VendorHealthSnapshot? Get(string vendor);

    IReadOnlyCollection<VendorHealthSnapshot> GetAll();

    void Update(VendorHealthSnapshot snapshot);

    event EventHandler<VendorHealthSnapshot> StatusChanged;
}
