using BackendAcctTask.Data.MongoDB;
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using MongoDB.Driver;

namespace BackendAcctTask.Models;
[BsonIgnoreExtraElements]
public class Import : RecordBase
{

    // TB-38-I01
    public string Number { get; set; } = string.Empty;

    public TrialBalanceReference TrialBalance { get; set; } = new();

    public string? Description { get; set; } = string.Empty;

    // Original uploaded file name
    public string? FileName { get; set; } = string.Empty;

    public ImportStatus Status { get; set; } = ImportStatus.Pending;

    public List<ImportColumn> Columns { get; set; } = new();

    public List<string> Headers { get; set; } = new();

    public List<ImportRow> Rows { get; set; } = new();

    public ImportType ImportType { get; set; } = ImportType.Csv;

    public DateTime PeriodStart { get; set; }

    public DateTime PeriodEnd { get; set; }

    public CsvImportType CsvImportType { get; set; }
        = CsvImportType.Default;
        
    // [BsonIgnoreIfDefault]
    // public bool IsDeleted { get; set; } = false;

    // // Agar null hai, toh database mein store nahi hoga
    // [BsonIgnoreIfNull]
    // public DateTime? DeletedAt { get; set; }

    // public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}


public enum ImportStatus
{
    Pending = 0,
    Imported = 1
}

public class ImportColumn
{
    public int Type { get; set; }

    public int Index { get; set; }

    public string Name { get; set; } = string.Empty;
}


public class ImportRow
{
    public string Code { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public AccountNature Nature { get; set; }

    public decimal Debit { get; set; }

    public decimal Credit { get; set; }

    public string Note { get; set; } = string.Empty;
}

public class TrialBalanceReference
{

    public string Id { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;
}


public class ImportRequest
{
     // TB-38-I01
    public string Number { get; set; } = string.Empty;

    public TrialBalanceReference TrialBalance { get; set; } = new();

    public string? Description { get; set; } = string.Empty;

    // Original uploaded file name
    public string? FileName { get; set; } = string.Empty;

    public ImportStatus Status { get; set; } = ImportStatus.Pending;

    public List<ImportColumn> Columns { get; set; } = new();

    public List<string> Headers { get; set; } = new();

    public List<ImportRow> Rows { get; set; } = new();

    public ImportType ImportType { get; set; } = ImportType.Csv;

    public DateTime PeriodStart { get; set; }

    public DateTime PeriodEnd { get; set; }

    public CsvImportType CsvImportType { get; set; }
        = CsvImportType.Default;
}


public class ImportResponseData
{
 // TB-38-I01
    public string Number { get; set; } = string.Empty;

    public TrialBalanceReference TrialBalance { get; set; } = new();

    public string? Description { get; set; } = string.Empty;

    // Original uploaded file name
    public string? FileName { get; set; } = string.Empty;

    public ImportStatus Status { get; set; } = ImportStatus.Pending;

    public List<ImportColumn> Columns { get; set; } = new();

    public List<string> Headers { get; set; } = new();

    public List<ImportRow> Rows { get; set; } = new();

    public ImportType ImportType { get; set; } = ImportType.Csv;

    public DateTime PeriodStart { get; set; }

    public DateTime PeriodEnd { get; set; }

    public CsvImportType CsvImportType { get; set; }
        = CsvImportType.Default;
}


public class ImportDB : RecordDB<Import>
{
// public async Task<List<Import>> GetImports()
// {

  public ImportDB(DBConfiguration dBConfiguration) : base(dBConfiguration)
        {
        }
    // private readonly IWebHostEnvironment _environment;
    // public TrialBalanceDB(DBConfiguration dBConfiguration, IWebHostEnvironment environment) : base(dBConfiguration)
    // {
    //     _environment = environment;
    // }

    public override string CollectionName => "Imports";

        public async Task<Import?> GetImportByIdAsync(string importId)
    {
        try
        {
            LastError = null;
            if (string.IsNullOrWhiteSpace(importId))
                return null;

            return await GetCollection()
                .Find(x => x.Id == importId && !x.IsDeleted)
                .FirstOrDefaultAsync();
        }
        catch (Exception ex)
        {
            LastError = ex;
            return null;
        }
    }

    public async Task<bool> AddImportAsync(Import import)
    {
        try
        {
            LastError = null;
            if (string.IsNullOrWhiteSpace(import.Id))
            {
                import.Id = ObjectId.GenerateNewId().ToString();
            }
            import.CreatedAt = DateTime.UtcNow;
            import.IsDeleted = false;
            await GetCollection().InsertOneAsync(import);
            return true;
        }
        catch (Exception ex)
        {
            LastError = ex;
            return false;
        }
    }
    
    public async Task<List<Import>> GetImportsByTrialBalanceIdAsync(string trialBalanceId)
    {
        try
        {
            LastError = null;
            if (string.IsNullOrWhiteSpace(trialBalanceId))
                return new List<Import>();

            return await GetCollection()
                .Find(x => x.TrialBalance.Id == trialBalanceId && !x.IsDeleted)
                .SortBy(x => x.CreatedAt)
                .ToListAsync();
        }
        catch (Exception ex)
        {
            LastError = ex;
            return new List<Import>();
        }
    }

  
  
  
    //     public async Task<bool> AddImportIdAsync(string trialBalanceId, string importId)
    // {
    //     try
    //     {
    //         var update = Builders<Import>.Update.AddToSet(x => x.ImportIds, importId);
    //         var result = await GetCollection().UpdateOneAsync(x => x.Id == trialBalanceId && !x.IsDeleted, update);
    //         return result.ModifiedCount > 0 || result.MatchedCount > 0;
    //     }
    //     catch (Exception ex)
    //     {
    //         LastError = ex;
    //         return false;
    //     }
    // }

// }

}
// public async Task<Import?> 


