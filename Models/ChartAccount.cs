using BackendAcctTask.Models;
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using MongoDB.Driver;

namespace BackendAcctTask.Models;

[BsonIgnoreExtraElements]
public class ChartAccount
{
    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    public string Id { get; set; } = string.Empty;

    public string Code { get; set; } = string.Empty;

    public string AccountName { get; set; } = string.Empty;

    public string AccountTypeId { get; set; } = string.Empty;

    public string AccountGroup { get; set; } = string.Empty;

    public bool ForClients { get; set; }

    public bool Archive { get; set; }

    [BsonIgnoreIfDefault]
    public bool IsDeleted { get; set; } = false;

    [BsonIgnoreIfNull]
    public DateTime? DeletedAt { get; set; }
}

public static class ChartAccountDB
{
    private static IMongoCollection<ChartAccount> GetCollection(IMongoDatabase database)
    {
        return database.GetCollection<ChartAccount>("ChartAccounts");
    }

    // CHECK IF ACCOUNT NAME EXISTS (Returns bool)
    public static async Task<bool> AccountNameExistsAsync(IMongoDatabase database, string accountName)
    {
        var existing = await GetCollection(database)
            .Find(x => x.AccountName == accountName && !x.IsDeleted)
            .Limit(1)
            .FirstOrDefaultAsync();

        return existing != null;
    }

    // GET ALL (Excluding soft-deleted records)
    public static async Task<List<ChartAccount>> GetAllAsync(IMongoDatabase database)
    {
        return await GetCollection(database)
            .Find(x => !x.IsDeleted)
            .ToListAsync();
    }

    // GET BY ID
    public static async Task<ChartAccount?> GetByIdAsync(IMongoDatabase database, string id)
    {
        return await GetCollection(database)
            .Find(x => x.Id == id && !x.IsDeleted)
            .FirstOrDefaultAsync();
    }

    // GET NEXT CODE
    public static async Task<string> GetNextCodeAsync(IMongoDatabase database)
    {
        var lastChartAccount = await GetCollection(database)
            .Find(x => !x.IsDeleted)
            .SortByDescending(x => x.Code)
            .FirstOrDefaultAsync();

        if (lastChartAccount == null)
        {
            return "1/1";
        }

        var codeParts = lastChartAccount.Code.Split('/');

        if (codeParts.Length != 2 ||
            !int.TryParse(codeParts[1], out int lastNumber))
        {
            return "1/1";
        }

        return $"1/{lastNumber + 1}";
    }

    // CREATE
    public static async Task CreateAsync(IMongoDatabase database, ChartAccount chartAccount)
    {
        chartAccount.Code = await GetNextCodeAsync(database);
        await GetCollection(database).InsertOneAsync(chartAccount);
    }

    // UPDATE
    public static async Task<bool> UpdateAsync(
        IMongoDatabase database,
        string id,
        UpdateChartAccountRequest request)
    {
        var collection = GetCollection(database);
        var updates = new List<UpdateDefinition<ChartAccount>>();

        if (request.AccountName != null)
        {
            updates.Add(Builders<ChartAccount>.Update.Set(x => x.AccountName, request.AccountName));
        }

        if (request.AccountTypeId != null)
        {
            updates.Add(Builders<ChartAccount>.Update.Set(x => x.AccountTypeId, request.AccountTypeId));
        }

        if (request.AccountGroup != null)
        {
            updates.Add(Builders<ChartAccount>.Update.Set(x => x.AccountGroup, request.AccountGroup));
        }

        if (request.ForClients.HasValue)
        {
            updates.Add(Builders<ChartAccount>.Update.Set(x => x.ForClients, request.ForClients.Value));
        }

        if (request.Archive.HasValue)
        {
            updates.Add(Builders<ChartAccount>.Update.Set(x => x.Archive, request.Archive.Value));
        }

        if (updates.Count == 0)
        {
            return false;
        }

        var combinedUpdate = Builders<ChartAccount>.Update.Combine(updates);

        var filter = Builders<ChartAccount>.Filter.And(
            Builders<ChartAccount>.Filter.Eq(x => x.Id, id),
            Builders<ChartAccount>.Filter.Where(x => !x.IsDeleted)
        );

        var result = await collection.UpdateOneAsync(filter, combinedUpdate);

        return result.MatchedCount > 0;
    }

    // SOFT DELETE
    public static async Task<bool> DeleteAsync(IMongoDatabase database, string id)
    {
        var collection = GetCollection(database);

        var filter = Builders<ChartAccount>.Filter.And(
            Builders<ChartAccount>.Filter.Eq(x => x.Id, id),
            Builders<ChartAccount>.Filter.Where(x => !x.IsDeleted)
        );

        var update = Builders<ChartAccount>.Update
            .Set(x => x.IsDeleted, true)
            .Set(x => x.DeletedAt, DateTime.UtcNow);

        var result = await collection.UpdateOneAsync(filter, update);

        return result.ModifiedCount > 0;
    }
}