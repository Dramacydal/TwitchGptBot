using MongoDB.Driver;
using TwitchGpt.Database.Mappers.Abstraction;
using TwitchGpt.Database.Mongo;
using TwitchGpt.TwitchRoutines.Entities;

namespace TwitchGpt.Database.Mappers;

public class AnnouncementMapper : AbstractMapper<AnnouncementMapper, MongoConnection>
{
    public override MongoConnection Connection => MongoConnectionManager.GetClient("mongo_twitch");

    private readonly string _collectionName = "announcements";

    public async Task<ChannelAnnouncementData> Save(ChannelAnnouncementData announcementData)
    {
        return await Connection.GetCollection<ChannelAnnouncementData>(_collectionName)
            .FindOneAndReplaceAsync(e => e.ChannelId == announcementData.ChannelId, announcementData,
                new FindOneAndReplaceOptions<ChannelAnnouncementData> { IsUpsert = true });
    }

    public async Task<ChannelAnnouncementData?> GetAnnouncementData(string channelId)
    {
        var coll = Connection.GetCollection<ChannelAnnouncementData>(_collectionName);
        var res = await coll.FindAsync(e => e.ChannelId == channelId);

        return res.FirstOrDefault();
    }
}
