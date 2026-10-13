using Microsoft.Extensions.FileSystemGlobbing;
using Microsoft.VisualBasic;
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using MongoDB.Driver;

namespace BackendAcctTask.Models;

[BsonIgnoreExtraElements]
public class AccountType
{
    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    public string Id { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public AccountNature Nature { get; set; }

    [BsonIgnoreIfDefault]
    public bool IsDeleted { get; set; } = false;

    // Agar null hai, toh database mein store nahi hoga
    [BsonIgnoreIfNull]
    public DateTime? DeletedAt { get; set; }
}


public static class AccountTypeDB
{
    private static IMongoCollection<AccountType> GetCollection(IMongoDatabase database)
    {

        return database.GetCollection<AccountType>("AccountTypes");
    }


    public static async Task<List<AccountType>> GetAllAsync(IMongoDatabase database)
    {
        return await GetCollection(database).Find(x => !x.IsDeleted).ToListAsync();
    }

    public static async Task<AccountType?> GetByIdAsync(IMongoDatabase database, string id){
        return await GetCollection(database)
        .Find(x => x.Id == id  && !x.IsDeleted)
        .FirstOrDefaultAsync();
    }

    public static async Task CreateAsync(IMongoDatabase database , AccountType accountType){

        await GetCollection(database).InsertOneAsync(accountType);

        
    }

    public static async Task<bool> UpdateAsync(IMongoDatabase database, string id, AccountType accountType) 
    {
         var collection = GetCollection(database);
        var filter = Builders<AccountType>.Filter.And(
                    Builders<AccountType>.Filter.Eq(x => x.Id, id),
                    Builders<AccountType>.Filter.Where(x => !x.IsDeleted)
        );

        var result = await collection.ReplaceOneAsync(filter, accountType);
        
        return result.ModifiedCount > 0;
    }


    public static async Task<bool> DeleteAsync(IMongoDatabase database , string id)
    {
        var collection = GetCollection(database);

        var filter = Builders<AccountType>.Filter.And(
            Builders<AccountType>.Filter.Eq(x => x.Id, id),
            Builders<AccountType>.Filter.Where(x => !x.IsDeleted)
        );

        var update = Builders<AccountType>.Update
        .Set( x => x.IsDeleted, true)
        .Set(x => x.DeletedAt, DateTime.UtcNow);

        var result = await collection.UpdateOneAsync(filter,update);
        return result.ModifiedCount > 0;
    }


    }
