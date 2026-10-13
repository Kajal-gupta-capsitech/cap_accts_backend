using BackendAcctTask.Models;
using BackendAcctTask.Settings;
using Microsoft.Extensions.Options;
using MongoDB.Driver;

namespace BackendAcctTask.Services;

public class ChartAccountService
{
    private readonly IMongoCollection<ChartAccount> _chartAccounts;
    private readonly IMongoCollection<AccountType> _accountTypes;

    public ChartAccountService(
     IOptions<MongoDbSettings> mongoDbSettings)
    {
        var mongoClient = new MongoClient(
            mongoDbSettings.Value.ConnectionString);

        var mongoDatabase = mongoClient.GetDatabase(
            mongoDbSettings.Value.DatabaseName);

        _chartAccounts =
            mongoDatabase.GetCollection<ChartAccount>(
                mongoDbSettings.Value.ChartAccountsCollectionName);

        _accountTypes =
            mongoDatabase.GetCollection<AccountType>(
                mongoDbSettings.Value.AccountTypesCollectionName);
    }

    public async Task<bool> AccountNameExistsAsync(string accountName)
    {
        return await _chartAccounts
            .Find(x => x.AccountName == accountName && !x.IsDeleted)
            .Limit(1)
            .FirstOrDefaultAsync() is not null;
    }

    // GET ALL (Excluding soft-deleted records)
    public async Task<List<ChartAccountResponse>> GetAsync()
    {
        var chartAccounts = await _chartAccounts
            .Find(x => !x.IsDeleted)
            .ToListAsync();

        if (chartAccounts.Count == 0)
        {
            return new List<ChartAccountResponse>();
        }

        // Fetch all referenced AccountTypes that are not soft-deleted
        var accountTypeIds = chartAccounts
            .Select(x => x.AccountTypeId)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct()
            .ToList();

        var accountTypes = accountTypeIds.Count == 0
            ? new List<AccountType>()
            : await _accountTypes
                .Find(Builders<AccountType>.Filter.And(
                    Builders<AccountType>.Filter.In(x => x.Id, accountTypeIds),
                    Builders<AccountType>.Filter.Where(x => !x.IsDeleted)
                ))
                .ToListAsync();

        var accountTypesById = accountTypes
            .ToDictionary(x => x.Id, StringComparer.Ordinal);

        return chartAccounts
            .Select(chartAccount => new ChartAccountResponse
            {
                Id = chartAccount.Id,
                Code = chartAccount.Code,
                AccountName = chartAccount.AccountName,
                AccountTypeId = chartAccount.AccountTypeId,
                AccountType = accountTypesById.TryGetValue(
                    chartAccount.AccountTypeId,
                    out var accountType)
                        ? accountType
                        : null,
                AccountGroup = chartAccount.AccountGroup,
                ForClients = chartAccount.ForClients,
                Archive = chartAccount.Archive
            })
            .ToList();
    }

    // GET BY ID (Excluding soft-deleted records)
    public async Task<ChartAccountResponse?> GetAsync(string id)
    {
        var chartAccount = await _chartAccounts
            .Find(x => x.Id == id && !x.IsDeleted)
            .FirstOrDefaultAsync();

        if (chartAccount == null)
        {
            return null;
        }

        var accountType = await _accountTypes
            .Find(x => x.Id == chartAccount.AccountTypeId && !x.IsDeleted)
            .FirstOrDefaultAsync();

        return new ChartAccountResponse
        {
            Id = chartAccount.Id,
            Code = chartAccount.Code,
            AccountName = chartAccount.AccountName,

            AccountTypeId = chartAccount.AccountTypeId,
            AccountType = accountType,

            AccountGroup = chartAccount.AccountGroup,
            ForClients = chartAccount.ForClients,
            Archive = chartAccount.Archive
        };
    }

    // GET NEXT CODE (Excluding soft-deleted accounts from sequence check)
    public async Task<string> GetNextCodeAsync()
    {
        var lastChartAccount = await _chartAccounts
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
    public async Task CreateAsync(ChartAccount chartAccount)
    {
        chartAccount.Code = await GetNextCodeAsync();

        await _chartAccounts.InsertOneAsync(chartAccount);
    }

    // PARTIAL UPDATE (Ensuring we don't update deleted records)
    public async Task<bool> UpdateAsync(
        string id,
        UpdateChartAccountRequest request)
    {
        var updates =
            new List<UpdateDefinition<ChartAccount>>();

        // Code is intentionally NOT updated.
        // Code is generated automatically.

        if (request.AccountName != null)
        {
            updates.Add(
                Builders<ChartAccount>.Update.Set(
                    x => x.AccountName,
                    request.AccountName));
        }

        if (request.AccountTypeId != null)
        {
            updates.Add(
                Builders<ChartAccount>.Update.Set(
                    x => x.AccountTypeId,
                    request.AccountTypeId));
        }

        if (request.AccountGroup != null)
        {
            updates.Add(
                Builders<ChartAccount>.Update.Set(
                    x => x.AccountGroup,
                    request.AccountGroup));
        }

        if (request.ForClients.HasValue)
        {
            updates.Add(
                Builders<ChartAccount>.Update.Set(
                    x => x.ForClients,
                    request.ForClients.Value));
        }

        if (request.Archive.HasValue)
        {
            updates.Add(
                Builders<ChartAccount>.Update.Set(
                    x => x.Archive,
                    request.Archive.Value));
        }

        if (updates.Count == 0)
        {
            return false;
        }

        var combinedUpdate =
            Builders<ChartAccount>.Update.Combine(updates);

        var result = await _chartAccounts.UpdateOneAsync(
            x => x.Id == id && !x.IsDeleted,
            combinedUpdate);

        return result.MatchedCount > 0;
    }

    // SOFT DELETE
    public async Task<bool> DeleteAsync(string id)
    {
        var filter = Builders<ChartAccount>.Filter.And(
            Builders<ChartAccount>.Filter.Eq(x => x.Id, id),
            Builders<ChartAccount>.Filter.Where(x => !x.IsDeleted)
        );

        var update = Builders<ChartAccount>.Update
            .Set(x => x.IsDeleted, true)
            .Set(x => x.DeletedAt, DateTime.UtcNow);

        var result = await _chartAccounts.UpdateOneAsync(filter, update);

        return result.ModifiedCount > 0;
    }
}