using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using MongoDB.Driver;

namespace BackendAcctTask.Models;

[BsonIgnoreExtraElements]
public class AccountingPeriod
{
    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    public string Id { get; set; } = string.Empty;

    public DateTime PeriodFrom { get; set; }

    public DateTime PeriodTo { get; set; }

    public bool IsActive { get; set; } = true;

    public bool IsClosed { get; set; } = false;

    [BsonIgnoreIfDefault]
    public bool IsDeleted { get; set; } = false;

    [BsonIgnoreIfNull]
    public DateTime? DeletedAt { get; set; }
}

/// <summary>
/// Saare Database operations ab is class ke andar aa jayenge, 
/// jisse aapko alag se Services folder ki zaroorat nahi padegi.
/// </summary>
public static class AccountingPeriodDB
{
    private static IMongoCollection<AccountingPeriod> GetCollection(IMongoDatabase database)
    {
        return database.GetCollection<AccountingPeriod>("AccountingPeriods");
    }

    // GET ALL
    public static async Task<List<AccountingPeriod>> GetAllAsync(IMongoDatabase database)
    {
        return await GetCollection(database)
            .Find(x => !x.IsDeleted)
            .ToListAsync();
    }

    // GET BY ID
    public static async Task<AccountingPeriod?> GetByIdAsync(IMongoDatabase database, string id)
    {
        return await GetCollection(database)
            .Find(x => x.Id == id && !x.IsDeleted)
            .FirstOrDefaultAsync();
    }

    // CREATE
    public static async Task CreateAsync(IMongoDatabase database, AccountingPeriod accountingPeriod)
    {
        await GetCollection(database).InsertOneAsync(accountingPeriod);
    }

    // PARTIAL UPDATE
    public static async Task<bool> UpdateAsync(
        IMongoDatabase database,
        string id,
        UpdateAccountingPeriodRequest request)
    {
        var collection = GetCollection(database);
        var updates = new List<UpdateDefinition<AccountingPeriod>>();

        if (request.PeriodFrom.HasValue)
        {
            updates.Add(Builders<AccountingPeriod>
            .Update.Set(x => x.PeriodFrom, request.PeriodFrom.Value));
        }

        if (request.PeriodTo.HasValue)
        {
            updates.Add(Builders<AccountingPeriod>.Update.Set(x => x.PeriodTo, request.PeriodTo.Value));
        }

        if (request.IsActive.HasValue)
        {
            updates.Add(Builders<AccountingPeriod>.Update.Set(x => x.IsActive, request.IsActive.Value));
        }

        if (request.IsClosed.HasValue)
        {
            updates.Add(Builders<AccountingPeriod>.Update.Set(x => x.IsClosed, request.IsClosed.Value));
        }

        if (updates.Count == 0)
        {
            return false;
        }

        var combinedUpdate = Builders<AccountingPeriod>.Update.Combine(updates);

        var result = await collection.UpdateOneAsync(
            x => x.Id == id && !x.IsDeleted,
            combinedUpdate);

        return result.MatchedCount > 0;
    }

    // SOFT DELETE
    public static async Task<bool> DeleteAsync(IMongoDatabase database, string id)
    {
        var collection = GetCollection(database);

        var filter = Builders<AccountingPeriod>.Filter.And(
            Builders<AccountingPeriod>.Filter.Eq(x => x.Id, id),
            Builders<AccountingPeriod>.Filter.Where(x => !x.IsDeleted)
        );

        var update = Builders<AccountingPeriod>.Update
            .Set(x => x.IsDeleted, true)
            .Set(x => x.DeletedAt, DateTime.UtcNow);

        var result = await collection.UpdateOneAsync(filter, update);

        return result.ModifiedCount > 0;
    }
}