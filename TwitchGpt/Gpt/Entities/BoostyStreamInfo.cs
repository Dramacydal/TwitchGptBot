using BoostyLib.Endpoints.Responses;

namespace TwitchGpt.Gpt.Entities;

public class BoostyStreamInfo : AbstractStreamInfo
{
    public VideoStreamResponse Stream { get; set; }

    public override string BuildMessage()
    {
        var msgText = "";

        if (Online)
        {
            msgText =
                "Это информация о стриме на Бусти, запомни ее, и используй ее, если тебя спросят о стриме на Бусти в чате\r\n" +
                "Стрим на Бусти сейчас онлайн, ты можешь по нему дать информацию и о том, что происходит на экране.\r\n" +
                $"Канал Бусти стримера: '{Stream.User.Name}'\r\n" +
                $"Название стрима на Бусти: '{Stream.Title}'\r\n" +
                // $"Описание стрима на Бусти: '{info.Stream.Description}'\r\n" +
                $"Количество зрителей на стриме Бусти: {Stream.Count.Viewers}\r\n" +
                $"Количество лайков на стриме Бусти: {Stream.Count.Likes}\r\n" +
                $"Время начала стрима на Бусти: {DateTime.UnixEpoch.AddSeconds(Stream.StartTime).ToLocalTime()}\r\n" +
                $"Текущее время: {DateTime.Now}\r\n";

            if (SnapShots.Count > 0)
            {
                msgText +=
                    "Прикрепляю файлом недавние кадры стрима на Boosty по которым ты можешь определить, что на нем происходит. Даты кадров: ";

                msgText += string.Join(", ", SnapShots.Select(s => s.Item1));
            }
        }
        else
        {
            msgText =
                "Стрим на Бусти сейчас оффлайн. Ты не можешь по нему дать информацию, и сказать, что сейчас на экране.\r\n";
        }

        return msgText;
    }
}
