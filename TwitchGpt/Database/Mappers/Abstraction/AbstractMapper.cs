namespace TwitchGpt.Database.Mappers.Abstraction;

public abstract class AbstractMapper<T, TC> where T : new()
{
    public abstract TC Connection { get; }
    
    private static T? _instance;

    public static T Instance
    {
        get
        {
            if (_instance != null)
                return _instance;

            _instance = new T();

            return _instance;
        }
    }
}
