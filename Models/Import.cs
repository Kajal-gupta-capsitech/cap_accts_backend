using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace BackendAcctTask.Models;
public class Import
{
    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    public string Id { get; set; } = string.Empty;

    // Example: IMP-01, IMP-02
    public string Number { get; set; } = string.Empty;

    // Reference to the Trial Balance
    // Example: TB-38
    public TrialBalanceReference TrialBalance { get; set; } = new();

    // CSV column configuration
    public List<ImportColumn> Columns { get; set; } = new();

    // Original CSV headers
    public List<string> Headers { get; set; } = new();

    // Imported CSV rows
    public List<ImportRow> Rows { get; set; } = new();

    public ImportType ImportType { get; set; } = ImportType.Csv;

    public DateTime PeriodStart { get; set; }

    public DateTime PeriodEnd { get; set; }

    public CsvImportType CsvImportType { get; set; } = CsvImportType.Default;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
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