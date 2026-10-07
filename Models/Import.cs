using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace BackendAcctTask.Models;
[BsonIgnoreExtraElements]
public class Import
{
    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    public string Id { get; set; } = string.Empty;

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

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
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

