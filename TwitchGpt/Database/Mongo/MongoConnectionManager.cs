using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.RegularExpressions;
using MongoDB.Bson.Serialization;
using MongoDB.Bson.Serialization.Conventions;
using TwitchGpt.Config;

namespace TwitchGpt.Database.Mongo;

public static class MongoConnectionManager
{
    class MyConvenction : IMemberMapConvention
    {
        public string Name => nameof(MyConvenction);

        public void Apply(BsonMemberMap memberMap)
        {
            var name = Regex.Replace(memberMap.MemberName, "([^A-Z])([A-Z])",
                match => $"{match.Groups[1].Value}_{match.Groups[2].Value}");
            memberMap.SetElementName(name.ToLower());
        }
    }

    static MongoConnectionManager()
    {
        var myConventions = new ConventionPack();
        myConventions.Add(new MyConvenction());
        myConventions.Add(new IgnoreExtraElementsConvention(true));

        ConventionRegistry.Register(
            "My Custom Conventions",
            myConventions,
            t => true);

        // only then apply the mapping
        // BsonClassMap.RegisterClassMap<Foo>(cm => { cm.AutoMap(); });
    }

    private static ConcurrentDictionary<string, MongoConnection> connections = new();

    public static MongoConnection GetClient(string section)
    {
        if (connections.TryGetValue(section, out var result))
            return result;

        var conn = new MongoConnection(ConfigManager.GetPath<MongoConfig>($"db.{section}")!);
        connections[section] = conn;

        return conn;
    }
}
