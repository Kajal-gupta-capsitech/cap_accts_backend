using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace Capsitech.Data.MongoDB
{
    [BsonIgnoreExtraElements]
    public abstract class RecordBase
    {
        [BsonId]
        [BsonRepresentation(BsonType.ObjectId)]
        public string Id { get; set; } = string.Empty;

        [BsonIgnoreIfDefault]
        public bool IsDeleted { get; set; } = false;

        [BsonIgnoreIfNull]
        public DateTime? DeletedAt { get; set; }

        [BsonIgnoreIfNull]
        public DateTime? CreatedAt { get; set; } = DateTime.UtcNow;

        [BsonIgnoreIfNull]
        public DateTime? UpdatedAt { get; set; }
    }
}
