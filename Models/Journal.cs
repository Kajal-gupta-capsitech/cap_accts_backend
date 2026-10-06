using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace BackendAcctTask.Models;

[BsonIgnoreExtraElements]
public class Journal
{
    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    public string Id { get; set; } = string.Empty;

    // TB-21-J01
    public string Number { get; set; } = string.Empty;


    public TrialBalanceReference TrialBalance { get; set; } = new();

    // CSV import reference, if this journal came from an import.
    // Not required for normal/manual journals.
    [BsonRepresentation(BsonType.ObjectId)]
    public string? ImportId { get; set; }

    public TrialBalanceType Type { get; set; }

    public TrialBalanceStatus Status { get; set; }

    public ImportType ImportType { get; set; }

    public DateTime PeriodStart { get; set; }

    public DateTime PeriodEnd { get; set; }

    public CsvImportType CsvImportType { get; set; }

    public List<JournalItem> Items { get; set; } = new();

    public int ItemsCount { get; set; }

    public decimal TotalDebit { get; set; }

    public decimal TotalCredit { get; set; }

    public DateTime CreatedAt { get; set; }

    public string? CreatedBy { get; set; }
}

public class JournalItem
{
    // We use ChartAccount.Code as the reference.
    // We do NOT store ChartAccountId.

    public string AccountCode { get; set; } = string.Empty;

    public string AccountName { get; set; } = string.Empty;

    public AccountNature Nature { get; set; }

    public string Note { get; set; } = string.Empty;

    public decimal Amount { get; set; }

    public decimal Debit { get; set; }

    public decimal Credit { get; set; }
}