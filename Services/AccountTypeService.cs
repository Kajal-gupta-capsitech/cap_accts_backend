using BackendAcctTask.Models;
using BackendAcctTask.Settings;
using Microsoft.Extensions.Options;
using MongoDB.Driver;

namespace BackendAcctTask.Services;

public class AccountTypeService
{
    private readonly IMongoCollection<AccountType> _accountTypes;

    public AccountTypeService(
        IOptions<MongoDbSettings> mongoDbSettings)
    {
        var mongoClient = new MongoClient(
            mongoDbSettings.Value.ConnectionString);

        var mongoDatabase = mongoClient.GetDatabase(
            mongoDbSettings.Value.DatabaseName);

        _accountTypes = mongoDatabase.GetCollection<AccountType>(
            mongoDbSettings.Value.AccountTypesCollectionName);
    }

    public async Task<List<AccountType>> GetAsync()
    {
        return await _accountTypes
            .Find(x => !x.IsDeleted)
            .ToListAsync();
    }

    public async Task<AccountType?> GetAsync(string id)
    {
        return await _accountTypes
            .Find(x => x.Id == id && !x.IsDeleted)
            .FirstOrDefaultAsync();
    }

    public async Task CreateAsync(AccountType accountType)
    {
        await _accountTypes.InsertOneAsync(accountType);
    }

  public async Task<bool> UpdateAsync(
    string id,
    AccountType accountType)
{
    // Ensure we only update if the record exists and is not already soft-deleted
    var filter = Builders<AccountType>.Filter.And(
        Builders<AccountType>.Filter.Eq(x => x.Id, id),
        Builders<AccountType>.Filter.Where(x => !x.IsDeleted)
    );

    var result = await _accountTypes.ReplaceOneAsync(filter, accountType);

    return result.MatchedCount > 0;
}

    public async Task<bool> DeleteAsync(string id)
    {

        var filter = Builders<AccountType>.Filter.And(
           Builders<AccountType>.Filter.Eq(x => x.Id, id),
           Builders<AccountType>.Filter.Where(x => !x.IsDeleted)
       );
        var update = Builders<AccountType>.Update
    .Set(x => x.IsDeleted, true)
    .Set(x => x.DeletedAt, DateTime.UtcNow);

        var result = await _accountTypes.UpdateOneAsync(filter, update);

        return result.ModifiedCount > 0;    
    }
}