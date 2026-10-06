using BackendAcctTask.Models;
using Microsoft.AspNetCore.Mvc;
using MongoDB.Bson;
using MongoDB.Driver;
using System.Globalization;
using System.Text;

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

            ImportId =
                import.Id,

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

                importId =
                    journal.ImportId,

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

    [HttpGet("imports/{importId}")]
    public async Task<IActionResult> GetImport(
        string importId)
    {
        var import =
            await _imports
                .Find(x =>
                    x.Id == importId
                )
                .FirstOrDefaultAsync();


        if (import == null)
        {
            return NotFound(new
            {
                status = false,
                message =
                    "Import not found."
            });
        }


        var trialBalance =
            await _trialBalances
                .Find(x =>
                    x.Id ==
                    import.TrialBalance.Id
                )
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


        var items =
            new List<object>();


        foreach (var row in import.Rows)
        {
            var account =
                await _chartAccounts
                    .Find(x =>
                        x.Code == row.Code
                    )
                    .FirstOrDefaultAsync();


            items.Add(
                new
                {
                    account =
                        new
                        {
                            id =
                                account?.Id ??
                                string.Empty,

                            name =
                                account?.AccountName ??
                                row.Name,

                            code =
                                row.Code
                        },

                    note =
                        row.Note,

                    amount =
                        row.Credit > 0
                            ? -row.Credit
                            : row.Debit,

                    debit =
                        row.Debit,

                    credit =
                        row.Credit
                }
            );
        }


        return Ok(new
        {
            result =
                new
                {
                    import =
                        new
                        {
                            name =
                                import.Number,

                            id =
                                import.Id
                        },

                    trialBalance =
                        new
                        {
                            name =
                                trialBalance.RefNo,

                            id =
                                trialBalance.Id
                        },

                    number =
                        import.Number,

                    items,

                    type =
                        trialBalance.Type,

                    status =
                        trialBalance.Status,

                    itemsCount =
                        import.Rows.Count,

                    totalDebit =
                        import.Rows.Sum(
                            x => x.Debit
                        ),

                    totalCredit =
                        import.Rows.Sum(
                            x => x.Credit
                        ),

                    validation =
                        new { },

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
}