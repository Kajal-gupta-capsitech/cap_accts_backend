using BackendAcctTask.Models;
using Microsoft.AspNetCore.Mvc;
using MongoDB.Bson;
using MongoDB.Driver;
using System.Globalization;
using System.Text;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace BackendAcctTask.Controllers;

[ApiController]
[Route("api/TrialBalances")]
public class TrialBalancesController : ControllerBase
{
    private readonly IWebHostEnvironment _environment;

    private readonly IMongoCollection<TrialBalance> _trialBalances;
    private readonly IMongoCollection<Import> _imports;
    private readonly IMongoCollection<AccountingPeriod> _accountingPeriods;
    private readonly IMongoCollection<ChartAccount> _chartAccounts;
    private readonly IMongoCollection<Journal> _journals;

    private readonly IMongoCollection<AccountType> _accountTypes;

    public TrialBalancesController(
        IMongoDatabase database,
        IWebHostEnvironment environment)
    {
        _environment = environment;

        _trialBalances =
            database.GetCollection<TrialBalance>("TrialBalances");

        _imports =
            database.GetCollection<Import>(
                "Imports"
            );

        _accountingPeriods =
            database.GetCollection<AccountingPeriod>(
                "AccountingPeriods"
            );
        _journals =
             database.GetCollection<Journal>("Journals");

        _chartAccounts =
            database.GetCollection<ChartAccount>(
                "ChartAccounts"
            );
        _accountTypes =
           database.GetCollection<AccountType>("AccountTypes");
    }

    // ============================================================
    // FILE SAVE
    // ============================================================


    private async Task<string> SaveFile(
        IFormFile file,
        string folder)
    {
        var rootPath = Path.Combine(
            _environment.ContentRootPath,
            "Uploads",
            "TrialBalances",
            folder
        );

        Directory.CreateDirectory(rootPath);

        var extension =
            Path.GetExtension(file.FileName);

        var fileName =
            $"{Guid.NewGuid():N}{extension}";

        var fullPath =
            Path.Combine(rootPath, fileName);

        await using var stream =
            new FileStream(
                fullPath,
                FileMode.Create
            );

        await file.CopyToAsync(stream);

        // Store relative path in MongoDB
        return Path.Combine(
            "Uploads",
            "TrialBalances",
            folder,
            fileName
        ).Replace("\\", "/");
    }


    // ============================================================
    // GENERATE TRIAL BALANCE NUMBER
    // TB-01, TB-02, TB-03...
    // ============================================================

    private async Task<string> GenerateTrialBalanceNumber()
    {
        var latest = await _trialBalances
            .Find(FilterDefinition<TrialBalance>.Empty)
            .SortByDescending(x => x.RefNo)
            .FirstOrDefaultAsync();

        if (latest == null ||
            string.IsNullOrWhiteSpace(latest.RefNo))
        {
            return "TB-01";
        }

        var numberPart =
            latest.RefNo
                .Replace("TB-", "");

        if (!int.TryParse(
                numberPart,
                out var currentNumber))
        {
            currentNumber = 0;
        }

        return $"TB-{currentNumber + 1:00}";
    }


    // ============================================================
    // GENERATE IMPORT NUMBER
    //
    // IMPORTANT:
    // Numbering is maintained PER trial balance.
    //
    // TB-20-I01
    // TB-20-I02
    //
    // TB-21-I01
    // ============================================================

    //private async Task<string> GenerateImportNumber(
    //    string trialBalanceId,
    //    string trialBalanceNumber)
    //{
    //    var latest = await _imports
    //        .Find(x =>
    //            x.TrialBalanceId == trialBalanceId
    //        )
    //        .SortByDescending(x => x.CreatedAt)
    //        .FirstOrDefaultAsync();

    //    var number = 1;

    //    if (latest != null &&
    //        !string.IsNullOrWhiteSpace(latest.RefNo))
    //    {
    //        var existingNumber =
    //            latest.RefNo
    //                .Split("-I")
    //                .LastOrDefault();

    //        if (int.TryParse(
    //                existingNumber,
    //                out var parsed))
    //        {
    //            number = parsed + 1;
    //        }
    //    }

    //    return $"{trialBalanceNumber}-I{number:00}";
    //}

    private async Task<string> GenerateImportNumber(
    string trialBalanceId,
    string trialBalanceNumber)
    {
        var latest = await _imports
            .Find(x => x.TrialBalance.Id == trialBalanceId)
            .SortByDescending(x => x.CreatedAt)
            .FirstOrDefaultAsync();

        var number = 1;

        if (latest != null &&
            !string.IsNullOrWhiteSpace(latest.Number))
        {
            var existingNumber = latest.Number
                .Split("-I")
                .LastOrDefault();

            if (int.TryParse(existingNumber, out var parsed))
            {
                number = parsed + 1;
            }
        }

        return $"{trialBalanceNumber}-I{number:00}";
    }


    // ============================================================
    // RESOLVE CHART ACCOUNTS BY CODE
    //
    // TrialBalanceItem does NOT store ChartAccountId.
    //
    // AccountCode
    //      ↓
    // ChartAccounts.Code
    // ============================================================










    private async Task<List<ImportRow>>
        ParseTrialBalanceCsv(
            IFormFile file)
    {
        var rows =
            new List<ImportRow>();

        using var stream =
            file.OpenReadStream();

        using var reader =
            new StreamReader(stream);

        // Skip header
        var headerLine =
            await reader.ReadLineAsync();

        if (string.IsNullOrWhiteSpace(headerLine))
        {
            return rows;
        }


        while (!reader.EndOfStream)
        {
            var line =
                await reader.ReadLineAsync();

            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            var values =
                ParseCsvLine(line);

            if (values.Count < 4)
            {
                continue;
            }


            var code =
                values[0].Trim();

            var name =
                values[1].Trim();


            decimal debit = 0;
            decimal credit = 0;


            decimal.TryParse(
                values[2],
                NumberStyles.Any,
                CultureInfo.InvariantCulture,
                out debit
            );

            decimal.TryParse(
                values[3],
                NumberStyles.Any,
                CultureInfo.InvariantCulture,
                out credit
            );


            if (string.IsNullOrWhiteSpace(code) &&
                string.IsNullOrWhiteSpace(name))
            {
                continue;
            }


            var nature =
                debit > 0
                    ? AccountNature.Debit
                    : AccountNature.Credit;


            rows.Add(
                new ImportRow
                {
                    Code = code,

                    Name = name,

                    Nature = nature,

                    Debit = debit,

                    Credit = credit,

                    Note = string.Empty
                }
            );
        }


        return rows;
    }

    private List<string> ParseCsvLine(string line)
    {
        var values =
            new List<string>();

        var current =
            new StringBuilder();

        bool insideQuotes = false;


        for (int i = 0; i < line.Length; i++)
        {
            char c = line[i];


            if (c == '"')
            {
                // Escaped quote ""
                if (insideQuotes &&
                    i + 1 < line.Length &&
                    line[i + 1] == '"')
                {
                    current.Append('"');
                    i++;
                }
                else
                {
                    insideQuotes =
                        !insideQuotes;
                }

                continue;
            }


            if (c == ',' &&
                !insideQuotes)
            {
                values.Add(
                    current
                        .ToString()
                        .Trim()
                );

                current.Clear();

                continue;
            }


            current.Append(c);
        }


        values.Add(
            current
                .ToString()
                .Trim()
        );


        return values;
    }
    [HttpPost]
    [Consumes("multipart/form-data")]
    public async Task<IActionResult> Create(
        [FromForm] string? periodId,
        [FromForm] string? accountingPeriodId,
        [FromForm] DateTime? periodStart,
        [FromForm] DateTime? periodEnd,
        [FromForm] TrialBalanceType? type,
        [FromForm] TrialBalanceType? trialBalanceType,
        [FromForm] ImportType? importType,
        [FromForm] ImportType? importMode,
        [FromForm] CsvImportType? csvImportType,
        [FromForm] string? importFormat,
        [FromForm] string? journalId,
        [FromForm] string? description,
        [FromForm] decimal turnover,
        IFormFile? csvFile,
        IFormFile? file,
        IFormFile? attachment)
    {
        IFormFile? effectiveCsvFile =
            csvFile ?? file;

        string? effectivePeriodId =
            !string.IsNullOrWhiteSpace(periodId)
                ? periodId
                : accountingPeriodId;

        TrialBalanceType effectiveType =
            type
            ?? trialBalanceType
            ?? TrialBalanceType.Statutory;

        ImportType effectiveImportType =
            importType
            ?? importMode
            ?? (
                effectiveCsvFile != null
                    ? ImportType.Csv
                    : ImportType.Manual
            );

        CsvImportType effectiveCsvImportType =
            csvImportType
            ?? CsvImportType.Default;


        // ========================================================
        // PERIOD
        // ========================================================

        DateTime finalPeriodStart;
        DateTime finalPeriodEnd;


        // ========================================================
        // STATUTORY
        // ========================================================

        if (effectiveType == TrialBalanceType.Statutory)
        {
            if (string.IsNullOrWhiteSpace(effectivePeriodId))
            {
                return BadRequest(new
                {
                    status = false,
                    message =
                        "PeriodId is required for statutory trial balance."
                });
            }

            var period =
                await _accountingPeriods
                    .Find(x => x.Id == effectivePeriodId)
                    .FirstOrDefaultAsync();

            if (period == null)
            {
                return BadRequest(new
                {
                    status = false,
                    message =
                        "Accounting period not found."
                });
            }

            finalPeriodStart = period.PeriodFrom;
            finalPeriodEnd = period.PeriodTo;
        }

        // ========================================================
        // MANAGEMENT
        // ========================================================

        else
        {
            if (!periodStart.HasValue ||
                !periodEnd.HasValue)
            {
                return BadRequest(new
                {
                    status = false,
                    message =
                        "PeriodStart and PeriodEnd are required for management trial balance."
                });
            }

            finalPeriodStart = periodStart.Value;
            finalPeriodEnd = periodEnd.Value;
        }


        // ========================================================
        // PERIOD VALIDATION
        // ========================================================

        if (finalPeriodStart > finalPeriodEnd)
        {
            return BadRequest(new
            {
                status = false,
                message =
                    "PeriodStart cannot be greater than PeriodEnd."
            });
        }


        // ========================================================
        // JOURNAL VALIDATION
        // ========================================================

        // Journal? journal = null;

        // if (!string.IsNullOrWhiteSpace(journalId))
        // {
        //     journal =
        //         await _journals
        //             .Find(x => x.Id == journalId)
        //             .FirstOrDefaultAsync();

        //     if (journal == null)
        //     {
        //         return BadRequest(new
        //         {
        //             status = false,
        //             message = "Journal not found."
        //         });
        //     }
        // }


        // ========================================================
        // CSV VALIDATION
        // ========================================================

        if (effectiveImportType == ImportType.Csv &&
            effectiveCsvFile == null)
        {
            return BadRequest(new
            {
                status = false,
                message =
                    "CSV file is required when import type is CSV."
            });
        }


        // ========================================================
        // GENERATE TRIAL BALANCE NUMBER
        // ========================================================

        var refNo =
            await GenerateTrialBalanceNumber();


        // ========================================================
        // CREATE TRIAL BALANCE
        // ========================================================

        var trialBalance =
            new TrialBalance
            {
                Id =
                    ObjectId.GenerateNewId().ToString(),

                RefNo =
                    refNo,

                PeriodId =
                    effectiveType == TrialBalanceType.Statutory
                        ? effectivePeriodId
                        : null,

                PeriodStart =
                    finalPeriodStart,

                PeriodEnd =
                    finalPeriodEnd,

                Type =
                    effectiveType,

                Description =
                    description ?? string.Empty,

                ImportType =
                    effectiveImportType,

                CsvImportType =
                    effectiveCsvImportType,

                Status =
                    TrialBalanceStatus.Unbalanced,

                IsLocked =
                    false,

                Turnover =
                    turnover,

                TotalDebit =
                    0,

                TotalCredit =
                    0,

                TotalProfitLoss =
                    0,

                AccountReports =
                    null,

                // ==================================================
                // IMPORTANT
                // ==================================================
                // TrialBalance does NOT contain Items anymore.

                ImportIds =
                    new List<string>(),

                JournalIds =
                    new List<string>()
            };


        // ========================================================
        // SAVE CSV FILE
        // ========================================================

        if (effectiveCsvFile != null)
        {
            trialBalance.CsvFilePath =
                await SaveFile(
                    effectiveCsvFile,
                    "csv"
                );
        }


        // ========================================================
        // SAVE ATTACHMENT
        // ========================================================

        if (attachment != null)
        {
            trialBalance.AttachmentFilePath =
                await SaveFile(
                    attachment,
                    "attachments"
                );
        }


        // ========================================================
        // JOURNAL RELATION
        // ========================================================

        // if (journal != null)
        // {
        //     trialBalance.JournalIds.Add(journal.Id);
        // }


        // ========================================================
        // INSERT TRIAL BALANCE
        // ========================================================

        await _trialBalances
            .InsertOneAsync(trialBalance);


        // ========================================================
        // CSV IMPORT
        // ========================================================

        if (effectiveImportType == ImportType.Csv &&
            effectiveCsvFile != null)
        {
            List<ImportRow> rows;

            try
            {
                rows =
                    await ParseTrialBalanceCsv(
                        effectiveCsvFile
                    );
            }
            catch (Exception ex)
            {
                // Trial Balance was already inserted.
                // Remove it because CSV import failed.
                await _trialBalances.DeleteOneAsync(
                    x => x.Id == trialBalance.Id
                );

                return BadRequest(new
                {
                    status = false,
                    message =
                        "Unable to process CSV file.",
                    error = ex.Message
                });
            }


            if (rows.Count == 0)
            {
                await _trialBalances.DeleteOneAsync(
                    x => x.Id == trialBalance.Id
                );

                return BadRequest(new
                {
                    status = false,
                    message =
                        "CSV file does not contain any valid data."
                });
            }


            // ====================================================
            // GENERATE IMPORT NUMBER
            // ====================================================

            var importNumber =
                await GenerateImportNumber(
                    trialBalance.Id,
                    trialBalance.RefNo
                );


            // ====================================================
            // CREATE IMPORT
            // ====================================================

            var import =
                new Import
                {
                    Id =
                        ObjectId.GenerateNewId().ToString(),

                    Number =
                        importNumber,

                    // IMPORTANT:
                    // We now link using TrialBalance RefNo.
                    TrialBalance = {
                    Id = trialBalance.Id,
                    Name = trialBalance.RefNo,
                    },


                    Headers =
                        new List<string>
                        {
                        "Account Code",
                        "Account Name",
                        "Debit",
                        "Credit"
                        },
                    PeriodStart =
                    trialBalance.PeriodStart,

                    PeriodEnd =
                    trialBalance.PeriodEnd,

                    Columns =
                        new List<ImportColumn>(),

                    Rows =
                        rows,

                    ImportType =
                        ImportType.Csv,

                    CsvImportType =
                        effectiveCsvImportType,

                    CreatedAt =
                        DateTime.UtcNow
                };


            // ====================================================
            // INSERT IMPORT
            // ====================================================

            await _imports
                .InsertOneAsync(import);


            // ====================================================
            // ADD IMPORT ID TO TRIAL BALANCE
            // ====================================================

            trialBalance.ImportIds.Add(
                import.Id
            );


            await _trialBalances.UpdateOneAsync(
                x => x.Id == trialBalance.Id,
                Builders<TrialBalance>.Update
                    .Set(
                        x => x.ImportIds,
                        trialBalance.ImportIds
                    )
            );
        }


        // ========================================================
        // RESPONSE
        // ========================================================

        return Ok(new
        {
            status = true,

            message =
                "Trial balance created successfully.",

            result = new
            {
                id = trialBalance.Id,

                refNo = trialBalance.RefNo,

                periodId = trialBalance.PeriodId,

                periodStart =
                    trialBalance.PeriodStart,

                periodEnd =
                    trialBalance.PeriodEnd,

                type =
                    trialBalance.Type,

                description =
                    trialBalance.Description,

                importType =
                    trialBalance.ImportType,

                csvImportType =
                    trialBalance.CsvImportType,

                importIds =
                    trialBalance.ImportIds,

                journalIds =
                    trialBalance.JournalIds,

                totalDebit =
                    trialBalance.TotalDebit,

                totalCredit =
                    trialBalance.TotalCredit,

                status =
                    trialBalance.Status
            }
        });
    }

    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        var trialBalances =
            await _trialBalances
                .Find(
                    FilterDefinition<TrialBalance>
                        .Empty
                )
                .SortByDescending(x => x.RefNo)
                .ToListAsync();

        var result =
            new List<object>();

        foreach (var tb in trialBalances)
        {
            object? period = null;

            Console.WriteLine("========================================");
            Console.WriteLine("GET ALL TRIAL BALANCES");
            Console.WriteLine($"Trial Balance ID: {tb.Id}");
            Console.WriteLine($"Trial Balance RefNo: {tb.RefNo}");
            Console.WriteLine("========================================");

            // ----------------------------------------------------
            // Get Accounting Period
            // ----------------------------------------------------

            if (!string.IsNullOrWhiteSpace(tb.PeriodId))
            {
                period =
                    await _accountingPeriods
                        .Find(x =>
                            x.Id == tb.PeriodId
                        )
                        .FirstOrDefaultAsync();
            }

            if (period == null &&
                (tb.PeriodStart != default ||
                 tb.PeriodEnd != default))
            {
                period = new
                {
                    id = string.Empty,
                    periodStart = tb.PeriodStart,
                    periodEnd = tb.PeriodEnd,
                    isActive = true,
                    isClosed = false
                };
            }

            // ----------------------------------------------------
            // Journal IDs
            // ----------------------------------------------------

            var journalIds =
                tb.JournalIds ??
                new List<string>();

            var itemsCount = 0;

            if (journalIds.Count > 0)
            {
                var journals =
                    await _journals
                        .Find(x =>
                            journalIds.Contains(x.Id))
                        .ToListAsync();

                itemsCount =
                    journals.Sum(x =>
                        x.Items?.Count ?? 0);
            }

            result.Add(
                new
                {
                    refNo =
                        tb.RefNo,

                    periodStart =
                        tb.PeriodStart,

                    periodEnd =
                        tb.PeriodEnd,

                    period,

                    description =
                        tb.Description,

                    type =
                        tb.Type,

                    turnover =
                        tb.Turnover,

                    importType =
                        tb.ImportType,

                    csvImportType =
                        tb.CsvImportType,

                    totalDebit =
                        tb.TotalDebit,

                    totalCredit =
                        tb.TotalCredit,

                    totalProfitLoss =
                        tb.TotalProfitLoss,

                    status =
                        tb.Status,

                    accountReports =
                        tb.AccountReports,

                    isLocked =
                        tb.IsLocked,

                    csvFilePath =
                        tb.CsvFilePath,

                    attachmentFilePath =
                        tb.AttachmentFilePath,

                    itemsCount,

                    journalIds,

                    id =
                        tb.Id
                }
            );
        }

        return Ok(new
        {
            status = true,
            result
        });
    }


    [HttpGet("{RefNo}")]
    public async Task<IActionResult> GetById(
        string RefNo)
    {
        // ============================================================
        // FIND TRIAL BALANCE
        // ============================================================

        var tb =
            await _trialBalances
                .Find(x => x.RefNo == RefNo)
                .FirstOrDefaultAsync();

        if (tb == null)
        {
            return NotFound(new
            {
                status = false,
                message = "Trial balance not found."
            });
        }


        // ============================================================
        // ACCOUNTING PERIOD
        // ============================================================

        object? period = null;

        if (!string.IsNullOrWhiteSpace(tb.PeriodId))
        {
            period =
                await _accountingPeriods
                    .Find(x => x.Id == tb.PeriodId)
                    .FirstOrDefaultAsync();
        }

        if (period == null &&
            (tb.PeriodStart != default ||
             tb.PeriodEnd != default))
        {
            period = new
            {
                id = string.Empty,

                periodFrom =
                    tb.PeriodStart,

                periodTo =
                    tb.PeriodEnd,

                periodStart =
                    tb.PeriodStart,

                periodEnd =
                    tb.PeriodEnd,

                isActive = true,

                isClosed = false
            };
        }


        // ============================================================
        // JOURNAL IDS
        // ============================================================

        var journalIds =
            tb.JournalIds ??
            new List<string>();


        // ============================================================
        // GET JOURNALS
        // ============================================================

        var journals =
            journalIds.Count == 0
                ? new List<Journal>()
                : await _journals
                    .Find(x =>
                        journalIds.Contains(x.Id))
                    .SortBy(x => x.CreatedAt)
                    .ToListAsync();


        // ============================================================
        // BUILD ITEMS FROM JOURNALS
        //
        // IMPORTANT:
        // TrialBalance does NOT contain Items.
        // Journal is the source of truth.
        // ============================================================

        var items =
            new List<object>();


        foreach (var journal in journals)
        {
            if (journal.Items == null ||
                journal.Items.Count == 0)
            {
                continue;
            }


            foreach (var journalItem in journal.Items)
            {
                var account =
                    await _chartAccounts
                        .Find(x =>
                            x.Code ==
                            journalItem.AccountCode)
                        .FirstOrDefaultAsync();


                items.Add(
                    new
                    {
                        account =
                            account == null
                                ? new
                                {
                                    id = string.Empty,

                                    name =
                                        journalItem.AccountName,

                                    code =
                                        journalItem.AccountCode
                                }
                                : new
                                {
                                    id =
                                        account.Id,

                                    name =
                                        account.AccountName,

                                    code =
                                        account.Code
                                },

                        note =
                            journalItem.Note,

                        amount =
                            journalItem.Credit > 0
                                ? -journalItem.Credit
                                : journalItem.Debit,

                        debit =
                            journalItem.Debit,

                        credit =
                            journalItem.Credit,

                        type =
                            journalItem.Nature
                    });
            }
        }


        // ============================================================
        // CALCULATE TOTALS FROM JOURNALS
        // ============================================================

        var totalDebit =
            journals.Sum(
                x => x.TotalDebit);

        var totalCredit =
            journals.Sum(
                x => x.TotalCredit);


        // ============================================================
        // TRIAL BALANCE STATUS
        // ============================================================

        var status =
            totalDebit == totalCredit
                ? TrialBalanceStatus.Balanced
                : TrialBalanceStatus.Unbalanced;


        // ============================================================
        // JOURNAL RESPONSE
        // ============================================================

        var journalResponse =
            journals
                .Select(journal => new
                {
                    id =
                        journal.Id,

                    number =
                        journal.Number,

                    trialBalance =
                        new
                        {
                            id =
                                journal.TrialBalance.Id,

                            name =
                                journal.TrialBalance.Name
                        },

                    type =
                        journal.Type,

                    status =
                        journal.Status,

                    itemsCount =
                        journal.Items?.Count ?? 0,

                    totalDebit =
                        journal.TotalDebit,

                    totalCredit =
                        journal.TotalCredit,

                    importType =
                        journal.ImportType,

                    csvImportType =
                        journal.CsvImportType,

                    createdAt =
                        journal.CreatedAt
                })
                .ToList();


        // ============================================================
        // RESPONSE
        // ============================================================

        return Ok(new
        {
            result =
                new
                {
                    trialBalance =
                        new
                        {
                            name =
                                tb.RefNo,

                            id =
                                tb.Id
                        },

                    period,

                    periodStart =
                        tb.PeriodStart,

                    periodEnd =
                        tb.PeriodEnd,

                    description =
                        tb.Description,

                    // Items come from Journal.Items
                    items,

                    type =
                        tb.Type,

                    status,

                    itemsCount =
                        items.Count,

                    totalDebit,

                    totalCredit,

                    totalProfitLoss =
                        tb.TotalProfitLoss,

                    turnover =
                        tb.Turnover,

                    validation =
                        new { },

                    importType =
                        tb.ImportType,

                    csvImportType =
                        tb.CsvImportType,

                    journalIds,

                    journals =
                        journalResponse,

                    importIds =
                        tb.ImportIds ??
                        new List<string>(),

                    attachments =
                        string.IsNullOrWhiteSpace(
                            tb.AttachmentFilePath)
                            ? new List<object>()
                            : new List<object>
                            {
                            new
                            {
                                name =
                                    Path.GetFileName(
                                        tb.AttachmentFilePath),

                                path =
                                    tb.AttachmentFilePath
                            }
                            },

                    csvFilePath =
                        tb.CsvFilePath,

                    id =
                        tb.Id
                },

            status = true
        });
    }

    private async Task<string> GenerateJournalNumber(
        string trialBalanceId,
        string trialBalanceNumber)
    {
        var latest =
            await _journals
                .Find(x =>
                    x.TrialBalance.Id ==
                    trialBalanceId
                )
                .SortByDescending(x => x.CreatedAt)
                .FirstOrDefaultAsync();

        var number = 1;

        if (
            latest != null &&
            !string.IsNullOrWhiteSpace(
                latest.Number
            )
        )
        {
            var existingNumber =
                latest.Number
                    .Split("-J")
                    .LastOrDefault();

            if (
                int.TryParse(
                    existingNumber,
                    out var parsed
                )
            )
            {
                number = parsed + 1;
            }
        }

        return $"{trialBalanceNumber}-J{number:00}";
    }


    [HttpPut("{RefNo}")]
    [HttpPatch("{RefNo}")]
    public async Task<IActionResult> Update(
        string RefNo,
        [FromBody] TrialBalance request)
    {
        // ============================================================
        // FIND EXISTING TRIAL BALANCE
        // ============================================================

        var existing =
            await _trialBalances
                .Find(x => x.RefNo == RefNo)
                .FirstOrDefaultAsync();

        if (existing == null)
        {
            return NotFound(new
            {
                status = false,
                message = "Trial balance not found."
            });
        }


        // ============================================================
        // VALIDATE PERIOD
        // ============================================================

        if (request.Type == TrialBalanceType.Statutory)
        {
            if (string.IsNullOrWhiteSpace(request.PeriodId))
            {
                return BadRequest(new
                {
                    status = false,
                    message =
                        "PeriodId is required for statutory trial balance."
                });
            }

            var period =
                await _accountingPeriods
                    .Find(x => x.Id == request.PeriodId)
                    .FirstOrDefaultAsync();

            if (period == null)
            {
                return BadRequest(new
                {
                    status = false,
                    message =
                        "Accounting period not found."
                });
            }

            existing.PeriodId =
                request.PeriodId;

            existing.PeriodStart =
                period.PeriodFrom;

            existing.PeriodEnd =
                period.PeriodTo;
        }
        else
        {
            if (request.PeriodStart >
                request.PeriodEnd)
            {
                return BadRequest(new
                {
                    status = false,
                    message =
                        "PeriodStart cannot be greater than PeriodEnd."
                });
            }

            existing.PeriodId = null;

            existing.PeriodStart =
                request.PeriodStart;

            existing.PeriodEnd =
                request.PeriodEnd;
        }


        // ============================================================
        // UPDATE TRIAL BALANCE METADATA
        // ============================================================

        existing.Type =
            request.Type;

        existing.Description =
            request.Description;

        existing.ImportType =
            request.ImportType;

        existing.CsvImportType =
            request.CsvImportType;

        existing.Turnover =
            request.Turnover;


        // ============================================================
        // FILE PATHS
        // ============================================================

        if (!string.IsNullOrWhiteSpace(
                request.CsvFilePath))
        {
            existing.CsvFilePath =
                request.CsvFilePath;
        }

        if (!string.IsNullOrWhiteSpace(
                request.AttachmentFilePath))
        {
            existing.AttachmentFilePath =
                request.AttachmentFilePath;
        }


        // ============================================================
        // JOURNAL IDS
        // ============================================================

        existing.JournalIds ??=
            new List<string>();


        // ============================================================
        // UPDATE JOURNALS
        //
        // IMPORTANT:
        // We are NOT changing Journal.Items here.
        //
        // Trial Balance Update only changes Journal metadata
        // that comes from the Trial Balance.
        // ============================================================

        var journals =
            existing.JournalIds.Count == 0
                ? new List<Journal>()
                : await _journals
                    .Find(x =>
                        existing.JournalIds.Contains(x.Id) &&
                        x.TrialBalance.Id == existing.Id)
                    .ToListAsync();


        // ============================================================
        // OPTIONAL JOURNAL ID
        //
        // If request.JournalId is provided:
        // update only that Journal.
        //
        // If request.JournalId is not provided:
        // update all Journals belonging to this Trial Balance.
        // ============================================================

        List<Journal> journalsToUpdate;


        if (!string.IsNullOrWhiteSpace(
                request.JournalId))
        {
            // --------------------------------------------------------
            // Validate Journal belongs to Trial Balance
            // --------------------------------------------------------

            if (!existing.JournalIds.Contains(
                    request.JournalId))
            {
                return BadRequest(new
                {
                    status = false,
                    message =
                        "The specified journal does not belong to this trial balance."
                });
            }


            var selectedJournal =
                journals.FirstOrDefault(
                    x => x.Id == request.JournalId);

            if (selectedJournal == null)
            {
                return NotFound(new
                {
                    status = false,
                    message =
                        "Journal not found."
                });
            }


            journalsToUpdate =
                new List<Journal>
                {

                };
        }
        else
        {
            // --------------------------------------------------------
            // No specific JournalId.
            //
            // Update every Journal belonging to this Trial Balance.
            // --------------------------------------------------------

            journalsToUpdate =
                journals;
        }


        // ============================================================
        // UPDATE JOURNAL METADATA
        // ============================================================

        foreach (var journal in journalsToUpdate)
        {
            // Trial Balance relationship
            journal.TrialBalance = new TrialBalanceReference
            {
                Id = existing.Id,
                Name = existing.RefNo
            };


            // Journal type follows Trial Balance type
            journal.Type =
                existing.Type;

            // Import type follows Trial Balance
            journal.ImportType =
                existing.ImportType;

            // CSV import type follows Trial Balance
            journal.CsvImportType =
                existing.CsvImportType;


            // --------------------------------------------------------
            // IMPORTANT:
            // DO NOT MODIFY:
            //
            // journal.Items
            // journal.ItemsCount
            // journal.TotalDebit
            // journal.TotalCredit
            //
            // because Update Trial Balance is not changing
            // Journal transaction data.
            // --------------------------------------------------------

            await _journals
                .ReplaceOneAsync(
                    x => x.Id == journal.Id,
                    journal);
        }


        // ============================================================
        // RELOAD ALL JOURNALS
        //
        // Journals remain the source for Trial Balance totals.
        // ============================================================

        journals =
            existing.JournalIds.Count == 0
                ? new List<Journal>()
                : await _journals
                    .Find(x =>
                        existing.JournalIds.Contains(x.Id) &&
                        x.TrialBalance.Id == existing.Id)
                    .ToListAsync();


        // ============================================================
        // RECALCULATE TRIAL BALANCE TOTALS
        //
        // NO TrialBalance.Items
        // ============================================================

        existing.TotalDebit =
            journals.Sum(x => x.TotalDebit);

        existing.TotalCredit =
            journals.Sum(x => x.TotalCredit);


        // ============================================================
        // TRIAL BALANCE STATUS
        // ============================================================

        existing.Status =
            existing.TotalDebit ==
            existing.TotalCredit
                ? TrialBalanceStatus.Balanced
                : TrialBalanceStatus.Unbalanced;


        // ============================================================
        // PROFIT / LOSS
        // ============================================================

        existing.TotalProfitLoss =
            request.TotalProfitLoss;


        // ============================================================
        // SAVE TRIAL BALANCE
        // ============================================================

        var update =
            Builders<TrialBalance>
                .Update

                .Set(
                    x => x.PeriodId,
                    existing.PeriodId)

                .Set(
                    x => x.PeriodStart,
                    existing.PeriodStart)

                .Set(
                    x => x.PeriodEnd,
                    existing.PeriodEnd)

                .Set(
                    x => x.Type,
                    existing.Type)

                .Set(
                    x => x.Description,
                    existing.Description)

                .Set(
                    x => x.ImportType,
                    existing.ImportType)

                .Set(
                    x => x.CsvImportType,
                    existing.CsvImportType)

                .Set(
                    x => x.Turnover,
                    existing.Turnover)

                .Set(
                    x => x.CsvFilePath,
                    existing.CsvFilePath)

                .Set(
                    x => x.AttachmentFilePath,
                    existing.AttachmentFilePath)

                .Set(
                    x => x.JournalIds,
                    existing.JournalIds)

                .Set(
                    x => x.ImportIds,
                    existing.ImportIds)

                .Set(
                    x => x.TotalDebit,
                    existing.TotalDebit)

                .Set(
                    x => x.TotalCredit,
                    existing.TotalCredit)

                .Set(
                    x => x.TotalProfitLoss,
                    existing.TotalProfitLoss)

                .Set(
                    x => x.Status,
                    existing.Status);


        await _trialBalances
            .UpdateOneAsync(
                x => x.RefNo == RefNo,
                update);


        // ============================================================
        // RESPONSE
        // ============================================================

        return Ok(new
        {
            status = true,

            message =
                "Trial balance and  journals updated successfully.",

            result = new
            {
                trialBalance = new
                {
                    id = existing.Id,
                    name = existing.RefNo,
                    periodId = existing.PeriodId,
                    periodStart = existing.PeriodStart,
                    periodEnd = existing.PeriodEnd,
                    type = existing.Type,
                    description = existing.Description,
                    importType = existing.ImportType,
                    csvImportType = existing.CsvImportType,
                    turnover = existing.Turnover,
                    importIds = existing.ImportIds,
                    journalIds = existing.JournalIds,
                    totalDebit = existing.TotalDebit,
                    totalCredit = existing.TotalCredit,
                    totalProfitLoss = existing.TotalProfitLoss,
                    status = existing.Status
                },

                journals =
                    journals
                        .Select(x => new
                        {
                            id = x.Id,
                            number = x.Number,
                            trialBalance = new
                            {
                                id = x.TrialBalance.Id,
                                name = x.TrialBalance.Name
                            },
                            type = x.Type,
                            importType =
                                x.ImportType,
                            csvImportType =
                                x.CsvImportType,
                            itemsCount =
                                x.ItemsCount,
                            totalDebit =
                                x.TotalDebit,
                            totalCredit =
                                x.TotalCredit,
                            status =
                                x.Status
                        })
                        .ToList()
            }
        });
    }


    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(string id)
    {
        var existing =
            await _trialBalances
                .Find(x => x.Id == id)
                .FirstOrDefaultAsync();

        if (existing == null)
        {
            return NotFound(new
            {
                status = false,
                message = "Trial balance not found."
            });
        }

        await _trialBalances.DeleteOneAsync(x => x.Id == id);

        return Ok(new
        {
            status = true,
            message = "Trial balance deleted successfully."
        });
    }


    [HttpPost("{RefNo}/imports")]
    public async Task<IActionResult> Import(
        string RefNo,
        [FromBody] Import request)
    {
        // ============================================================
        // FIND TRIAL BALANCE USING REF NO
        // ============================================================

        var trialBalance =
            await _trialBalances
                .Find(x => x.RefNo == RefNo)
                .FirstOrDefaultAsync();

        if (trialBalance == null)
        {
            return NotFound(new
            {
                status = false,
                message = "Trial balance not found."
            });
        }


        // ============================================================
        // VALIDATE IMPORT ROWS
        // ============================================================

        if (
            request.Rows == null ||
            request.Rows.Count == 0
        )
        {
            return BadRequest(new
            {
                status = false,
                message = "Import must contain at least one row."
            });
        }


        // ============================================================
        // GENERATE IMPORT NUMBER
        //
        // Example:
        // TB-34-I01
        // TB-34-I02
        // ============================================================

        var importNumber =
            await GenerateImportNumber(
                trialBalance.Id,
                trialBalance.RefNo
            );


        // ============================================================
        // CREATE IMPORT
        //
        // IMPORTANT:
        //
        // Store the CSV data AS-IS.
        //
        // DO NOT:
        // - validate Chart Accounts
        // - resolve AccountCode
        // - change AccountName
        // - change Nature
        // - create JournalItems
        // ============================================================

        var import =
            new Import
            {
                Id =
                    ObjectId.GenerateNewId()
                        .ToString(),

                Number =
                    importNumber,

                TrialBalance =
                    new TrialBalanceReference
                    {
                        Id = trialBalance.Id,
                        Name = trialBalance.RefNo
                    },

                Columns =
                    request.Columns ??
                    new List<ImportColumn>(),

                Headers =
                    request.Headers ??
                    new List<string>(),

                Rows =
                    request.Rows,

                ImportType =
                    ImportType.Csv,

                PeriodStart =
                    trialBalance.PeriodStart,

                PeriodEnd =
                    trialBalance.PeriodEnd,

                CsvImportType =
                    request.CsvImportType,

                CreatedAt =
                    DateTime.UtcNow
            };


        // ============================================================
        // SAVE IMPORT
        // ============================================================

        await _imports
            .InsertOneAsync(import);


        // ============================================================
        // GENERATE JOURNAL NUMBER
        // ============================================================

        var journalNumber =
            await GenerateJournalNumber(
                trialBalance.Id,
                trialBalance.RefNo
            );


        // ============================================================
        // CREATE EMPTY JOURNAL
        //
        // IMPORTANT:
        //
        // Import creates the Journal DOCUMENT only.
        //
        // NO JournalItem records are created.
        //
        // The imported CSV may contain invalid / unknown
        // Chart Account codes. That is allowed at this stage.
        // ============================================================

        var journal =
            new Journal
            {
                Id =
                    ObjectId.GenerateNewId()
                        .ToString(),

                Number =
                    journalNumber,

                TrialBalance =
                    new TrialBalanceReference
                    {
                        Id = trialBalance.Id,
                        Name = trialBalance.RefNo
                    },

                Imports = new ImportsReference
                {
                    Id = import.Id,
                    Name = import.Number
                },

                Type =
                    trialBalance.Type,

                Status =
                    TrialBalanceStatus.Unbalanced,

                ImportType =
                    ImportType.Csv,

                CsvImportType =
                    request.CsvImportType,

                // IMPORTANT:
                // No JournalItem records during import.
                Items =
                    new List<JournalItem>(),

                ItemsCount = 0,

                TotalDebit = 0,

                TotalCredit = 0,

                CreatedAt =
                    DateTime.UtcNow
            };


        // ============================================================
        // SAVE EMPTY JOURNAL
        // ============================================================

        await _journals
            .InsertOneAsync(journal);


        // ============================================================
        // DO NOT ADD JOURNAL ID TO TRIAL BALANCE
        //
        // Import-created Journal is currently a staging/empty
        // Journal. It should not become part of the Trial Balance
        // journal aggregation until actual JournalItems are created.
        // ============================================================


        // ============================================================
        // RESPONSE
        // ============================================================

        return Ok(new
        {
            executionTime = 0,

            result = new
            {
                import = new
                {
                    name =
                        import.Number,

                    id =
                        import.Id,

                    rowsCount =
                        import.Rows.Count
                },

                journal = new
                {
                    number =
                        journal.Number,

                    id =
                        journal.Id,

                    imports =
                        journal.Imports,

                    itemsCount =
                        journal.ItemsCount,

                    totalDebit =
                        journal.TotalDebit,

                    totalCredit =
                        journal.TotalCredit,

                    status =
                        journal.Status
                },

                trialBalance = new
                {
                    name =
                        trialBalance.RefNo,

                    id =
                        trialBalance.Id
                },

                number =
                    import.Number,

                type =
                    trialBalance.Type,

                importType =
                    import.ImportType,

                csvImportType =
                    import.CsvImportType,

                id =
                    import.Id
            },

            status = true
        });
    }
    // ============================================================
    // GET IMPORTS BY TRIAL BALANCE
    //
    // GET
    // /api/trial-balances/{trialBalanceId}/imports
    //
    // Response is based on the IMPORT rows, not
    // TrialBalance.Items.
    //
    // amounts[] contains values from the imports.
    // ============================================================

    [HttpGet("{trialBalanceId}/imports")]
    public async Task<IActionResult> GetImports(
        string trialBalanceId)
    {
        var trialBalance =
            await _trialBalances
                .Find(x =>
                    x.Id == trialBalanceId
                )
                .FirstOrDefaultAsync();


        if (trialBalance == null)
        {
            return NotFound(new
            {
                status = false,
                message =
                    "Trial balance not found."
            });
        }


        var imports =
            await _imports
                .Find(x =>
                    x.TrialBalance.Id ==
                    trialBalanceId
                )
                .SortBy(x => x.CreatedAt)
                .ToListAsync();


        // ========================================================
        // BUILD UNIQUE ACCOUNT LIST
        // ========================================================

        var codes =
            imports
                .SelectMany(x => x.Rows)
                .Select(x => x.Code)
                .Where(x =>
                    !string.IsNullOrWhiteSpace(x))
                .Distinct()
                .ToList();


        var items =
            new List<object>();


        foreach (var code in codes)
        {
            var account =
                await _chartAccounts
                    .Find(x =>
                        x.Code == code
                    )
                    .FirstOrDefaultAsync();


            // All rows for this account
            var rows =
                imports
                    .SelectMany(x => x.Rows)
                    .Where(x =>
                        x.Code == code)
                    .ToList();


            // One amount per import.
            var amounts =
                imports
                    .Select(import =>
                    {
                        var row =
                            import.Rows
                                .FirstOrDefault(
                                    x =>
                                        x.Code == code
                                );

                        if (row == null)
                        {
                            return 0m;
                        }

                        return row.Credit > 0
                            ? -row.Credit
                            : row.Debit;
                    })
                    .ToList();


            var firstRow =
                rows.FirstOrDefault();


            items.Add(
                new
                {
                    type =
                        firstRow?.Nature ??
                        AccountNature.Debit,

                    code,

                    amounts,

                    rAmounts =
                        imports
                            .Select(x => 0m)
                            .ToList(),

                    accountNature =
                        firstRow?.Nature ??
                        AccountNature.Debit,

                    name =
                        account?.AccountName ??
                        firstRow?.Name ??
                        string.Empty,

                    id =
                        account?.Id ??
                        string.Empty
                }
            );
        }


        // ========================================================
        // RESPONSE
        // ========================================================

        return Ok(new
        {
            trialBalances =
                new[]
                {
                    new
                    {
                        periodStart =
                            trialBalance.PeriodStart,

                        periodEnd =
                            trialBalance.PeriodEnd,

                        totalDebit =
                            trialBalance.TotalDebit,

                        totalCredit =
                            trialBalance.TotalCredit,

                        name =
                            trialBalance.RefNo,

                        id =
                            trialBalance.Id
                    }
                },

            items
        });
    }


    // ============================================================
    // GET SINGLE IMPORT
    //
    // GET
    // /api/trial-balances/imports/{importId}
    // ============================================================

    [HttpGet("{RefNo}/imports/{importId}")]
    public async Task<IActionResult> GetImport(
        string RefNo, string importId)
    {
        // ============================================================
        // FIND IMPORT
        // ============================================================

        var import =
            await _imports
                .Find(x =>
                    x.Id == importId)
                .FirstOrDefaultAsync();

        if (import == null)
        {
            return NotFound(new
            {
                status = false,
                message = "Import not found."
            });
        }


        // ============================================================
        // FIND ASSOCIATED TRIAL BALANCE
        // ============================================================

        var trialBalance =
            await _trialBalances
                .Find(x =>
                    x.Id ==
                    import.TrialBalance.Id)
                .FirstOrDefaultAsync();

        if (trialBalance == null)
        {
            return NotFound(new
            {
                status = false,
                message =
                    "Associated trial balance not found."
            });
        }


        // ============================================================
        // BUILD ROWS
        //
        // Preserve the original imported CSV values.
        // ============================================================

        var rows =
            import.Rows
                .Select(row => new[]
                {
                row.Code,
                row.Name,

                row.Debit
                    .ToString(
                        System.Globalization.CultureInfo.InvariantCulture
                    ),

                row.Credit
                    .ToString(
                        System.Globalization.CultureInfo.InvariantCulture
                    )
                })
                .ToList();


        // ============================================================
        // RESPONSE
        // ============================================================

        return Ok(new
        {
            executionTime = 0,

            result = new
            {
                // ====================================================
                // IMPORT
                // ====================================================

                id =
                    import.Id,

                number =
                    import.Number,

                description =
                    import.Description ??
                    string.Empty,

                periodStart =
                    import.PeriodStart,

                periodEnd =
                    import.PeriodEnd,


                // ====================================================
                // TRIAL BALANCE
                // ====================================================

                trialBalance =
                    new
                    {
                        name =
                            trialBalance.RefNo,

                        id =
                            trialBalance.Id
                    },


                // ====================================================
                // CSV HEADER
                // ====================================================

                header =
                    import.Headers ??
                    new List<string>(),

                hasHeader =
                    import.Headers != null &&
                    import.Headers.Count > 0,


                // ====================================================
                // CSV IMPORT TYPE
                // ====================================================

                csvImportType =
                    import.CsvImportType,


                // ====================================================
                // IMPORT STATUS
                // ====================================================

                status =
                   import.Status.ToString().ToLowerInvariant(),


                // ====================================================
                // CSV ROWS
                // ====================================================

                rows =
                    rows
            },

            status = true
        });
    }

    // ============================================================
    // UPDATE IMPORT
    //
    // PUT
    // /api/trial-balances/imports/{importId}
    //
    // This is used when the user edits the imported CSV
    // rows before the final TrialBalance PUT.
    // ============================================================


    [HttpPut("imports/{importId}")]
    public async Task<IActionResult> UpdateImport(
        string importId,
        [FromBody] Import request)
    {
        var existing =
            await _imports
                .Find(x =>
                    x.Id == importId
                )
                .FirstOrDefaultAsync();


        if (existing == null)
        {
            return NotFound(new
            {
                status = false,
                message =
                    "Import not found."
            });
        }


        if (request.Rows == null ||
            request.Rows.Count == 0)
        {
            return BadRequest(new
            {
                status = false,
                message =
                    "Import must contain at least one row."
            });
        }


        // ========================================================
        // VALIDATE ACCOUNT CODES
        // ========================================================

        foreach (var row in request.Rows)
        {
            if (string.IsNullOrWhiteSpace(
                    row.Code))
            {
                return BadRequest(new
                {
                    status = false,
                    message =
                        "Account code is required."
                });
            }


            var account =
                await _chartAccounts
                    .Find(x =>
                        (!string.IsNullOrWhiteSpace(row.Code) && x.Code == row.Code) ||
                        (!string.IsNullOrWhiteSpace(row.Name) && x.AccountName == row.Name)
                    )
                    .FirstOrDefaultAsync();

            if (account != null)
            {
                if (string.IsNullOrWhiteSpace(row.Name))
                {
                    row.Name = account.AccountName;
                }
                if (string.IsNullOrWhiteSpace(row.Code))
                {
                    row.Code = account.Code;
                }
            }
        }



        // ========================================================
        // UPDATE ONLY EDITABLE IMPORT DATA
        //
        // Keep:
        // - Id
        // - Number
        // - TrialBalanceId
        // - CreatedAt
        // ========================================================

        existing.Rows =
            request.Rows;

        existing.Columns =
            request.Columns ??
            new List<ImportColumn>();

        existing.Headers =
            request.Headers ??
            new List<string>();


        await _imports
            .ReplaceOneAsync(
                x => x.Id == importId,
                existing
            );


        return Ok(new
        {
            status = true,

            message =
                "Import updated successfully.",

            result =
                existing
        });
    }



    // ============================================================
    // GET TRIAL BALANCE DETAILS
    //
    // GET:
    // /api/TrialBalances/{id}/details
    //
    // Used ONLY by the Trial Balance Details page.
    // Does NOT modify the existing GetById API.
    // ============================================================

    [HttpGet("{RefNo}/details")]
    public async Task<IActionResult> GetTrialBalanceDetails(string RefNo)
    {
        // ============================================================
        // 1. GET TRIAL BALANCE
        // ============================================================

        var trialBalance =
            await _trialBalances
                .Find(x => x.RefNo == RefNo)
                .FirstOrDefaultAsync();

        if (trialBalance == null)
        {
            return NotFound(new
            {
                status = false,
                message = "Trial balance not found."
            });
        }


        // ============================================================
        // 2. GET ACCOUNTING PERIOD
        // ============================================================

        object? period = null;

        if (!string.IsNullOrWhiteSpace(trialBalance.PeriodId))
        {
            period =
                await _accountingPeriods
                    .Find(x => x.Id == trialBalance.PeriodId)
                    .FirstOrDefaultAsync();
        }

        // Management TB may not have PeriodId
        if (period == null)
        {
            period = new
            {
                id = trialBalance.PeriodId ?? string.Empty,

                periodStart =
                    trialBalance.PeriodStart,

                periodEnd =
                    trialBalance.PeriodEnd,

                isActive = true,

                isClosed = false
            };
        }


        // ============================================================
        // 3. GET IMPORTS
        //
        // TrialBalance.ImportIds contains the Import IDs
        // ============================================================

        var importIds =
            trialBalance.ImportIds ??
            new List<string>();

        var imports =
            importIds.Count == 0
                ? new List<Import>()
                : await _imports
                    .Find(x =>
                        importIds.Contains(x.Id))
                    .SortBy(x => x.CreatedAt)
                    .ToListAsync();


        // ============================================================
        // 4. BUILD IMPORT RESPONSE
        // ============================================================

        var importResponse =
            imports
                .Select(import => new
                {
                    id =
                        import.Id,

                    refNo =
                        import.Number,

                    description =
                        import.Description ??
                        trialBalance.Description ??
                        string.Empty,

                    fileName =
                        import.FileName ??
                        string.Empty,

                    status =
                        import.Status.ToString(),

                    importedOn =
                        import.CreatedAt,

                    action =
                        new
                        {
                            id = import.Id,

                            edit = true,

                            delete = true
                        }
                })
                .ToList();


        // ============================================================
        // 5. GET JOURNALS
        //
        // TrialBalance.JournalIds contains Journal IDs
        // ============================================================

        var journalIds =
            trialBalance.JournalIds ??
            new List<string>();

        var journals =
            journalIds.Count == 0
                ? new List<Journal>()
                : await _journals
                    .Find(x =>
                        journalIds.Contains(x.Id))
                    .SortBy(x => x.CreatedAt)
                    .ToListAsync();


        // ============================================================
        // 6. BUILD JOURNAL RESPONSE
        // ============================================================

        var journalResponse =
            journals
                .Select(journal => new
                {
                    id =
                        journal.Id,

                    number =
                        journal.Number,

                    imports = journal.Imports,

                    //  new ImportsReference {
                    // Id = journal.Id,
                    // Name = journal.name,
                    // },


                    TrialBalance = new TrialBalanceReference
                    {
                        Id = trialBalance.Id,
                        Name = trialBalance.RefNo,
                    },

                    description =
                        journal.Description ??
                        string.Empty,

                    itemsCount =
                        journal.ItemsCount > 0
                            ? journal.ItemsCount
                            : journal.Items?.Count ?? 0,

                    journalType =
                        journal.JournalType.ToString(),

                    journalStatus =
                        journal.JournalStatus.ToString(),

                    importType =
                        journal.ImportType == ImportType.Csv
                            ? "Default"
                            : "Manual",

                    status =
                        journal.IsActive,

                    createdOn =
                        journal.CreatedAt,

                    totalDebit =
                        journal.TotalDebit,

                    totalCredit =
                        journal.TotalCredit,

                    action =
                        new
                        {
                            id =
                                journal.Id,

                            edit =
                                journal.JournalStatus !=
                                JournalStatus.Posted,

                            delete =
                                journal.JournalStatus !=
                                JournalStatus.Posted,

                            unpost =
                                journal.JournalStatus ==
                                JournalStatus.Posted,

                            download = true,

                            addAttachment = true
                        }
                })
                .ToList();


        // ============================================================
        // 7. CALCULATE TOTALS
        //
        // Journals are the source of truth.
        // ============================================================

        var totalDebit =
            journals.Sum(x => x.TotalDebit);

        var totalCredit =
            journals.Sum(x => x.TotalCredit);


        // ============================================================
        // 8. BALANCE STATUS
        // ============================================================

        // var balanceStatus =
        //     totalDebit == totalCredit
        //         ? "Balance"
        //         : "Unbalanced";


        string balanceStatus;

        if (totalDebit == totalCredit)
        {
            balanceStatus = "Balance";
        }
        else if (totalDebit > totalCredit)
        {
            var debitDifference = totalDebit - totalCredit;

            balanceStatus = $"Debit €{debitDifference:N2}";
        }
        else
        {
            var creditDifference = totalCredit - totalDebit;

            balanceStatus = $"Credit €{creditDifference:N2}";
        }
        // ============================================================
        // 9. PROFIT / LOSS
        // ============================================================

        var profitLoss =
            trialBalance.TotalProfitLoss;


        // ============================================================
        // 10. FINAL RESPONSE
        // ============================================================

        return Ok(new
        {
            status = true,

            result = new
            {
                // ====================================================
                // TRIAL BALANCE
                // ====================================================

                trialBalance = new
                {
                    id =
                        trialBalance.Id,

                    refNo =
                        trialBalance.RefNo,


                    period,

                    description =
                        trialBalance.Description,

                    periodStart =
                        trialBalance.PeriodStart,

                    periodEnd =
                        trialBalance.PeriodEnd,

                    type =
                        trialBalance.Type.ToString()
                },


                // ====================================================
                // IMPORTS
                // ====================================================

                imports =
                    importResponse,


                // ====================================================
                // JOURNALS
                // ====================================================

                journals =
                    journalResponse,


                // ====================================================
                // SUMMARY
                // ====================================================

                summary = new
                {
                    totalDebit =
                        totalDebit,

                    totalCredit =
                        totalCredit,

                    // difference =
                    //     totalDebit -
                    //     totalCredit,

                    difference =
        Math.Abs(totalDebit - totalCredit),

                    status =
                        balanceStatus,

                    profitLoss =
                        profitLoss
                }
            }
        });
    }


    // ============================================================
    // GET POSTED TRIAL BALANCE DATA
    //
    // GET
    // /api/TrialBalances/{tbId}/posted-data
    //
    // Only POSTED journals are included.
    // Multiple journals belonging to the same TB are combined.
    // Entries are aggregated by Chart Account Code.
    // ============================================================

    [HttpGet("{tbId}/posted-data")]
    public async Task<IActionResult> GetPostedTrialBalanceData(
        string tbId)
    {
        // ============================================================
        // 1. FIND TRIAL BALANCE
        // ============================================================

        var trialBalance =
            await _trialBalances
                .Find(x => x.Id == tbId)
                .FirstOrDefaultAsync();

        if (trialBalance == null)
        {
            return NotFound(new
            {
                status = false,
                message = "Trial balance not found."
            });
        }


        // ============================================================
        // 2. GET ONLY POSTED JOURNALS
        //
        // Important:
        // Drafted   -> excluded
        // Unposted  -> excluded
        // Posted    -> included
        // ============================================================

        var journals =
            await _journals
                .Find(x =>
                    x.TrialBalance.Id == tbId &&
                    x.JournalStatus == JournalStatus.Posted
                )
                .SortBy(x => x.CreatedAt)
                .ToListAsync();


        // ============================================================
        // 3. NO POSTED JOURNALS
        // ============================================================

        if (journals.Count == 0)
        {
            return Ok(new
            {
                executionTime = 0,

                result = new
                {
                    trialBalances = new[]
                    {
                    new
                    {
                        periodStart =
                            trialBalance.PeriodStart,

                        periodEnd =
                            trialBalance.PeriodEnd,

                        totalDebit = 0m,

                        totalCredit = 0m,

                        name =
                            trialBalance.RefNo,

                        id =
                            trialBalance.Id
                    }
                },

                    items = new List<object>()
                },

                status = true
            });
        }


        // ============================================================
        // 4. GET ALL JOURNAL ITEMS
        //
        // Multiple posted journals can belong to one TB.
        // Combine all their entries.
        // ============================================================

        var journalItems =
            journals
                .Where(x => x.Items != null)
                .SelectMany(x => x.Items)
                .ToList();


        // ============================================================
        // 5. GET UNIQUE ACCOUNT CODES
        // ============================================================

        var accountCodes =
            journalItems
                .Select(x => x.AccountCode)
                .Where(x =>
                    !string.IsNullOrWhiteSpace(x))
                .Distinct()
                .ToList();


        // ============================================================
        // 6. GET CHART ACCOUNTS
        // ============================================================

        var chartAccounts =
            accountCodes.Count == 0
                ? new List<ChartAccount>()
                : await _chartAccounts
                    .Find(x =>
                        accountCodes.Contains(x.Code))
                    .ToListAsync();


        // ============================================================
        // 7. BUILD ITEMS
        //
        // One row per Chart Account.
        // ============================================================

        var items =
            new List<object>();


        foreach (var code in accountCodes)
        {
            var account =
                chartAccounts
                    .FirstOrDefault(x =>
                        x.Code == code);


            // --------------------------------------------------------
            // All journal entries for this account
            // --------------------------------------------------------

            var accountEntries =
                journalItems
                    .Where(x =>
                        x.AccountCode == code)
                    .ToList();


            // --------------------------------------------------------
            // Debit / Credit totals for this account
            // --------------------------------------------------------

            var debit =
                accountEntries.Sum(x => x.Debit);

            var credit =
                accountEntries.Sum(x => x.Credit);


            // --------------------------------------------------------
            // Signed amount
            //
            // Debit  = positive
            // Credit = negative
            // --------------------------------------------------------

            var amount =
                debit - credit;


            // --------------------------------------------------------
            // Account Type / Group
            // --------------------------------------------------------

            object? accountType = null;

            Console.WriteLine("account", account);

            if (account != null &&
                !string.IsNullOrWhiteSpace(
                    account.AccountTypeId))
            {
                accountType =
                    await _accountTypes
                        .Find(x =>
                            x.Id ==
                            account.AccountTypeId)
                        .FirstOrDefaultAsync();
            }
            Console.WriteLine("accountType", accountType);




            items.Add(
                new
                {
                    groupName =
                        account?.AccountGroup ??
                        string.Empty,

                    group =
                        account?.AccountGroup ??
                        string.Empty,

                    // typeName =
                    //     accountType?.Name ??
                    //     string.Empty,

                    type =
                        account?.AccountTypeId ??
                        string.Empty,

                    code =
                        code,

                    // Signed amount
                    // Debit positive / Credit negative
                    amounts =
                        new[]
                        {
                        amount
                        },

                    accountNature =
                        amount >= 0
                            ? AccountNature.Debit
                            : AccountNature.Credit,

                    name =
                        account?.AccountName ??
                        accountEntries
                            .FirstOrDefault()
                            ?.AccountName ??
                        string.Empty,

                    id =
                        account?.Id ??
                        string.Empty,

                    debit =
                        debit,

                    credit =
                        credit,

                    totalDebit =
                        debit,

                    totalCredit =
                        credit
                }
            );
        }


        // ============================================================
        // 8. TRIAL BALANCE TOTALS
        // ============================================================

        var totalDebit =
            journalItems.Sum(x => x.Debit);

        var totalCredit =
            journalItems.Sum(x => x.Credit);


        // ============================================================
        // 9. RESPONSE
        // ============================================================

        return Ok(new
        {
            executionTime = 0,

            result = new
            {
                trialBalances =
                    new[]
                    {
                    new
                    {
                        periodStart =
                            trialBalance.PeriodStart,

                        periodEnd =
                            trialBalance.PeriodEnd,

                        totalDebit =
                            totalDebit,

                        totalCredit =
                            totalCredit,

                        name =
                            trialBalance.RefNo,

                        id =
                            trialBalance.Id
                    }
                    },

                items =
                    items
            },

            status = true
        });
    }

    // ============================================================
    // CREATE / UPDATE JOURNAL
    // ============================================================

    [HttpPost("{RefNo}/journals/{journalId}")]
    public async Task<IActionResult> CreateOrUpdateJournal(
        string RefNo,
        string journalId,
        [FromForm] string journal,
        IFormFile? attachment)
    {
        // ============================================================
        // FIND TRIAL BALANCE
        // ============================================================

        var trialBalance =
            await _trialBalances
                .Find(x => x.RefNo == RefNo)
                .FirstOrDefaultAsync();

        if (trialBalance == null)
        {
            return NotFound(new
            {
                status = false,
                message = "Trial balance not found."
            });
        }


        // ============================================================
        // DESERIALIZE JOURNAL
        // ============================================================

        Journal request;

        try
        {
            request =
                System.Text.Json.JsonSerializer.Deserialize<Journal>(
                    journal,
                    new System.Text.Json.JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true
                    }) ?? new Journal();
        }
        catch
        {
            return BadRequest(new
            {
                status = false,
                message = "Invalid journal data."
            });
        }


        // ============================================================
        // VALIDATE JOURNAL ITEMS
        // ============================================================

        if (request.Items == null ||
            request.Items.Count == 0)
        {
            return BadRequest(new
            {
                status = false,
                message = "At least one journal item is required."
            });
        }


        // ============================================================
        // CALCULATE TOTALS
        // ============================================================

        decimal totalDebit =
            request.Items.Sum(x => x.Debit);

        decimal totalCredit =
            request.Items.Sum(x => x.Credit);


        // ============================================================
        // CREATE NEW JOURNAL
        // journalId = 0
        // ============================================================

        if (journalId == "0")
        {
            // --------------------------------------------------------
            // Generate Journal Number
            // --------------------------------------------------------

            var journalNumber =
                await GenerateJournalNumber(
                        trialBalance.Id,
                    trialBalance.RefNo);


            // --------------------------------------------------------
            // Generate MongoDB ID
            // --------------------------------------------------------

            var newJournalId =
                ObjectId.GenerateNewId().ToString();


            // --------------------------------------------------------
            // Set Journal properties
            // --------------------------------------------------------

            request.Id =
                newJournalId;

            request.Number =
                journalNumber;


            request.TrialBalance =
                new TrialBalanceReference
                {
                    Id = trialBalance.Id,
                    Name = trialBalance.RefNo
                };

            request.PeriodStart =
                      trialBalance.PeriodStart;
            request.PeriodStart =
                trialBalance.PeriodStart;

            request.PeriodEnd =
                trialBalance.PeriodEnd;


            request.ItemsCount =
                request.Items.Count;

            request.TotalDebit =
                totalDebit;

            request.TotalCredit =
                totalCredit;

            request.CreatedAt =
                DateTime.UtcNow;

            request.Description = request.Description;
            // --------------------------------------------------------
            // Attachment
            // --------------------------------------------------------

            // if (attachment != null &&
            //     attachment.Length > 0)
            // {
            //     var uploadFolder =
            //         Path.Combine(
            //             _environment.WebRootPath ?? "wwwroot",
            //             "uploads",
            //             "journals");

            //     if (!Directory.Exists(uploadFolder))
            //     {
            //         Directory.CreateDirectory(uploadFolder);
            //     }


            //     // Keep original extension
            //     var extension =
            //         Path.GetExtension(
            //             attachment.FileName);


            //     // Safe backend file name
            //     var fileName =
            //         $"{request.Number}_{Guid.NewGuid():N}{extension}";


            //     var filePath =
            //         Path.Combine(
            //             uploadFolder,
            //             fileName);


            //     using (var stream =
            //            new FileStream(
            //                filePath,
            //                FileMode.Create))
            //     {
            //         await attachment.CopyToAsync(stream);
            //     }


            //     request.Attachment.Name =
            //         attachment.FileName;

            //     request.Attachment.Path =
            //         Path.Combine(
            //             "uploads",
            //             "journals",
            //             fileName)
            //         .Replace("\\", "/");
            // }


            if (attachment != null &&
                attachment.Length > 0)
            {
                var uploadFolder =
                    Path.Combine(
                        _environment.WebRootPath ?? "wwwroot",
                        "uploads",
                        "journals");

                if (!Directory.Exists(uploadFolder))
                {
                    Directory.CreateDirectory(uploadFolder);
                }

                // Keep original extension
                var extension =
                    Path.GetExtension(attachment.FileName);

                // Safe backend file name
                var fileName =
                    $"{request.Number}_{Guid.NewGuid():N}{extension}";

                var filePath =
                    Path.Combine(
                        uploadFolder,
                        fileName);

                // Save physical file
                using (var stream =
                       new FileStream(
                           filePath,
                           FileMode.Create))
                {
                    await attachment.CopyToAsync(stream);
                }

                // Create Attachment object
                // because request.Attachment is null
                var relativePath =
                    Path.Combine(
                        "uploads",
                        "journals",
                        fileName)
                    .Replace("\\", "/");

                request.Attachment = new Attachment
                {
                    Name = attachment.FileName,
                    Path = relativePath
                };
            }
            // --------------------------------------------------------
            // Insert Journal
            // --------------------------------------------------------

            await _journals.InsertOneAsync(request);


            // ========================================================
            // ADD JOURNAL ID TO TRIAL BALANCE
            // ========================================================

            trialBalance.JournalIds ??=
                new List<string>();


            if (!trialBalance.JournalIds.Contains(
                    request.Id))
            {
                trialBalance.JournalIds.Add(
                    request.Id);
            }

            // ========================================================
            // RECALCULATE TRIAL BALANCE TOTALS
            // ========================================================

            var allJournals =
                await _journals
                    .Find(x =>
                        x.TrialBalance.Id ==
                        trialBalance.Id)
                    .ToListAsync();


            trialBalance.TotalDebit =
                allJournals.Sum(
                    x => x.TotalDebit);

            trialBalance.TotalCredit =
                allJournals.Sum(
                    x => x.TotalCredit);


            trialBalance.Status =
                trialBalance.TotalDebit ==
                trialBalance.TotalCredit
                    ? TrialBalanceStatus.Balanced
                    : TrialBalanceStatus.Unbalanced;


            // ========================================================
            // SAVE TRIAL BALANCE
            // ========================================================

            await _trialBalances.UpdateOneAsync(
                x => x.Id == trialBalance.Id,
                Builders<TrialBalance>.Update
                    .Set(
                        x => x.JournalIds,
                        trialBalance.JournalIds)
                    .Set(
                        x => x.TotalDebit,
                        trialBalance.TotalDebit)
                    .Set(
                        x => x.TotalCredit,
                        trialBalance.TotalCredit)
                    .Set(
                        x => x.Status,
                        trialBalance.Status));


            // ========================================================
            // RESPONSE
            // ========================================================

            return Ok(new
            {
                status = true,
                message = "Journal created successfully.",
                result = request
            });
        }


        // ============================================================
        // UPDATE EXISTING JOURNAL
        // journalId = MongoDB ObjectId
        // ============================================================

        if (!ObjectId.TryParse(
                journalId,
                out _))
        {
            return BadRequest(new
            {
                status = false,
                message =
                    "Invalid journal ID. Use 0 to create a new journal or a valid MongoDB ObjectId to update."
            });
        }


        // ============================================================
        // FIND EXISTING JOURNAL
        // ============================================================

        var existingJournal =
            await _journals
                .Find(x =>
                    x.Id == journalId &&
                    x.TrialBalance.Id ==
                    trialBalance.Id)
                .FirstOrDefaultAsync();


        if (existingJournal == null)
        {
            return NotFound(new
            {
                status = false,
                message =
                    "Journal not found for this trial balance."
            });
        }


        // ============================================================
        // UPDATE ONLY THE JOURNAL FIELDS
        // ============================================================

        var update =
            Builders<Journal>.Update

                // ----------------------------------------------------
                // Journal data
                // ----------------------------------------------------

                .Set(
                    x => x.Description,
                    request.Description)

                .Set(
                    x => x.JournalType,
                    request.JournalType)

                .Set(
                    x => x.Type,
                    request.Type)

                .Set(
                    x => x.JournalStatus,
                    request.JournalStatus)

                .Set(
                    x => x.ImportType,
                    request.ImportType)

                .Set(
                    x => x.CsvImportType,
                    request.CsvImportType)

                .Set(
                    x => x.IsActive,
                    request.IsActive)

                // ----------------------------------------------------
                // Journal Items
                // ----------------------------------------------------

                .Set(
                    x => x.Items,
                    request.Items)

                .Set(
                    x => x.ItemsCount,
                    request.Items.Count)

                .Set(
                    x => x.TotalDebit,
                    totalDebit)

                .Set(
                    x => x.TotalCredit,
                    totalCredit);


        // ============================================================
        // ATTACHMENT
        // ============================================================

        // IMPORTANT:
        // If no new attachment is supplied,
        // existing attachment remains untouched.

        if (attachment != null &&
            attachment.Length > 0)
        {
            var uploadFolder =
                Path.Combine(
                    _environment.WebRootPath ?? "wwwroot",
                    "uploads",
                    "journals");

            if (!Directory.Exists(uploadFolder))
            {
                Directory.CreateDirectory(uploadFolder);
            }


            // --------------------------------------------------------
            // File extension
            // --------------------------------------------------------

            var extension =
                Path.GetExtension(
                    attachment.FileName);


            // --------------------------------------------------------
            // New backend file name
            // --------------------------------------------------------

            var fileName =
                $"{existingJournal.Number}_{Guid.NewGuid():N}{extension}";


            var filePath =
                Path.Combine(
                    uploadFolder,
                    fileName);


            // --------------------------------------------------------
            // Save file
            // --------------------------------------------------------

            using (var stream =
                   new FileStream(
                       filePath,
                       FileMode.Create))
            {
                await attachment.CopyToAsync(stream);
            }


            // --------------------------------------------------------
            // Update attachment information
            // --------------------------------------------------------

            var relativePath =
                Path.Combine(
                    "uploads",
                    "journals",
                    fileName)
                .Replace("\\", "/");

            var newAttachment = new Attachment
            {
                Name = attachment.FileName,
                Path = relativePath
            };

            update = update.Set(
                x => x.Attachment,
                newAttachment
            );
            // update =
            //     update
            //         .Set(
            //             x => x.Attachment.Name,
            //             attachment.FileName)
            //         .Set(
            //             x => x.Attachment.Path,
            //             relativePath);
        }


        // ============================================================
        // UPDATE JOURNAL DOCUMENT
        // ============================================================

        await _journals.UpdateOneAsync(
            x => x.Id == existingJournal.Id,
            update);


        // ============================================================
        // RECALCULATE TRIAL BALANCE TOTALS
        // ============================================================

        var allUpdatedJournals =
            await _journals
                .Find(x =>
                    x.TrialBalance.Id ==
                    trialBalance.Id)
                .ToListAsync();


        trialBalance.TotalDebit =
            allUpdatedJournals.Sum(
                x => x.TotalDebit);

        trialBalance.TotalCredit =
            allUpdatedJournals.Sum(
                x => x.TotalCredit);


        trialBalance.Status =
            trialBalance.TotalDebit ==
            trialBalance.TotalCredit
                ? TrialBalanceStatus.Balanced
                : TrialBalanceStatus.Unbalanced;


        // ============================================================
        // SAVE TRIAL BALANCE TOTALS
        // ============================================================

        await _trialBalances.UpdateOneAsync(
            x => x.Id == trialBalance.Id,
            Builders<TrialBalance>.Update
                .Set(
                    x => x.TotalDebit,
                    trialBalance.TotalDebit)
                .Set(
                    x => x.TotalCredit,
                    trialBalance.TotalCredit)
                .Set(
                    x => x.Status,
                    trialBalance.Status));


        // ============================================================
        // GET UPDATED JOURNAL
        // ============================================================

        var updatedJournal =
            await _journals
                .Find(x =>
                    x.Id == existingJournal.Id)
                .FirstOrDefaultAsync();


        // ============================================================
        // RESPONSE
        // ============================================================

        return Ok(new
        {
            status = true,
            message = "Journal updated successfully.",
            result = updatedJournal
        });
    }


    // ============================================================
    // DOWNLOAD POSTED TRIAL BALANCE AS PDF
    //
    // POST
    // /api/TrialBalances/{tbId}/posted-data/pdf
    //
    // PDF is generated in memory.
    // Nothing is saved on the server.
    // ============================================================

    [HttpPost("{tbId}/posted-data/pdf")]
    public async Task<IActionResult> DownloadPostedTrialBalancePdf(
        string tbId)
    {
        // ============================================================
        // 1. FIND TRIAL BALANCE
        // ============================================================

        var trialBalance =
            await _trialBalances
                .Find(x => x.Id == tbId)
                .FirstOrDefaultAsync();

        if (trialBalance == null)
        {
            return NotFound(new
            {
                status = false,
                message = "Trial balance not found."
            });
        }


        // ============================================================
        // 2. GET ONLY POSTED JOURNALS
        // ============================================================

        var journals =
            await _journals
                .Find(x =>
                    x.TrialBalance.Id == tbId &&
                    x.JournalStatus == JournalStatus.Posted)
                .SortBy(x => x.CreatedAt)
                .ToListAsync();


        // ============================================================
        // 3. COMBINE ALL JOURNAL ITEMS
        // ============================================================

        var journalItems =
            journals
                .Where(x => x.Items != null)
                .SelectMany(x => x.Items)
                .ToList();


        // ============================================================
        // 4. GET UNIQUE ACCOUNT CODES
        // ============================================================

        var accountCodes =
            journalItems
                .Select(x => x.AccountCode)
                .Where(x =>
                    !string.IsNullOrWhiteSpace(x))
                .Distinct()
                .ToList();


        // ============================================================
        // 5. GET CHART ACCOUNTS
        // ============================================================

        var chartAccounts =
            accountCodes.Count == 0
                ? new List<ChartAccount>()
                : await _chartAccounts
                    .Find(x =>
                        accountCodes.Contains(x.Code))
                    .ToListAsync();


        // ============================================================
        // 6. GENERATE PDF DIRECTLY
        // ============================================================

        byte[] pdfBytes =
            Document.Create(document =>
            {
                document.Page(page =>
                {
                    // ====================================================
                    // PAGE SETTINGS
                    // ====================================================

                    page.Size(PageSizes.A4);

                    page.MarginLeft(40);
                    page.MarginRight(40);
                    page.MarginTop(35);
                    page.MarginBottom(35);
                    // page.Margin(
                    //     left: 40,
                    //     right: 40,
                    //     top: 35,
                    //     bottom: 35
                    // );

                    page.DefaultTextStyle(
                        x => x.FontSize(9)
                    );


                    // ====================================================
                    // HEADER
                    // ====================================================

                    page.Header()
                        .Column(column =>
                        {
                            // ------------------------------------------------
                            // CLIENT NAME
                            // ------------------------------------------------

                            column.Item()
                                .Text("client_name")
                                .FontSize(18)
                                .Bold();


                            // ------------------------------------------------
                            // REPORT NAME
                            // ------------------------------------------------

                            column.Item()
                                .PaddingTop(4)
                                .Text("Trial Balance Report")
                                .FontSize(13)
                                .Bold();


                            // ------------------------------------------------
                            // SPACE
                            // ------------------------------------------------

                            column.Item()
                                .PaddingTop(8);


                            // ------------------------------------------------
                            // HEADER LINE
                            // ------------------------------------------------

                            column.Item()
                                .LineHorizontal(1);
                        });


                    // ====================================================
                    // CONTENT
                    // ====================================================

                    page.Content()
                        .PaddingTop(5)
                        .Table(table =>
                        {
                            // =================================================
                            // COLUMNS
                            // =================================================

                            table.ColumnsDefinition(columns =>
                            {
                                // Code
                                columns.ConstantColumn(55);

                                // Account Name
                                columns.RelativeColumn(4);

                                // Debit
                                columns.RelativeColumn(2);

                                // Credit
                                columns.RelativeColumn(2);
                            });


                            // =================================================
                            // TABLE HEADER
                            // =================================================

                            table.Header(header =>
                            {
                                // ------------------------------------------------
                                // CODE
                                // ------------------------------------------------

                                header.Cell()
                                    .RowSpan(2)
                                    .Element(HeaderCell)
                                    .Text("Code");


                                // ------------------------------------------------
                                // ACCOUNT NAME
                                // ------------------------------------------------

                                header.Cell()
                                    .RowSpan(2)
                                    .Element(HeaderCell)
                                    .Text("Account Name");


                                // ------------------------------------------------
                                // PERIOD
                                // ------------------------------------------------

                                header.Cell()
                                    .ColumnSpan(2)
                                    .Element(HeaderCell)
                                    .AlignCenter()
                                    .Text(
                                        $"{trialBalance.PeriodStart:dd/MM/yyyy} - " +
                                        $"{trialBalance.PeriodEnd:dd/MM/yyyy}"
                                    );


                                // ------------------------------------------------
                                // DEBIT
                                // ------------------------------------------------

                                header.Cell()
                                    .Element(HeaderCell)
                                    .AlignRight()
                                    .Text("Debit");


                                // ------------------------------------------------
                                // CREDIT
                                // ------------------------------------------------

                                header.Cell()
                                    .Element(HeaderCell)
                                    .AlignRight()
                                    .Text("Credit");
                            });


                            // =================================================
                            // TOTALS
                            // =================================================

                            decimal totalDebit = 0;

                            decimal totalCredit = 0;


                            // =================================================
                            // DATA ROWS
                            // =================================================

                            foreach (var code in accountCodes)
                            {
                                var account =
                                    chartAccounts
                                        .FirstOrDefault(
                                            x => x.Code == code
                                        );


                                // ------------------------------------------------
                                // All entries for this account
                                // ------------------------------------------------

                                var entries =
                                    journalItems
                                        .Where(x =>
                                            x.AccountCode == code)
                                        .ToList();


                                // ------------------------------------------------
                                // Debit
                                // ------------------------------------------------

                                var debit =
                                    entries.Sum(
                                        x => x.Debit
                                    );


                                // ------------------------------------------------
                                // Credit
                                // ------------------------------------------------

                                var credit =
                                    entries.Sum(
                                        x => x.Credit
                                    );


                                totalDebit += debit;

                                totalCredit += credit;


                                // =================================================
                                // CODE
                                // =================================================

                                table.Cell()
                                    .Element(DataCell)
                                    .Text(code);


                                // =================================================
                                // ACCOUNT NAME
                                // =================================================

                                table.Cell()
                                    .Element(DataCell)
                                    .Text(
                                        account?.AccountName ??
                                        entries
                                            .FirstOrDefault()
                                            ?.AccountName ??
                                        string.Empty
                                    );


                                // =================================================
                                // DEBIT
                                // =================================================

                                table.Cell()
                                    .Element(DataCell)
                                    .AlignRight()
                                    .Text(
                                        debit == 0
                                            ? "-"
                                            : $"£{debit:N2}"
                                    );


                                // =================================================
                                // CREDIT
                                // =================================================

                                table.Cell()
                                    .Element(DataCell)
                                    .AlignRight()
                                    .Text(
                                        credit == 0
                                            ? "-"
                                            : $"£{credit:N2}"
                                    );
                            }


                            // =================================================
                            // TOTAL ROW
                            // =================================================

                            table.Cell()
                                .ColumnSpan(2)
                                .Element(TotalCell)
                                .Text("Total");


                            table.Cell()
                                .Element(TotalCell)
                                .AlignRight()
                                .Text(
                                    totalDebit == 0
                                        ? "-"
                                        : $"£{totalDebit:N2}"
                                );


                            table.Cell()
                                .Element(TotalCell)
                                .AlignRight()
                                .Text(
                                    totalCredit == 0
                                        ? "-"
                                        : $"£{totalCredit:N2}"
                                );
                        });


                    // ====================================================
                    // FOOTER
                    // ====================================================

                    page.Footer()
                        .AlignCenter()
                        .Text(
                            $"Generated on " +
                            $"{DateTime.Now:dd/MM/yyyy HH:mm}"
                        );
                });
            })
            .GeneratePdf();


        // ============================================================
        // FILE NAME
        // ============================================================

        var year =
            trialBalance.PeriodEnd.Year;


        var fileName =
            $"client_name_trialbalance_{year}.pdf";


        // ============================================================
        // RETURN PDF TO FRONTEND
        // ============================================================

        return File(
            pdfBytes,
            "application/pdf",
            fileName
        );


        // ============================================================
        // LOCAL PDF CELL STYLES
        // ============================================================

        static IContainer HeaderCell(
            IContainer container)
        {
            return container
                .Background("#EEEEEE")
                .Padding(5)
                .BorderBottom(1);
        }


        static IContainer DataCell(
            IContainer container)
        {
            return container
                .PaddingVertical(4)
                .PaddingHorizontal(5)
                .BorderBottom(0.5f);
        }


        static IContainer TotalCell(
            IContainer container)
        {
            return container
                .PaddingVertical(5)
                .PaddingHorizontal(5)
                .BorderTop(1);
        }
    }


    [HttpGet("{RefNo}/journals/{journalId}")]
    public async Task<IActionResult> GetById(
        string RefNo,
        string journalId)
    {
        // ============================================================
        // FIND TRIAL BALANCE
        // ============================================================

        var tb =
            await _trialBalances
                .Find(x => x.RefNo == RefNo)
                .FirstOrDefaultAsync();

        if (tb == null)
        {
            return NotFound(new
            {
                status = false,
                message = "Trial balance not found."
            });
        }


        // ============================================================
        // ACCOUNTING PERIOD
        // ============================================================

        object? period = null;

        if (!string.IsNullOrWhiteSpace(tb.PeriodId))
        {
            period =
                await _accountingPeriods
                    .Find(x => x.Id == tb.PeriodId)
                    .FirstOrDefaultAsync();
        }

        // If AccountingPeriod document is not found,
        // use the dates stored in TrialBalance.
        if (period == null &&
            (tb.PeriodStart != default ||
             tb.PeriodEnd != default))
        {
            period = new
            {
                id = string.Empty,

                periodFrom =
                    tb.PeriodStart,

                periodTo =
                    tb.PeriodEnd,

                periodStart =
                    tb.PeriodStart,

                periodEnd =
                    tb.PeriodEnd,

                isActive = true,

                isClosed = false
            };
        }


        Journal? journal = null;


        if (journalId != "0")
        {
            // --------------------------------------------------------
            // VALIDATE JOURNAL ID
            // --------------------------------------------------------

            if (!ObjectId.TryParse(journalId, out _))
            {
                return BadRequest(new
                {
                    status = false,
                    message = "Invalid journal ID."
                });
            }


            // --------------------------------------------------------
            // FIND JOURNAL
            //
            // Also verify that this Journal belongs to the
            // Trial Balance requested in the URL.
            // --------------------------------------------------------

            journal =
                await _journals
                    .Find(x =>
                        x.Id == journalId &&
                        x.TrialBalance != null &&
                        x.TrialBalance.Id == tb.Id)
                    .FirstOrDefaultAsync();


            // --------------------------------------------------------
            // JOURNAL NOT FOUND
            // --------------------------------------------------------

            if (journal == null)
            {
                return NotFound(new
                {
                    status = false,
                    message =
                        "Journal not found for this trial balance."
                });
            }
        }


        // ============================================================
        // CHECK WHETHER JOURNAL EXISTS
        // ============================================================

        var hasJournal =
            journal != null;


        // ============================================================
        // JOURNAL IDS
        //
        // Since journalId comes from the path, we do not read
        // TrialBalance.JournalIds.
        // ============================================================

        var journalIds =
            hasJournal
                ? new List<string>
                {
                journal!.Id
                }
                : new List<string>();


        // ============================================================
        // ITEMS
        //
        // TrialBalance does not contain Items.
        // Items come from Journal.Items.
        // ============================================================

        var items =
            new List<object>();


        if (hasJournal &&
            journal!.Items != null &&
            journal.Items.Count > 0)
        {
            foreach (var journalItem in journal.Items)
            {
                // ====================================================
                // FIND CHART ACCOUNT
                // ====================================================

                var account =
                    await _chartAccounts
                        .Find(x =>
                            x.Code ==
                            journalItem.AccountCode)
                        .FirstOrDefaultAsync();


                // ====================================================
                // ACCOUNT INFORMATION
                // ====================================================

                var accountData =
                    account == null
                        ? new
                        {
                            id = string.Empty,

                            name =
                                journalItem.AccountName,

                            code =
                                journalItem.AccountCode
                        }
                        : new
                        {
                            id =
                                account.Id,

                            name =
                                account.AccountName,

                            code =
                                account.Code
                        };


                // ====================================================
                // ADD JOURNAL ITEM
                // ====================================================

                items.Add(
                    new
                    {
                        journalId =
                            journal.Id,

                        journalNumber =
                            journal.Number,

                        account =
                            accountData,

                        note =
                            journalItem.Note,

                        amount =
                            journalItem.Credit > 0
                                ? -journalItem.Credit
                                : journalItem.Debit,

                        debit =
                            journalItem.Debit,

                        credit =
                            journalItem.Credit,

                        type =
                            journalItem.Nature
                    });
            }
        }


        // ============================================================
        // TOTALS
        //
        // If Journal exists:
        //     take totals from Journal.
        //
        // If journalId == "0":
        //     totals are 0.
        // ============================================================

        decimal totalDebit =
            hasJournal
                ? journal!.TotalDebit
                : 0;

        decimal totalCredit =
            hasJournal
                ? journal!.TotalCredit
                : 0;


        // ============================================================
        // STATUS
        //
        // If Journal exists:
        //     calculate status from Journal totals.
        //
        // If journalId == "0":
        //     preserve TrialBalance status.
        // ============================================================

        var status =
            hasJournal
                ? (
                    totalDebit == totalCredit
                        ? TrialBalanceStatus.Balanced
                        : TrialBalanceStatus.Unbalanced
                  )
                : tb.Status;


        // ============================================================
        // JOURNAL RESPONSE
        // ============================================================

        var journalResponse =
            hasJournal
                ? new List<object>
                {
                new
                {
                    id =
                        journal!.Id,

                    number =
                        journal.Number,

                    // --------------------------------------------
                    // TRIAL BALANCE REFERENCE
                    // --------------------------------------------

                    trialBalance =
                        new
                        {
                            id =
                                journal.TrialBalance.Id,

                            name =
                                journal.TrialBalance.Name
                        },

                    // --------------------------------------------
                    // JOURNAL INFORMATION
                    // --------------------------------------------

                    type =
                        journal.Type,

                    status =
                        journal.Status,
description =
                        journal.Description,
                    journalStatus =
                        journal.JournalStatus,

                    itemsCount =
                        journal.Items?.Count ?? 0,

                    totalDebit =
                        journal.TotalDebit,

                    totalCredit =
                        journal.TotalCredit,

                    importType =
                        journal.ImportType,

                    csvImportType =
                        journal.CsvImportType,



                    isActive =
                        journal.IsActive,

                    periodStart =
                        journal.PeriodStart,

                    periodEnd =
                        journal.PeriodEnd,

                    createdAt =
                        journal.CreatedAt,

                    createdBy =
                        journal.CreatedBy,


                }
                }
                : new List<object>();


        // ============================================================
        // RESPONSE
        // ============================================================

        return Ok(
            new
            {
                result =
                    new
                    {
                        // ============================================
                        // TRIAL BALANCE
                        // ============================================

                        trialBalance =
                            new
                            {
                                name =
                                    tb.RefNo,

                                id =
                                    tb.Id
                            },


                        // ============================================
                        // ACCOUNTING PERIOD
                        // ============================================

                        period,

                        periodStart =
                            tb.PeriodStart,

                        periodEnd =
                            tb.PeriodEnd,


                        // ============================================
                        // TRIAL BALANCE DETAILS
                        // ============================================

                        description =
                            tb.Description,

                        type =
                            tb.Type,

                        status,

                        importType =
                            tb.ImportType,

                        csvImportType =
                            tb.CsvImportType,


                        // ============================================
                        // JOURNAL ITEMS
                        // ============================================

                        items,

                        itemsCount =
                            items.Count,


                        // ============================================
                        // TOTALS
                        // ============================================

                        totalDebit,

                        totalCredit,

                        totalProfitLoss =
                            tb.TotalProfitLoss,

                        turnover =
                            tb.Turnover,


                        // ============================================
                        // VALIDATION
                        // ============================================

                        validation =
                            new { },

                        // ============================================
                        // JOURNAL
                        // ============================================

                        journalId =
                            hasJournal
                                ? journal!.Id
                                : "0",

                        journalIds,

                        journals =
                            journalResponse,


                        // ============================================
                        // IMPORTS
                        // ============================================

                        importIds =
                            tb.ImportIds ??
                            new List<string>(),


                        // ============================================
                        // TRIAL BALANCE ATTACHMENT
                        // ============================================
                        attachments =
    journal?.Attachment == null
        ? null
        : new
        {
            name = journal.Attachment.Name,
            path = journal.Attachment.Path
        },

                        // attachments =
                        //     string.IsNullOrWhiteSpace(
                        //         tb.AttachmentFilePath)
                        //             ? new List<object>()
                        //             : new List<object>
                        //             {
                        //             new
                        //             {
                        //                 name =
                        //                     Path.GetFileName(
                        //                         tb.AttachmentFilePath),

                        //                 path =
                        //                     tb.AttachmentFilePath
                        //             }
                        //             },


                        // ============================================
                        // CSV
                        // ============================================

                        csvFilePath =
                            tb.CsvFilePath,


                        // ============================================
                        // TRIAL BALANCE ID
                        // ============================================

                        id =
                            tb.Id
                    },

                status = true
            });
    }



    [HttpPut("{RefNo}/accounting-period")]
    public async Task<IActionResult> ChangeAccountingPeriod(
        string RefNo,
        [FromBody] TrialBalance request)
    {
        // ============================================================
        // FIND TRIAL BALANCE
        // ============================================================

        var trialBalance =
            await _trialBalances
                .Find(x => x.RefNo == RefNo)
                .FirstOrDefaultAsync();

        if (trialBalance == null)
        {
            return NotFound(new
            {
                status = false,
                message = "Trial balance not found."
            });
        }


        // ============================================================
        // VALIDATE PERIOD DATES
        // ============================================================

        if (request.PeriodStart == default ||
            request.PeriodEnd == default)
        {
            return BadRequest(new
            {
                status = false,
                message = "Period start and period end are required."
            });
        }


        if (request.PeriodStart > request.PeriodEnd)
        {
            return BadRequest(new
            {
                status = false,
                message = "Period start date cannot be greater than period end date."
            });
        }


        // ============================================================
        // ACCOUNTING PERIOD
        //
        // If PeriodId is provided:
        //     Find the AccountingPeriod.
        //
        // If PeriodId is not provided:
        //     Update only PeriodStart / PeriodEnd.
        // ============================================================

        AccountingPeriod? accountingPeriod = null;

        if (!string.IsNullOrWhiteSpace(request.PeriodId))
        {
            accountingPeriod =
                await _accountingPeriods
                    .Find(x => x.Id == request.PeriodId)
                    .FirstOrDefaultAsync();

            if (accountingPeriod == null)
            {
                return NotFound(new
                {
                    status = false,
                    message = "Accounting period not found."
                });
            }
        }


        // ============================================================
        // UPDATE TRIAL BALANCE
        // ============================================================

        var update =
            Builders<TrialBalance>.Update
                .Set(
                    x => x.PeriodStart,
                    request.PeriodStart)
                .Set(
                    x => x.PeriodEnd,
                    request.PeriodEnd);


        // ============================================================
        // UPDATE PERIOD ID ONLY IF PROVIDED
        // ============================================================

        if (!string.IsNullOrWhiteSpace(request.PeriodId))
        {
            update =
                update.Set(
                    x => x.PeriodId,
                    request.PeriodId);
        }


        // ============================================================
        // SAVE
        // ============================================================

        await _trialBalances.UpdateOneAsync(
            x => x.Id == trialBalance.Id,
            update);


        // ============================================================
        // RETURN UPDATED DATA
        // ============================================================

        return Ok(new
        {
            status = true,

            message = "Accounting period updated successfully.",

            result = new
            {
                trialBalanceId =
                    trialBalance.Id,

                refNo =
                    trialBalance.RefNo,

                periodId =
                    string.IsNullOrWhiteSpace(request.PeriodId)
                        ? trialBalance.PeriodId
                        : request.PeriodId,

                periodStart =
                    request.PeriodStart,

                periodEnd =
                    request.PeriodEnd
            }
        });
    }

}

