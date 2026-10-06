namespace MongoFlow.Tests;

// A collection whose documents can be of a derived type.
public partial class UntypedCollectionTests
{
    public class Vehicle
    {
        public int Id { get; set; }
    }

    public sealed class Truck : Vehicle
    {
        public int Axles { get; set; }
    }

    public sealed class FleetVault : MongoVault
    {
        public IVaultCollection<Vehicle, int> Vehicles { get; init; } = null!;
    }
}
