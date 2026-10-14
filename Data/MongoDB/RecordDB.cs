using MongoDB.Bson;
using MongoDB.Driver;
using System.Linq.Expressions;

namespace BackendAcctTask.Data.MongoDB
{
    public abstract class RecordDB<T> where T : RecordBase
    {
        protected readonly DBConfiguration _dbConfig;
        protected readonly IMongoDatabase _database;

        public abstract string CollectionName { get; }

        public Exception? LastError { get; set; }

        public RecordDB(DBConfiguration dBConfiguration)
        {
            _dbConfig = dBConfiguration;
            _database = dBConfiguration.Database;
        }

        public IMongoCollection<T> GetCollection()
        {
            return _database.GetCollection<T>(CollectionName);
        }

        public virtual async Task<List<T>> GetAllAsync(Expression<Func<T, bool>>? filter = null, bool includeDeleted = false)
        {
            try
            {
                LastError = null;
                var collection = GetCollection();
                var builder = Builders<T>.Filter;
                var finalFilter = includeDeleted ? builder.Empty : builder.Where(x => !x.IsDeleted);

                if (filter != null)
                {
                    finalFilter = builder.And(finalFilter, builder.Where(filter));
                }

                return await collection.Find(finalFilter).ToListAsync();
            }
            catch (Exception ex)
            {
                LastError = ex;
                return new List<T>();
            }
        }

        public virtual async Task<T?> GetByIdAsync(string id)
        {
            try
            {
                LastError = null;
                if (string.IsNullOrWhiteSpace(id))
                    return null;

                return await GetCollection()
                    .Find(x => x.Id == id && !x.IsDeleted)
                    .FirstOrDefaultAsync();
            }
            catch (Exception ex)
            {
                LastError = ex;
                return null;
            }
        }

        public virtual async Task<bool> AddAsync(T record)
        {
            try
            {
                LastError = null;
                if (string.IsNullOrWhiteSpace(record.Id))
                {
                    record.Id = ObjectId.GenerateNewId().ToString();
                }
                record.CreatedAt = DateTime.UtcNow;
                record.IsDeleted = false;

                await GetCollection().InsertOneAsync(record);
                return true;
            }
            catch (Exception ex)
            {
                LastError = ex;
                return false;
            }
        }

        public virtual async Task<bool> UpdateAsync(string id, T record)
        {
            try
            {
                LastError = null;
                record.Id = id;
                record.UpdatedAt = DateTime.UtcNow;

                var filter = Builders<T>.Filter.And(
                    Builders<T>.Filter.Eq(x => x.Id, id),
                    Builders<T>.Filter.Where(x => !x.IsDeleted)
                );

                var result = await GetCollection().ReplaceOneAsync(filter, record);
                return result.MatchedCount > 0;
            }
            catch (Exception ex)
            {
                LastError = ex;
                return false;
            }
        }

        public virtual async Task<bool> DeleteAsync(string id, bool softDelete = true)
        {
            try
            {
                LastError = null;
                var filter = Builders<T>.Filter.And(
                    Builders<T>.Filter.Eq(x => x.Id, id),
                    Builders<T>.Filter.Where(x => !x.IsDeleted)
                );

                if (softDelete)
                {
                    var update = Builders<T>.Update
                        .Set(x => x.IsDeleted, true)
                        .Set(x => x.DeletedAt, DateTime.UtcNow);

                    var result = await GetCollection().UpdateOneAsync(filter, update);
                    return result.ModifiedCount > 0;
                }
                else
                {
                    var result = await GetCollection().DeleteOneAsync(filter);
                    return result.DeletedCount > 0;
                }
            }
            catch (Exception ex)
            {
                LastError = ex;
                return false;
            }
        }

        public virtual async Task<bool> ExistsAsync(Expression<Func<T, bool>> filter)
        {
            try
            {
                LastError = null;
                var builder = Builders<T>.Filter;
                var finalFilter = builder.And(builder.Where(x => !x.IsDeleted), builder.Where(filter));
                var count = await GetCollection().Find(finalFilter).Limit(1).CountDocumentsAsync();
                return count > 0;
            }
            catch (Exception ex)
            {
                LastError = ex;
                return false;
            }
        }

        public virtual async Task<long> CountAsync(Expression<Func<T, bool>>? filter = null)
        {
            try
            {
                LastError = null;
                var builder = Builders<T>.Filter;
                var finalFilter = builder.Where(x => !x.IsDeleted);
                if (filter != null)
                {
                    finalFilter = builder.And(finalFilter, builder.Where(filter));
                }

                return await GetCollection().CountDocumentsAsync(finalFilter);
            }
            catch (Exception ex)
            {
                LastError = ex;
                return 0;
            }
        }
public virtual async Task<bool> UpdateFieldsAsync(string id, UpdateDefinition<T> updateDefinition)
{
    try
    {
        LastError = null;
        var filter = Builders<T>.Filter.And(
            Builders<T>.Filter.Eq(x => x.Id, id),
            Builders<T>.Filter.Where(x => !x.IsDeleted)
        );

        // Automatically UpdatedAt set karne ke liye agar definition mein nahi hai
        // (Optional: aap ise controller ya yahan handle kar sakte hain)
        
        var result = await GetCollection().UpdateOneAsync(filter, updateDefinition);
        return result.ModifiedCount > 0 || result.MatchedCount > 0;
    }
    catch (Exception ex)
    {
        LastError = ex;
        return false;
    }
}


    }
}


