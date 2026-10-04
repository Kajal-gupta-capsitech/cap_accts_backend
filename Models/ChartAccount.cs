using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace BackendAcctTask.Models;

[BsonIgnoreExtraElements]
public class ChartAccount
{
    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    public string Id { get; set; } = string.Empty;

    [BsonElement("code")]
    public string Code { get; set; } = string.Empty;

    [BsonElement("accountName")]
    public string AccountName { get; set; } = string.Empty;

    // Foreign key -> AccountType
    [BsonElement("accountTypeId")]
    [BsonRepresentation(BsonType.ObjectId)]
    public string AccountTypeId { get; set; } = string.Empty;

    [BsonElement("accountGroup")]
    public string AccountGroup { get; set; } = string.Empty;

    [BsonElement("forClients")]
    public bool ForClients { get; set; }

    [BsonElement("archive")]
    public bool Archive { get; set; }
}