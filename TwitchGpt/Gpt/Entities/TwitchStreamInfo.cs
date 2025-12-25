using Stream = TwitchLib.Api.Helix.Models.Streams.GetStreams.Stream;

namespace TwitchGpt.Gpt.Entities;

public class TwitchStreamInfo : AbstractStreamInfo
{
    public Stream Stream { get; set; }
    
    public List<string>? AvailableBttvEmotes;

    public override string BuildMessage()
    {
        var msgText = "";

        var emoteLine = "Доступные на канале BetterTTV Emotes: ";
        if (AvailableBttvEmotes != null && AvailableBttvEmotes.Count > 0)
            emoteLine += string.Join(", ", AvailableBttvEmotes);
        else
            emoteLine += "нет";

        if (Online)
        {
            msgText =
                "Это информация о стриме на Twitch, запомни ее, и используй ее, если тебя спросят о стриме на Twitch в чате\r\n" +
                "Стрим на Twitch сейчас онлайн, ты можешь по нему дать информацию и о том, что происходит на экране.\r\n" +
                $"Канал Twitch стримера: '{Stream.UserName}'\r\n" +
                $"Название стрима на Twitch: '{Stream.Title}'\r\n" +
                $"Категория (игра) на стриме Twitch: {Stream.GameName}\r\n" +
                $"Теги стрима Twitch: {string.Join(", ", Stream.Tags)}\r\n" +
                $"Количество зрителей на Twitch стриме: {Stream.ViewerCount}\r\n" +
                $"Время начала Twitch стрима: {Stream.StartedAt.ToLocalTime()}\r\n" +
                $"Текущее время: {DateTime.Now}\r\n" +
                emoteLine + "\r\n";

            if (SnapShots.Count > 0)
            {
                msgText +=
                    "Прикрепляю файлом недавние кадры стрима на Twitch по которым ты можешь определить, что на нем происходит. Даты кадров: ";

                msgText += string.Join(", ", SnapShots.Select(s => s.Item1));
            }
        }
        else
        {
            msgText =
                "Стрим на Twitch сейчас оффлайн. Ты не можешь по нему дать информацию, и сказать, что сейчас на экране.\r\n" +
                emoteLine;
        }

        return msgText;
    }
}
