namespace MongoFlow.Samples.Configuration;

public sealed class CustomerOptions
{
    /// <summary>Erase customers for real on delete, as data-protection law requires in some markets.</summary>
    public bool HardDeleteCustomers { get; set; }
}
