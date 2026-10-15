using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using BackendAcctTask.Models;
using MongoDB.Driver;
using Microsoft.AspNetCore.Mvc;
namespace BackendAcctTask.Data.MongoDB;
// namespace BackendAcctTask.Models;

[BsonIgnoreExtraElements]
public class Journal : RecordBase
{
    // Example: TB-21-J01
    public string Number { get; set; } = string.Empty;

    // References to separate collections.
    // These are embedded reference objects, not nested documents.
    // containing the complete TrialBalance or Import records.
    public TrialBalanceReference TrialBalance { get; set; } = new();

    public ImportsReference Imports { get; set; } = new();

    // Journal configuration
    public JournalType JournalType { get; set; }

    public TrialBalanceType Type { get; set; }

    public JournalStatus JournalStatus { get; set; }

    public ImportType ImportType { get; set; }

    public CsvImportType CsvImportType { get; set; }

    // Accounting period
    public DateTime PeriodStart { get; set; }

    public DateTime PeriodEnd { get; set; }

    // Journal information
    public string? Description { get; set; } = string.Empty;

    public bool IsActive { get; set; } = true;

    // Transaction entries
    public List<JournalItem> Items { get; set; } = new();

    public int ItemsCount { get; set; }

    // Journal totals
    public decimal TotalDebit { get; set; }

    public decimal TotalCredit { get; set; }

    public TrialBalanceStatus Status { get; set; }

    // Audit information not already provided by RecordBase
    public string? CreatedBy { get; set; }

    public Attachment? Attachment { get; set; }
}

public class Attachment
{
    public string Name { get; set; } = string.Empty;

    public string Path { get; set; } = string.Empty;
}

public class JournalItem
{
    // Reference ChartAccount by its code.
    // No ChartAccountId is required.
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



public class JournalDB : RecordDB<Journal>
{
    private readonly IWebHostEnvironment _environment;

    public JournalDB(
        DBConfiguration configuration,
        IWebHostEnvironment environment)
        : base(configuration)
    {
        _environment = environment;
    }

    public override string CollectionName => "Journals";

    // ============================================================
    // GENERATE JOURNAL NUMBER
    // Example: TB-21-J01, TB-21-J02
    // ============================================================

    public async Task<string> GenerateJournalNumberAsync(
        string trialBalanceId,
        string trialBalanceNumber)
    {
        var latest = await GetCollection()
            .Find(x =>
                x.TrialBalance.Id == trialBalanceId &&
                !x.IsDeleted)
            .SortByDescending(x => x.CreatedAt)
            .FirstOrDefaultAsync();

        var nextNumber = 1;

        if (latest != null &&
            !string.IsNullOrWhiteSpace(latest.Number))
        {
            var suffix = latest.Number
                .Split("-J")
                .LastOrDefault();

            if (int.TryParse(suffix, out var parsed))
                nextNumber = parsed + 1;
        }

        return $"{trialBalanceNumber}-J{nextNumber:00}";
    }

    // ============================================================
    // CREATE JOURNAL
    // ============================================================
    public async Task<ApiResponse<object>> CreateJournalAsync(
        Journal request,
        TrialBalanceDB trialBalanceDB,
        ChartAccountDB chartAccountDB,
        AccountTypeDB accountTypeDB)
    {
        var response = new ApiResponse<object>();

        try
        {
            // 1. Validate request.
            if (request == null)
            {
                response.Message = "Invalid journal request.";
                response.AddError("Journal request is required.");
                return response;
            }

            if (request.TrialBalance == null ||
                string.IsNullOrWhiteSpace(request.TrialBalance.Id))
            {
                response.Message = "Invalid trial balance reference.";
                response.AddError("TrialBalance.Id is required.");
                return response;
            }

            // 2. Get trial balance.
            var trialBalance = await trialBalanceDB
                .GetByIdAsync(request.TrialBalance.Id);

            if (trialBalance == null)
            {
                response.Message = "Trial balance not found.";
                response.AddError("The referenced trial balance does not exist.");
                return response;
            }

            // 3. Validate journal items.
            if (request.Items == null || request.Items.Count == 0)
            {
                response.Message = "Invalid journal items.";
                response.AddError(
                    "Journal must contain at least one item.");

                return response;
            }

            // 4. Resolve chart accounts and account types.
            foreach (var item in request.Items)
            {
                if (string.IsNullOrWhiteSpace(item.AccountCode))
                {
                    response.Message = "Invalid journal item.";
                    response.AddError("Account code is required.");
                    return response;
                }

                var account = await chartAccountDB
                    .GetAllbyCode(item.AccountCode);

                if (account == null)
                {
                    response.Message = "Chart account not found.";
                    response.AddError(
                        $"Chart account not found: {item.AccountCode}");

                    return response;
                }

                var accountType = await accountTypeDB
                    .GetByIdAsync(account.AccountTypeId);

                if (accountType == null)
                {
                    response.Message = "Account type not found.";
                    response.AddError(
                        $"Account type not found for account: {item.AccountCode}");

                    return response;
                }

                item.AccountName = account.AccountName;
                item.Nature = accountType.Nature;
            }

            // 5. Calculate journal totals.
            request.TotalDebit = request.Items.Sum(x => x.Debit);
            request.TotalCredit = request.Items.Sum(x => x.Credit);
            request.ItemsCount = request.Items.Count;

            request.Status =
                request.TotalDebit == request.TotalCredit
                    ? TrialBalanceStatus.Balanced
                    : TrialBalanceStatus.Unbalanced;

            // 6. Generate journal identity and number.
            request.Id = ObjectId.GenerateNewId().ToString();

            request.Number = await GenerateJournalNumberAsync(
                trialBalance.Id,
                trialBalance.RefNo);

            // 7. Copy trial balance reference and metadata.
            request.TrialBalance = new TrialBalanceReference
            {
                Id = trialBalance.Id,
                Name = trialBalance.RefNo
            };

            request.Type = trialBalance.Type;
            request.ImportType = trialBalance.ImportType;
            request.CsvImportType = trialBalance.CsvImportType;
            request.PeriodStart = trialBalance.PeriodStart;
            request.PeriodEnd = trialBalance.PeriodEnd;
            request.CreatedAt = DateTime.UtcNow;

            // 8. Save journal.
            await GetCollection().InsertOneAsync(request);

            // 9. Calculate totals from all active journals linked
            //    to this trial balance.
            var journals = await GetCollection()
                .Find(x =>
                    x.TrialBalance.Id == trialBalance.Id &&
                    !x.IsDeleted)
                .ToListAsync();

            var totalDebit = journals.Sum(x => x.TotalDebit);
            var totalCredit = journals.Sum(x => x.TotalCredit);

            // 10. Update the TrialBalances collection through its DB class.
            var updated = await trialBalanceDB.UpdateJournalSummaryAsync(
                trialBalance.Id,
                request.Id,
                totalDebit,
                totalCredit);

            if (!updated)
            {
                response.Message =
                    "Journal saved, but trial balance summary update failed.";

                response.AddError(
                    trialBalanceDB.LastError?.Message ??
                    "Could not update the trial balance.");

                return response;
            }

            // 11. Build the response using the existing ApiResponse<T>.
            trialBalance.JournalIds ??= new List<string>();

            if (!trialBalance.JournalIds.Contains(request.Id))
                trialBalance.JournalIds.Add(request.Id);

            trialBalance.TotalDebit = totalDebit;
            trialBalance.TotalCredit = totalCredit;

            trialBalance.Status =
                totalDebit == totalCredit
                    ? TrialBalanceStatus.Balanced
                    : TrialBalanceStatus.Unbalanced;

            response.Status = true;
            response.Message = "Journal created successfully.";
            response.Result = new
            {
                journal = request,
                trialBalance = new
                {
                    id = trialBalance.Id,
                    name = trialBalance.RefNo,
                    journalIds = trialBalance.JournalIds,
                    totalDebit = trialBalance.TotalDebit,
                    totalCredit = trialBalance.TotalCredit,
                    status = trialBalance.Status
                }
            };

            return response;
        }
        catch (Exception ex)
        {
            LastError = ex;

            response.Message = "Failed to create journal.";
            response.AddError(ex.Message);

            return response;
        }
    }

    // ============================================================
    // GET JOURNALS BY TRIAL BALANCE ID
    // ============================================================

    // public async Task<List<Journal>> GetByTrialBalanceIdAsync(
    //     string trialBalanceId,
    //     bool onlyPosted = false)
    // {
    //     try
    //     {
    //         LastError = null;

    //         if (string.IsNullOrWhiteSpace(trialBalanceId))
    //             return new List<Journal>();

    //         var filterBuilder = Builders<Journal>.Filter;

    //         var filter = filterBuilder.And(
    //             filterBuilder.Eq(x => x.TrialBalance.Id, trialBalanceId),
    //             filterBuilder.Where(x => !x.IsDeleted)
    //         );

    //         if (onlyPosted)
    //         {
    //             filter = filterBuilder.And(
    //                 filter,
    //                 filterBuilder.Eq(
    //                     x => x.JournalStatus,
    //                     JournalStatus.Posted)
    //             );
    //         }

    //         return await GetCollection()
    //             .Find(filter)
    //             .SortBy(x => x.CreatedAt)
    //             .ToListAsync();
    //     }
    //     catch (Exception ex)
    //     {
    //         LastError = ex;
    //         return new List<Journal>();
    //     }
    // }

    // ============================================================
    // GET JOURNALS BY IDS
    // ============================================================

    public async Task<List<Journal>> GetByIdsAsync(
        IEnumerable<string>? journalIds)
    {
        try
        {
            LastError = null;

            var ids = journalIds?
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct()
                .ToList() ?? new List<string>();

            if (ids.Count == 0)
                return new List<Journal>();

            return await GetCollection()
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



    public async Task<List<Journal>> GetByTrialBalanceIdAsync(
        string trialBalanceId,
        bool onlyPosted = false)
    {
        try
        {
            LastError = null;

            if (string.IsNullOrWhiteSpace(trialBalanceId))
                return new List<Journal>();

            var filterBuilder = Builders<Journal>.Filter;

            var filter = filterBuilder.And(
                filterBuilder.Eq(
                    x => x.TrialBalance.Id,
                    trialBalanceId
                ),
                filterBuilder.Where(x => !x.IsDeleted)
            );

            if (onlyPosted)
            {
                filter = filterBuilder.And(
                    filter,
                    filterBuilder.Eq(
                        x => x.JournalStatus,
                        JournalStatus.Posted
                    )
                );
            }

            return await GetCollection()
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



public async Task<bool> AddJournalIdAsync(
    string trialBalanceId,
    string journalId)
{
    try
    {
        LastError = null;

        var update = Builders<TrialBalance>.Update
            .AddToSet(x => x.JournalIds, journalId);

        var result = await _database
            .GetCollection<TrialBalance>("TrialBalances")
            .UpdateOneAsync(
                x => x.Id == trialBalanceId && !x.IsDeleted,
                update
            );

        return result.MatchedCount > 0;
    }
    catch (Exception ex)
    {
        LastError = ex;
        return false;
    }
}

    // public async Task<List<Journal>> GetByIdsAsync(
    //     IEnumerable<string>? journalIds)
    // {
    //     try
    //     {
    //         LastError = null;

    //         var ids = journalIds?
    //             .Where(x => !string.IsNullOrWhiteSpace(x))
    //             .Distinct()
    //             .ToList() ?? new List<string>();

    //         if (ids.Count == 0)
    //             return new List<Journal>();

    //         return await GetCollection()
    //             .Find(x => ids.Contains(x.Id) && !x.IsDeleted)
    //             .SortBy(x => x.CreatedAt)
    //             .ToListAsync();
    //     }
    //     catch (Exception ex)
    //     {
    //         LastError = ex;
    //         return new List<Journal>();
    //     }
    // }

    public async Task<Attachment> SaveJournalAttachmentAsync(
    IFormFile attachment)
{
    var uploadFolder = Path.Combine(
        _environment.WebRootPath ?? "wwwroot",
        "uploads",
        "journals"
    );

    Directory.CreateDirectory(uploadFolder);

    var extension = Path.GetExtension(attachment.FileName);
    var fileName = $"{Guid.NewGuid():N}{extension}";
    var filePath = Path.Combine(uploadFolder, fileName);

    await using var stream = new FileStream(
        filePath,
        FileMode.CreateNew
    );

    await attachment.CopyToAsync(stream);

    return new Attachment
    {
        Name = Path.GetFileName(attachment.FileName),
        Path = Path.Combine(
            "uploads",
            "journals",
            fileName
        ).Replace("\\", "/")
    };
}

}