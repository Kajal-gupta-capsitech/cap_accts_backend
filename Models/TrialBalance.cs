using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace BackendAcctTask.Models;

[BsonIgnoreExtraElements]
public class TrialBalance
{
    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    public string Id { get; set; } = string.Empty;

    // TB-01, TB-02, TB-03...
    public string RefNo { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [BsonRepresentation(BsonType.ObjectId)]
    public string? PeriodId { get; set; }

    public DateTime PeriodStart { get; set; }

    public DateTime PeriodEnd { get; set; }

    // Statutory = 0
    // Management = 1
    [BsonElement("TrialBalanceType")]
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
    
    public string? JournalId { get; set; }
    
    [BsonRepresentation(BsonType.ObjectId)]
        public List<string> JournalIds { get; set; } = new();

     [BsonRepresentation(BsonType.ObjectId)]
        public List<string> ImportIds { get; set; } = new();

}




// ============================================================
// ENUMS
// ============================================================

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