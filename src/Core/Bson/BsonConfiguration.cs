using MongoDB.Bson.Serialization;
using MongoFlow.Bson.Serializers;

namespace MongoFlow.Bson;

internal static class BsonConfiguration
{
    public static void Configure()
    {
        BsonClassMap.TryRegisterClassMap<MigrationDocument>(map =>
        {
            map.AutoMap();
            map.MapProperty(x => x.Version).SetSerializer(new SemVersionSerializer());
        });
    }
    
}