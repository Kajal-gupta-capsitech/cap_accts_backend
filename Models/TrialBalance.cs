using BackendAcctTask.Data.MongoDB;
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using System.ComponentModel.DataAnnotations;
using MongoDB.Driver;
namespace BackendAcctTask.Models;

[BsonIgnoreExtraElements]
public class TrialBalance : RecordBase
{
    // TB-01, TB-02, TB-03...
    public string RefNo { get; set; } = string.Empty;

    [BsonRepresentation(BsonType.ObjectId)]
    public string? PeriodId { get; set; }

    public DateTime PeriodStart { get; set; }

    public DateTime PeriodEnd { get; set; }

    // Statutory = 0
    // Management = 1
    // [BsonElement("TrialBalanceType")]
    public TrialBalanceType Type { get; set; }

    public string? Description { get; set; } = string.Empty;

    // Manual = 0
    // Csv = 1
    public ImportType ImportType { get; set; }

    // Currently only Default = 0
    public CsvImportType CsvImportType { get; set; }

    // Unbalanced = 0
    // Balanced = 1
    public TrialBalanceStatus Status { get; set; }

    public bool IsLocked { get; set; }

    public decimal Turnover { get; set; }

    public decimal TotalDebit { get; set; }

    public decimal TotalCredit { get; set; }

    public decimal TotalProfitLoss { get; set; }

    // No separate AccountReport entity currently.
    public object? AccountReports { get; set; }

    // Local CSV path.
    public string? CsvFilePath { get; set; }

    // Local attachment path.
    public string? AttachmentFilePath { get; set; }

    // public string? JournalId { get; set; }

    [BsonRepresentation(BsonType.ObjectId)]
    public List<string> JournalIds { get; set; } = new();

    [BsonRepresentation(BsonType.ObjectId)]
    public List<string> ImportIds { get; set; } = new();

}

// ============================================================
// ENUMS
// ============================================================


// public class TrialBalanceRequest
// {
//     public string? PeriodId { get; set; }

//     public DateTime PeriodStart { get; set; }

//     public DateTime PeriodEnd { get; set; }

//     public TrialBalanceType Type { get; set; }

//     public string? Description { get; set; }

//     public ImportType ImportType { get; set; }

//     public CsvImportType CsvImportType { get; set; }
// }


public class TrialBalanceRequest
{
    // Accounting Period
    public string? PeriodId { get; set; }

    public string? AccountingPeriodId { get; set; }

    public DateTime? PeriodStart { get; set; }

    public DateTime? PeriodEnd { get; set; }

    // Trial Balance Type
    public TrialBalanceType? Type { get; set; }

    public TrialBalanceType? TrialBalanceType { get; set; }

    // Import Configuration
    public ImportType? ImportType { get; set; }

    public ImportType? ImportMode { get; set; }

    public CsvImportType? CsvImportType { get; set; }

    public string? ImportFormat { get; set; }

    // Journal
    public string? JournalId { get; set; }

    // Basic Information
    public string? Description { get; set; }

    public decimal Turnover { get; set; }

    // CSV Upload
    public IFormFile? CsvFile { get; set; }

    // Alternative CSV field name (backward compatibility)
    public IFormFile? File { get; set; }

    // Attachment Upload
    public IFormFile? Attachment { get; set; }

    // Source Trial Balance for copy operations
    public string? SourceTrialBalanceId { get; set; }
}


public class TrialBalanceResponseData
{
    public string Id { get; set; } = string.Empty;

    public string RefNo { get; set; } = string.Empty;

    public string? PeriodId { get; set; }

    public DateTime PeriodStart { get; set; }

    public DateTime PeriodEnd { get; set; }

    public TrialBalanceType Type { get; set; }

    public string? Description { get; set; }

    public ImportType ImportType { get; set; }

    public CsvImportType CsvImportType { get; set; }

    public List<string> ImportIds { get; set; } = new();

    public List<string> JournalIds { get; set; } = new();

    public decimal TotalDebit { get; set; }

    public decimal TotalCredit { get; set; }

    public TrialBalanceStatus Status { get; set; }
}


public enum TrialBalanceType
{
    Statutory = 0,
    Management = 1
}

public enum ImportType
{
    Csv = 0,
    Manual = 1
}

public enum CsvImportType
{
    Default = 0
}

public enum TrialBalanceStatus
{
    Unbalanced = 0,
    Balanced = 1
}

public enum AccountNature
{
    Debit = 1,
    Credit = 0
}


public class ImportJournalRequest
{
    public string? Description { get; set; }

    public DateTime? PeriodStart { get; set; }

    public DateTime? PeriodEnd { get; set; }

    public List<string> Columns { get; set; } = new();

    public List<List<string>> Rows { get; set; } = new();

    public int CsvImportType { get; set; }

    public int JournalType { get; set; }
}

public class TrialBalanceDB : RecordDB<TrialBalance>
{
    private readonly IWebHostEnvironment _environment;
    public TrialBalanceDB(DBConfiguration dBConfiguration, IWebHostEnvironment environment) : base(dBConfiguration)
    {
        _environment = environment;
    }

    public override string CollectionName => "TrialBalances";

    public IMongoCollection<Import> GetImportCollection()
    {
        return _database.GetCollection<Import>("Imports");
    }

    public IMongoCollection<Journal> GetJournalCollection()
    {
        return _database.GetCollection<Journal>("Journals");
    }





    public async Task<bool> AddImportIdAsync(string trialBalanceId, string importId)
    {
        try
        {
            var update = Builders<TrialBalance>.Update.AddToSet(x => x.ImportIds, importId);
            var result = await GetCollection().UpdateOneAsync(x => x.Id == trialBalanceId && !x.IsDeleted, update);
            return result.ModifiedCount > 0 || result.MatchedCount > 0;
        }
        catch (Exception ex)
        {
            LastError = ex;
            return false;
        }
    }

    // Helper function to generate entity numbers for TrialBalance, Import, Journal, etc.


    public async Task<List<TrialBalance>> GetLatestTB()
    {
        try
        {
            LastError = null;
            var trialBalances = await GetCollection()
                .Find(x => !x.IsDeleted)
                .SortByDescending(x => x.RefNo)
                .ToListAsync();

            var collection = GetCollection();

            var totalCount = await collection.CountDocumentsAsync(
                Builders<TrialBalance>.Filter.Empty
            );

            var activeCount = await collection.CountDocumentsAsync(
                Builders<TrialBalance>.Filter.Where(x => !x.IsDeleted)
            );

            Console.WriteLine($"Collection: {CollectionName}");
            Console.WriteLine($"Total documents: {totalCount}");
            Console.WriteLine($"Non-deleted documents: {activeCount}");

            return trialBalances;
        }
        catch (Exception ex)
        {
            Console.WriteLine("exception", ex.ToString(), ex.Message);
            throw new InvalidOperationException(
            $"Failed to retrieve trial balances from '{CollectionName}'.",
            ex
        );
        }
    }

    public async Task<string> GenerateEntityNumberAsync<TEntity>(
        IMongoCollection<TEntity> collection,
        string? trialBalanceId,
        string? trialBalanceNumber,
        string separator,
        Func<TEntity, string?> numberSelector) where TEntity : class
    {
        try
        {
            var filterBuilder = Builders<TEntity>.Filter;
            var filter = !string.IsNullOrWhiteSpace(trialBalanceId)
                ? filterBuilder.And(
                    filterBuilder.Eq("TrialBalance.Id", trialBalanceId),
                    filterBuilder.Ne("IsDeleted", true))
                : filterBuilder.Ne("IsDeleted", true);

            var latest = await collection
                .Find(filter)
                .Sort(Builders<TEntity>.Sort.Descending("CreatedAt"))
                .FirstOrDefaultAsync();

            int number = 1;

            if (latest != null)
            {
                var numberStr = numberSelector(latest);
                if (!string.IsNullOrWhiteSpace(numberStr))
                {
                    string lastPart;
                    if (numberStr.Contains(separator))
                    {
                        lastPart = numberStr.Split(separator).LastOrDefault() ?? string.Empty;
                    }
                    else
                    {
                        lastPart = numberStr.Split('-').LastOrDefault() ?? string.Empty;
                    }

                    var digits = new string(lastPart.Where(char.IsDigit).ToArray());
                    if (int.TryParse(digits, out var parsed))
                    {
                        number = parsed + 1;
                    }
                }
            }

            if (string.IsNullOrWhiteSpace(trialBalanceNumber))
            {
                var prefix = separator.TrimStart('-');
                return $"{prefix}-{number:00}";
            }

            return $"{trialBalanceNumber}{separator}{number:00}";
        }
        catch (Exception ex)
        {
            LastError = ex;
            if (string.IsNullOrWhiteSpace(trialBalanceNumber))
            {
                var prefix = separator.TrimStart('-');
                return $"{prefix}-01";
            }
            return $"{trialBalanceNumber}{separator}01";
        }
    }
public async Task<bool> UpdateJournalSummaryAsync(
    string trialBalanceId,
    string journalId,
    decimal totalDebit,
    decimal totalCredit)
{
    try
    {
        LastError = null;

        var update = Builders<TrialBalance>.Update.Combine(
            Builders<TrialBalance>.Update.AddToSet(
                x => x.JournalIds,
                journalId),

            Builders<TrialBalance>.Update.Set(
                x => x.TotalDebit,
                totalDebit),

            Builders<TrialBalance>.Update.Set(
                x => x.TotalCredit,
                totalCredit),

            Builders<TrialBalance>.Update.Set(
                x => x.Status,
                totalDebit == totalCredit
                    ? TrialBalanceStatus.Balanced
                    : TrialBalanceStatus.Unbalanced),

            Builders<TrialBalance>.Update.Set(
                x => x.UpdatedAt,
                DateTime.UtcNow)
        );

        var result = await GetCollection().UpdateOneAsync(
            x => x.Id == trialBalanceId && !x.IsDeleted,
            update);

        return result.MatchedCount > 0;
    }
    catch (Exception ex)
    {
        LastError = ex;
        return false;
    }
}

    public async Task<string?> SaveFile(
        IFormFile? file,
        string folder)
    {
        if (file == null)
            return null;

        var rootPath = Path.Combine(
            _environment.ContentRootPath,
            "Uploads",
            "TrialBalances",
            folder
        );

        Directory.CreateDirectory(rootPath);

        var extension = Path.GetExtension(file.FileName);
        var fileName = $"{Guid.NewGuid():N}{extension}";
        var fullPath = Path.Combine(rootPath, fileName);

        await using var stream = new FileStream(fullPath, FileMode.Create);
        await file.CopyToAsync(stream);

        // Store relative path in MongoDB
        return Path.Combine(
            "Uploads",
            "TrialBalances",
            folder,
            fileName
        ).Replace("\\", "/");
    }

    public async Task<TrialBalance?> GetByRefNoOrIdAsync(string refNoOrId)
    {
        try
        {
            LastError = null;
            if (string.IsNullOrWhiteSpace(refNoOrId))
                return null;

            var isObjectId = ObjectId.TryParse(refNoOrId, out _);
            var filterBuilder = Builders<TrialBalance>.Filter;
            var notDeletedFilter = filterBuilder.Where(x => !x.IsDeleted);

            FilterDefinition<TrialBalance> queryFilter;
            if (isObjectId)
            {
                queryFilter = filterBuilder.And(
                    filterBuilder.Or(
                        filterBuilder.Eq(x => x.Id, refNoOrId),
                        filterBuilder.Eq(x => x.RefNo, refNoOrId)
                    ),
                    notDeletedFilter
                );
            }
            else
            {
                queryFilter = filterBuilder.And(
                    filterBuilder.Eq(x => x.RefNo, refNoOrId),
                    notDeletedFilter
                );
            }

            return await GetCollection().Find(queryFilter).FirstOrDefaultAsync();
        }
        catch (Exception ex)
        {
            LastError = ex;
            return null;
        }
    }




    public async Task<List<Journal>> GetJournalsByIdsAsync(IEnumerable<string>? journalIds)
    {
        try
        {
            LastError = null;
            var ids = journalIds?.Where(x => !string.IsNullOrWhiteSpace(x)).ToList() ?? new List<string>();
            if (ids.Count == 0)
                return new List<Journal>();

            return await GetJournalCollection()
                .Find(x => ids.Contains(x.Id) && !x.IsDeleted)
                .SortBy(x => x.CreatedAt)
                .ToListAsync();
        }
        catch (Exception ex)
        {
            LastError = ex;
            return new List<Journal>();
        }
    }

    public async Task<List<Journal>> GetJournalsByTrialBalanceIdAsync(string trialBalanceId, bool onlyPosted = false)
    {
        try
        {
            LastError = null;
            if (string.IsNullOrWhiteSpace(trialBalanceId))
                return new List<Journal>();

            var filterBuilder = Builders<Journal>.Filter;
            var filter = filterBuilder.And(
                filterBuilder.Eq(x => x.TrialBalance.Id, trialBalanceId),
                filterBuilder.Where(x => !x.IsDeleted)
            );

            if (onlyPosted)
            {
                filter = filterBuilder.And(filter, filterBuilder.Eq(x => x.JournalStatus, JournalStatus.Posted));
            }

            return await GetJournalCollection()
                .Find(filter)
                .SortBy(x => x.CreatedAt)
                .ToListAsync();
        }
        catch (Exception ex)
        {
            LastError = ex;
            return new List<Journal>();
        }
    }



    public async Task<Journal?> GetJournalByIdAsync(string journalId, string? trialBalanceId = null)
    {
        try
        {
            LastError = null;
            if (string.IsNullOrWhiteSpace(journalId))
                return null;

            var filterBuilder = Builders<Journal>.Filter;
            var filter = filterBuilder.And(
                filterBuilder.Eq(x => x.Id, journalId),
                filterBuilder.Where(x => !x.IsDeleted)
            );

            if (!string.IsNullOrWhiteSpace(trialBalanceId))
            {
                filter = filterBuilder.And(filter, filterBuilder.Eq(x => x.TrialBalance.Id, trialBalanceId));
            }

            return await GetJournalCollection().Find(filter).FirstOrDefaultAsync();
        }
        catch (Exception ex)
        {
            LastError = ex;
            return null;
        }
    }

    public async Task<bool> RecalculateTotalsAsync(string trialBalanceId)
    {
        try
        {
            LastError = null;
            var tb = await GetByIdAsync(trialBalanceId);
            if (tb == null)
                return false;

            var journals = await GetJournalsByTrialBalanceIdAsync(trialBalanceId);
            tb.TotalDebit = journals.Sum(x => x.TotalDebit);
            tb.TotalCredit = journals.Sum(x => x.TotalCredit);
            tb.Status = tb.TotalDebit == tb.TotalCredit
                ? TrialBalanceStatus.Balanced
                : TrialBalanceStatus.Unbalanced;

            var update = Builders<TrialBalance>.Update
                .Set(x => x.TotalDebit, tb.TotalDebit)
                .Set(x => x.TotalCredit, tb.TotalCredit)
                .Set(x => x.Status, tb.Status);

            var result = await GetCollection().UpdateOneAsync(x => x.Id == trialBalanceId, update);
            return result.ModifiedCount > 0 || result.MatchedCount > 0;
        }
        catch (Exception ex)
        {
            LastError = ex;
            return false;
        }
    }
}




