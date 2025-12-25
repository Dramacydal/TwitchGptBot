using TwitchLib.Api.Helix.Models.Users.GetUsers;

namespace TwitchGpt.TwitchRoutines.Entities;

public class Condition
{
    public ConditionType Type { get; set; }

    public object? Value1 { get; set; }
    
    public object? Value2 { get; set; }
    
    public object? Value3 { get; set; }
    
    public object? Value4 { get; set; }
    
    public bool Negate { get; set; }

    public async Task<bool> IsMatch(Bot bot, User user)
    {
        bool match;
        switch (Type)
        {
            case ConditionType.None:
                return true;
            case ConditionType.TwitchStreamOnline:
                match = await TwitchOnline(bot, user);
                break;
            case ConditionType.BoostyStreamOnline:
                match = await BoostyOnline(bot, user);
                break;
            default:
                return false;
        }

        return Negate ? !match : match;
    }

    private async Task<bool> BoostyOnline(Bot bot, User user)
    {
        try
        {
            if (bot.BoostyApi == null)
                return false;

            var stream = await bot.BoostyApi.Call(api => api.VideoStream.Get(bot.BoostyClient!.ChannelName));

            return stream != null;
        }
        catch (Exception ex)
        {
            return false;
        }
    }

    private async Task<bool> TwitchOnline(Bot bot, User user)
    {
        try
        {
            var stream = await bot.TwitchApi.Call(api => api.Helix.Streams.GetStreamsAsync(userIds: [user.Id]));

            return stream != null && stream.Streams.Length > 0;
        }
        catch (Exception ex)
        {
            return false;
        }
    }
}