namespace MongoFlow;

public sealed class DisableContext
{
    private DisableContext(string[] disabledItems,
        bool disableAll)
    {
        DisabledItems = disabledItems;
        AllDisabled = disableAll;
    }
    
    public string[] DisabledItems { get; }
    public bool AllDisabled { get; }
    
    public static readonly DisableContext Empty = new([], false);
    public static readonly DisableContext All = new([], true);
    
    public DisableContext Disable(string[] items)
    {
        if (AllDisabled) return this;
        
        var disabledItems = DisabledItems.Concat(items).Distinct().ToArray();
        return new DisableContext(disabledItems, false);
    }
}