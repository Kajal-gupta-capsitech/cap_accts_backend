using BackendAcctTask.Models;
using BackendAcctTask.Settings;
using Microsoft.Extensions.Options;
using MongoDB.Driver;

namespace BackendAcctTask.Services;

public class AccountingPeriodService
{
    private readonly IMongoCollection<AccountingPeriod> _accountingPeriods;

    public AccountingPeriodService(
        IOptions<MongoDbSettings> mongoDbSettings)
    {
        var mongoClient = new MongoClient(
            mongoDbSettings.Value.ConnectionString);

        var mongoDatabase = mongoClient.GetDatabase(
            mongoDbSettings.Value.DatabaseName);

        _accountingPeriods = mongoDatabase.GetCollection<AccountingPeriod>(
            "AccountingPeriods");
    }

    // GET ALL (Excluding soft-deleted records)
    public async Task<List<AccountingPeriod>> GetAsync()
    {
        return await _accountingPeriods
            .Find(x => !x.IsDeleted)
            .ToListAsync();
    }

    // GET BY ID (Excluding soft-deleted records)
    public async Task<AccountingPeriod?> GetAsync(string id)
    {
        return await _accountingPeriods
            .Find(x => x.Id == id && !x.IsDeleted)
            .FirstOrDefaultAsync();
    }

    // CREATE
    public async Task CreateAsync(AccountingPeriod accountingPeriod)
    {
        await _accountingPeriods.InsertOneAsync(accountingPeriod);
    }

    // PARTIAL UPDATE
    public async Task<bool> UpdateAsync(
      string id,
      UpdateAccountingPeriodRequest request)
    {
        var updates = new List<UpdateDefinition<AccountingPeriod>>();

        if (request.PeriodFrom.HasValue)
        {
            updates.Add(
                Builders<AccountingPeriod>.Update.Set(
                    x => x.PeriodFrom,
                    request.PeriodFrom.Value));
        }

        if (request.PeriodTo.HasValue)
        {
            updates.Add(
                Builders<AccountingPeriod>.Update.Set(
                    x => x.PeriodTo,
                    request.PeriodTo.Value));
        }

        if (request.IsActive.HasValue)
        {
            updates.Add(
                Builders<AccountingPeriod>.Update.Set(
                    x => x.IsActive,
                    request.IsActive.Value));
        }

        if (request.IsClosed.HasValue)
        {
            updates.Add(
                Builders<AccountingPeriod>.Update.Set(
                    x => x.IsClosed,
                    request.IsClosed.Value));
        }

        if (updates.Count == 0)
        {
            return false;
        }

        var combinedUpdate =
            Builders<AccountingPeriod>.Update.Combine(updates);

        var result = await _accountingPeriods.UpdateOneAsync(
            x => x.Id == id && !x.IsDeleted,
            combinedUpdate);

        return result.MatchedCount > 0;
    }


    // SOFT DELETE
    public async Task<bool> DeleteAsync(string id)
    {
        var filter = Builders<AccountingPeriod>.Filter.And(
            Builders<AccountingPeriod>.Filter.Eq(x => x.Id, id),
            Builders<AccountingPeriod>.Filter.Where(x => !x.IsDeleted)
        );

        var update = Builders<AccountingPeriod>.Update
            .Set(x => x.IsDeleted, true)
            .Set(x => x.DeletedAt, DateTime.UtcNow);

        var result = await _accountingPeriods.UpdateOneAsync(filter, update);

        return result.ModifiedCount > 0;
    }
}