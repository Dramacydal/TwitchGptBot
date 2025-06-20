namespace TwitchGpt.Api;

public abstract class ApiCaller<TApi>
{
    public TApi Api { get; init; }
    
    public async Task<T> Call<T>(Func<TApi, Task<T>> api)
    {
        for (var i = 0; i < 3; ++i)
        {
            try
            {
                return await api.Invoke(Api);
            }
            catch (Exception ex)
            {
                if (IsCredentialsException(ex))
                    await ReloadCredentials();
                else
                    throw;
            }
        }

        throw new Exception("Api query failed after 3 tries");
    }
    
    public async Task Call(Func<TApi, Task> api)
    {
        for (var i = 0; i < 3; ++i)
        {
            try
            {
                await api.Invoke(Api);
                return;
            }
            catch (Exception ex)
            {
                if (IsCredentialsException(ex))
                    await ReloadCredentials();
                else
                    throw;
            }
        }

        throw new Exception("Api query failed after 3 tries");
    }
    
    protected abstract bool IsCredentialsException(Exception exception);

    protected abstract Task ReloadCredentials();
}
