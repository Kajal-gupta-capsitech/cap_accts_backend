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
    public ImportsReference Imports { get; set; } = new();

    // [BsonRepresentation(BsonType.ObjectId)]

    // Normal / Adjusting
    public JournalType JournalType { get; set; }
    public TrialBalanceType Type { get; set; }

    // Drafted / Posted / Unposted
    public JournalStatus JournalStatus { get; set; }

    // Default / Manual
    public ImportType ImportType { get; set; }

    public DateTime PeriodStart { get; set; }

    public DateTime PeriodEnd { get; set; }

    public CsvImportType CsvImportType { get; set; }

    public string? Description { get; set; } = string.Empty;

    public bool IsActive { get; set; } = true;

    public List<JournalItem> Items { get; set; } = new();

    public int ItemsCount { get; set; }

    public decimal TotalDebit { get; set; }

    public decimal TotalCredit { get; set; }

    public TrialBalanceStatus Status { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public string? CreatedBy { get; set; }

    public Attachment? Attachment { get; set; } 
}


public class Attachment
{

    public string Name { get; set; } = string.Empty;

    public string Path { get; set; } = string.Empty;
}



public enum JournalType
{
    Normal = 0,
    Adjusting = 1
}


public enum JournalStatus
{
    Drafted = 0,
    Posted = 1,
    Unposted = 2
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

public class ImportsReference
{

    public string Id { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;
}

