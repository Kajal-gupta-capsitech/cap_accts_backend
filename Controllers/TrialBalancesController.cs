using BackendAcctTask.Models;
using BackendAcctTask.Utils;
using BackendAcctTask.Data.MongoDB;
using Microsoft.AspNetCore.Mvc;
using MongoDB.Bson;
using MongoDB.Driver;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace BackendAcctTask.Controllers;

[ApiController]
[Route("api/TrialBalances")]
// public class TrialBalancesController : ControllerBase
public class TrialBalancesController : ApiControllerBase
{
    private readonly IWebHostEnvironment _environment;

    public TrialBalancesController(DBConfiguration dBconfiguration, IWebHostEnvironment environment) : base(dBconfiguration)
    {
        _environment = environment;
    }

    private TrialBalanceDB GetDB() => new TrialBalanceDB(_dbConfig, _environment);
    private AccountingPeriodDB GetAcctPeriodDB() => new AccountingPeriodDB(_dbConfig);
    private ImportDB GetImportDB() => new ImportDB(_dbConfig);
    private ChartAccountDB GetChartAccountDB() => new ChartAccountDB(_dbConfig);
    // private AccountTypeDB GetAccountTypeDB() => new AccountTypeDB(_dbConfig);
    private JournalDB GetJournalDB() => new JournalDB(_dbConfig, _environment);

    #region trial balance create
    [HttpPost]
    [Consumes("multipart/form-data")]
    public async Task<ActionResult<ApiResponse<TrialBalance>>> Create([FromForm] TrialBalanceRequest request)
    {
        var response = new ApiResponse<TrialBalance>();

        if (ValidateModel(response))
            return BadRequest(response);

        try
        {
            var db = GetDB();
            var periodDB = GetAcctPeriodDB();
            var ImportDb = GetImportDB();

            var effectiveCsvFile = request.CsvFile ?? request.File;

            string? effectivePeriodId = !string.IsNullOrWhiteSpace(request.PeriodId)
                ? request.PeriodId
                : request.AccountingPeriodId;

            TrialBalanceType effectiveType = request.Type
                ?? request.TrialBalanceType
                ?? TrialBalanceType.Statutory;

            ImportType effectiveImportType = request.ImportType
                ?? request.ImportMode
                ?? (effectiveCsvFile != null ? ImportType.Csv : ImportType.Manual);

            CsvImportType effectiveCsvImportType = request.CsvImportType
                ?? CsvImportType.Default;

            DateTime finalPeriodStart;
            DateTime finalPeriodEnd;

            // ========================================================
            // PERIOD VALIDATION
            // ========================================================
            if (effectiveType == TrialBalanceType.Statutory)
            {
                if (string.IsNullOrWhiteSpace(effectivePeriodId))
                {
                    response.AddError("PeriodId is required for statutory trial balance.");

                    return BadRequest(response);
                }

                var period = await periodDB.GetByIdAsync(effectivePeriodId);
                if (period == null)
                {
                    response.AddError("Accounting period not found.");
                    return BadRequest(response);
                }

                finalPeriodStart = period.PeriodFrom;
                finalPeriodEnd = period.PeriodTo;
            }
            else
            {
                if (!request.PeriodStart.HasValue || !request.PeriodEnd.HasValue)
                {
                    response.AddError("PeriodStart and PeriodEnd are required for management trial balance.");

                    return BadRequest(response);

                }

                finalPeriodStart = request.PeriodStart.Value;
                finalPeriodEnd = request.PeriodEnd.Value;
            }

            if (finalPeriodStart > finalPeriodEnd)
            {
                response.AddError("PeriodStart cannot be greater than PeriodEnd.");




                return BadRequest(response);

            }

            // ========================================================
            // CSV VALIDATION
            // ========================================================
            if (effectiveImportType == ImportType.Csv && effectiveCsvFile == null)
            {
                response.AddError("CSV file is required when import type is CSV.");

                return BadRequest(response);

            }

            // ========================================================
            // GENERATE TRIAL BALANCE NUMBER
            var refNo = await db.GenerateEntityNumberAsync(
                db.GetCollection(),
                null,
                null,
                "-TB",
                x => x.RefNo
            );

            // ========================================================
            // CREATE TRIAL BALANCE
            // ========================================================
            var trialBalance = new TrialBalance
            {
                RefNo = refNo,
                PeriodId = effectiveType == TrialBalanceType.Statutory ? effectivePeriodId : null,
                PeriodStart = finalPeriodStart,
                PeriodEnd = finalPeriodEnd,
                Type = effectiveType,
                Description = $"Trial Balance From {finalPeriodStart:dd MMM yyyy} - {finalPeriodEnd:dd MMM yyyy}",
                ImportType = effectiveImportType,
                CsvImportType = effectiveCsvImportType,
                Status = TrialBalanceStatus.Unbalanced,
                IsLocked = false,
                Turnover = request.Turnover,
                TotalDebit = 0,
                TotalCredit = 0,
                TotalProfitLoss = 0,
                ImportIds = new List<string>(),
                JournalIds = new List<string>()
            };

            // Save CSV file if uploaded
            if (effectiveCsvFile != null)
            {
                trialBalance.CsvFilePath = await db.SaveFile(effectiveCsvFile, "csv");
            }

            // Save attachment if uploaded
            if (request.Attachment != null)
            {
                trialBalance.AttachmentFilePath = await db.SaveFile(request.Attachment, "attachments");
            }

            // Save trial balance using base RecordDB.AddAsync
            var result = await db.AddAsync(trialBalance);
            if (!result)
            {
                response.Message = "Failed to save trial balance.";
                if (db.LastError != null)
                    response.AddError(db.LastError.Message);

                return BadRequest(response);

            }

            // ========================================================
            // CSV IMPORT PROCESSING
            // ========================================================
            if (effectiveImportType == ImportType.Csv && effectiveCsvFile != null)
            {
                List<ImportRow> rows;
                try
                {
                    rows = await CsvUtils.ParseTrialBalanceCsv(effectiveCsvFile);
                }
                catch (Exception ex)
                {
                    // Rollback trial balance if CSV processing fails
                    await db.DeleteAsync(trialBalance.Id, softDelete: false);
                    response.AddError($"Unable to process CSV file: {ex.Message}");

                    return BadRequest(response);

                }

                if (rows.Count == 0)
                {
                    await db.DeleteAsync(trialBalance.Id, softDelete: false);
                    response.AddError("CSV file does not contain any valid data.");

                    return BadRequest(response);

                }

                var importNumber = await db.GenerateEntityNumberAsync(
                    db.GetImportCollection(),
                    trialBalance.Id,
                    trialBalance.RefNo,
                    "-I",
                    x => x.Number
                );

                var import = new Import
                {
                    Number = importNumber,
                    TrialBalance = new TrialBalanceReference
                    {
                        Id = trialBalance.Id,
                        Name = trialBalance.RefNo
                    },
                    Description = request.Description
                    ?? $"Trial Balance From {finalPeriodStart:dd MMM yyyy} - {finalPeriodEnd:dd MMM yyyy}",
                    Headers = new List<string>
                    {
                        "Account Code",
                        "Account Name",
                        "Debit",
                        "Credit"
                    },
                    FileName = effectiveCsvFile.FileName,
                    PeriodStart = trialBalance.PeriodStart,
                    PeriodEnd = trialBalance.PeriodEnd,
                    Columns = new List<ImportColumn>(),
                    Rows = rows,
                    ImportType = ImportType.Csv,
                    CsvImportType = effectiveCsvImportType,
                    CreatedAt = DateTime.UtcNow
                };

                var importSaved = await ImportDb.AddImportAsync(import);
                if (importSaved)
                {
                    trialBalance.ImportIds.Add(import.Id);
                    await db.AddImportIdAsync(trialBalance.Id, import.Id);
                }
                else if (db.LastError != null)
                {
                    response.AddError($"Trial balance created, but CSV  import failed: {db.LastError.Message}");
                }
            }

            response.Result = trialBalance;
            response.Message = "Trial balance created successfully.";
        }
        catch (Exception ex)
        {
            response.AddError(ex.Message);
            return StatusCode(500, response);

        }

        return response;
    }
    #endregion



    #region uploadCSV
    [HttpPatch("{RefNo}/uploadCSV")]
    [Consumes("multipart/form-data")]
    public async Task<ActionResult<ApiResponse<TrialBalance>>> uploadCSV(string RefNo, [FromForm] TrialBalanceRequest request)
    {
        //upload csv in the backend 
        // create import generate code 
        // add the import id of the csv 
        var response = new ApiResponse<TrialBalance>();

        if (ValidateModel(response))
            return BadRequest(response);

        try
        {
            var db = GetDB();
            // var periodDB = GetAcctPeriodDB();
            var ImportDb = GetImportDB();


            var existingTB = await db.GetByRefNoOrIdAsync(RefNo);

            if (existingTB == null)
            {
                response.AddError("Trial balance not found.");
                // response.Message = "Trial balance not found.";
                return BadRequest(response);
            }

            if (existingTB.IsLocked)
            {
                response.AddError("Locked trial balance cannot be updated.");
                response.Message = "Trial balance is locked.";
                return BadRequest(response);
            }

            var effectiveCsvFile = request.CsvFile;

            // string? effectivePeriodId = !string.IsNullOrWhiteSpace(request.PeriodId)
            //     ? request.PeriodId
            //     : request.AccountingPeriodId;

            // TrialBalanceType effectiveType = request.Type
            //     ?? request.TrialBalanceType
            //     ?? TrialBalanceType.Statutory;

            ImportType effectiveImportType = request.ImportType
                ?? request.ImportMode
                ?? (effectiveCsvFile != null ? ImportType.Csv : ImportType.Manual);

            CsvImportType effectiveCsvImportType = request.CsvImportType
                ?? CsvImportType.Default;



            if (effectiveImportType == ImportType.Csv && effectiveCsvFile == null)
            {
                response.AddError("CSV file is required when import type is CSV.");

                return BadRequest(response);

            }


            var trialBalance = new TrialBalance
            {

                ImportType = effectiveImportType,
                CsvImportType = effectiveCsvImportType,

                IsLocked = false,

            };


            if (effectiveImportType == ImportType.Csv && effectiveCsvFile != null)
            {
                List<ImportRow> rows;
                try
                {
                    rows = await CsvUtils.ParseTrialBalanceCsv(effectiveCsvFile);
                }
                catch (Exception ex)
                {

                    response.AddError($"Unable to process CSV file: {ex.Message}");

                    return BadRequest(response);

                }

                if (rows.Count == 0)
                {
                    // await db.DeleteAsync(trialBalance.Id, softDelete: false);
                    response.AddError("CSV file does not contain any valid data.");

                    return BadRequest(response);

                }

                var importNumber = await db.GenerateEntityNumberAsync(
                    db.GetImportCollection(),
                    existingTB.Id,
                    existingTB.RefNo,
                    "-I",
                    x => x.Number
                );

                var import = new Import
                {
                    Number = importNumber,
                    TrialBalance = new TrialBalanceReference
                    {
                        Id = existingTB.Id,
                        Name = existingTB.RefNo
                    },
                    Headers = new List<string>
                    {
                        "Account Code",
                        "Account Name",
                        "Debit",
                        "Credit"
                    },
                    Description = existingTB.Description
                    ?? $"Trial Balance From {existingTB.PeriodStart:dd MMM yyyy} - {existingTB.PeriodEnd:dd MMM yyyy}",
                    FileName = effectiveCsvFile.FileName,
                    PeriodStart = existingTB.PeriodStart,
                    PeriodEnd = existingTB.PeriodEnd,
                    Columns = new List<ImportColumn>(),
                    Rows = rows,
                    ImportType = ImportType.Csv,
                    CsvImportType = effectiveCsvImportType,
                    CreatedAt = DateTime.UtcNow
                };

                var importSaved = await ImportDb.AddImportAsync(import);
                if (importSaved)
                {
                    existingTB.ImportIds.Add(import.Id);
                    await db.AddImportIdAsync(existingTB.Id, import.Id);
                }


                else if (db.LastError != null)
                {
                    response.AddError($"CSV  import failed: {db.LastError.Message}");
                }
            }

            response.Result = existingTB;
            response.Message = "CSV Uploaded successfully.";
        }


        catch (Exception ex)
        {
            response.AddError(ex.Message);
            return StatusCode(500, response);

        }

        return response;
    }
    #endregion


    #region trial balance get all

    [HttpGet]
    public async Task<ActionResult<ApiResponse<object>>> GetAll()
    {

        Console.WriteLine("GetAll TrialBalances API called");
        var response = new ApiResponse<object>();

        try
        {
            var db = GetDB();
            var periodDB = GetAcctPeriodDB();

            // 1. Get trial balances.
            var trialBalances = await db.GetLatestTB();

            var result = new List<object>();

            foreach (var tb in trialBalances)
            {
                // 2. Get accounting period.
                object? period = null;

                if (!string.IsNullOrWhiteSpace(tb.PeriodId))
                {
                    period = await periodDB.GetByIdAsync(tb.PeriodId);
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

                // 3. Get journal IDs and item count.
                var journalIds = tb.JournalIds ?? new List<string>();

                var itemsCount = 0;

                if (journalIds.Count > 0)
                {
                    var journals = await db.GetJournalsByIdsAsync(
                        journalIds
                    );

                    itemsCount = journals.Sum(
                        x => x.Items?.Count ?? 0
                    );
                }

                // 4. Build trial balance response.
                result.Add(new
                {
                    refNo = tb.RefNo,
                    periodStart = tb.PeriodStart,
                    periodEnd = tb.PeriodEnd,
                    period,
                    description = tb.Description,
                    type = tb.Type,
                    turnover = tb.Turnover,
                    importType = tb.ImportType,
                    csvImportType = tb.CsvImportType,
                    totalDebit = tb.TotalDebit,
                    totalCredit = tb.TotalCredit,
                    totalProfitLoss = tb.TotalProfitLoss,
                    status = tb.Status,
                    accountReports = tb.AccountReports,
                    isLocked = tb.IsLocked,
                    csvFilePath = tb.CsvFilePath,
                    attachmentFilePath = tb.AttachmentFilePath,
                    itemsCount,
                    journalIds,
                    id = tb.Id
                });
            }

            response.Status = true;
            response.Message = "Trial balances retrieved successfully.";
            response.Result = result;

            return response;
        }
        catch (Exception ex)
        {

            // Console.WriteLine("ex", ex.ToString(), ex.Message);
            response.AddError(ex.Message);
            response.Message = "Failed to retrieve trial balances.";


            return StatusCode(500, response);

        }
    }

    #endregion

    #region trial balance get by id
    [HttpGet("{RefNo}")]
    public async Task<IActionResult> GetById(
            string RefNo)
    {
        var db = GetDB();
        var periodDb = GetAcctPeriodDB();
        var chartAccountDb = GetChartAccountDB();

        // ============================================================
        // FIND TRIAL BALANCE (Ensuring IsDeleted is false)
        // ============================================================

        var tb = await db.GetByRefNoOrIdAsync(RefNo);

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
            period = await periodDb.GetByIdAsync(tb.PeriodId);
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
        // GET JOURNALS (Ensuring soft-deleted journals are excluded)
        // ============================================================

        var journals = await db.GetJournalsByIdsAsync(journalIds);


        // ============================================================
        // BUILD ITEMS FROM JOURNALS
        //
        // IMPORTANT:
        // TrialBalance does NOT contain Items.
        // Journal is the source of truth.
        // ============================================================

        var items =
            new List<object>();

        if (journals.Count > 0)
        {
            var allAccounts = await chartAccountDb.GetAllAsync(x => !x.IsDeleted);
            var accountMapByCode = allAccounts
                .Where(x => !string.IsNullOrWhiteSpace(x.Code))
                .GroupBy(x => x.Code.Trim(), StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

            var accountMapByName = allAccounts
                .Where(x => !string.IsNullOrWhiteSpace(x.AccountName))
                .GroupBy(x => x.AccountName.Trim(), StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

            foreach (var journal in journals)
            {
                if (journal.Items == null ||
                    journal.Items.Count == 0)
                {
                    continue;
                }

                foreach (var journalItem in journal.Items)
                {
                    ChartAccount? account = null;
                    if (!string.IsNullOrWhiteSpace(journalItem.AccountCode) &&
                        accountMapByCode.TryGetValue(journalItem.AccountCode.Trim(), out var accByCode))
                    {
                        account = accByCode;
                    }
                    else if (!string.IsNullOrWhiteSpace(journalItem.AccountName) &&
                        accountMapByName.TryGetValue(journalItem.AccountName.Trim(), out var accByName))
                    {
                        account = accByName;
                    }

                    items.Add(
                        new
                        {
                            account =
                                account == null
                                    ? new
                                    {
                                        id = string.Empty,
                                        name = journalItem.AccountName,
                                        code = journalItem.AccountCode
                                    }
                                    : new
                                    {
                                        id = account.Id,
                                        name = account.AccountName,
                                        code = account.Code
                                    },
                            note = journalItem.Note,
                            amount =
                                journalItem.Credit > 0
                                    ? -journalItem.Credit
                                    : journalItem.Debit,
                            debit = journalItem.Debit,
                            credit = journalItem.Credit,
                            type = journalItem.Nature
                        });
                }
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


        var status =
            totalDebit == totalCredit
                ? TrialBalanceStatus.Balanced
                : TrialBalanceStatus.Unbalanced;

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
    #endregion

    #region Trial Balance Update

    [HttpPut("{RefNo}")]
    [Consumes("multipart/form-data")]
    public async Task<ActionResult<ApiResponse<TrialBalance>>> Update(
        string RefNo,
        [FromForm] TrialBalanceRequest request)
    {
        var response = new ApiResponse<TrialBalance>();

        if (ValidateModel(response))
            return response;

        try
        {
            var db = GetDB();
            var periodDB = GetAcctPeriodDB();


            var existing = await db.GetByRefNoOrIdAsync(RefNo);

            if (existing == null)
            {
                response.AddError("Trial balance not found.");
                response.Message = "Trial balance not found.";
                return BadRequest(response);
            }

            if (existing.IsLocked)
            {
                response.AddError("Locked trial balance cannot be updated.");
                response.Message = "Trial balance is locked.";
                return BadRequest(response);
            }


            var effectivePeriodId =
                !string.IsNullOrWhiteSpace(request.PeriodId)
                    ? request.PeriodId
                    : !string.IsNullOrWhiteSpace(request.AccountingPeriodId)
                        ? request.AccountingPeriodId
                        : existing.PeriodId;

            var effectiveType =
                request.Type
                ?? request.TrialBalanceType
                ?? existing.Type;

            var effectiveImportType =
                request.ImportType
                ?? request.ImportMode
                ?? existing.ImportType;

            var effectiveCsvImportType =
                request.CsvImportType
                ?? existing.CsvImportType;

            var effectiveCsvFile =
                request.CsvFile ?? request.File;

            DateTime finalPeriodStart;
            DateTime finalPeriodEnd;

            // ========================================================
            // PERIOD VALIDATION
            // ========================================================

            if (effectiveType == TrialBalanceType.Statutory)
            {
                if (string.IsNullOrWhiteSpace(effectivePeriodId))
                {
                    response.AddError(
                        "PeriodId is required for statutory trial balance."
                    );
                    return BadRequest(response);
                }

                var period = await periodDB.GetByIdAsync(effectivePeriodId);

                if (period == null)
                {
                    response.AddError("Accounting period not found.");

                    return BadRequest(response);
                }

                finalPeriodStart = period.PeriodFrom;
                finalPeriodEnd = period.PeriodTo;

                existing.PeriodId = effectivePeriodId;
            }
            else
            {
                finalPeriodStart =
                    request.PeriodStart ?? existing.PeriodStart;

                finalPeriodEnd =
                    request.PeriodEnd ?? existing.PeriodEnd;

                if (finalPeriodStart == default ||
                    finalPeriodEnd == default)
                {
                    response.AddError(
                        "PeriodStart and PeriodEnd are required for management trial balance."
                    );
                    return response;
                }

                existing.PeriodId = null;
            }

            if (finalPeriodStart > finalPeriodEnd)
            {
                response.AddError(
                    "PeriodStart cannot be greater than PeriodEnd."
                );

                return BadRequest(response);
            }

            existing.PeriodStart = finalPeriodStart;
            existing.PeriodEnd = finalPeriodEnd;

            // ========================================================
            // CSV VALIDATION
            // A previously uploaded CSV can be retained without
            // uploading the file again.
            // ========================================================

            if (effectiveImportType == ImportType.Csv &&
                effectiveCsvFile == null &&
                string.IsNullOrWhiteSpace(existing.CsvFilePath))
            {
                response.AddError(
                    "CSV file is required when import type is CSV."
                );
                return response;
            }

            // ========================================================
            // UPDATE TRIAL BALANCE METADATA
            // ========================================================

            existing.Type = effectiveType;
            existing.ImportType = effectiveImportType;
            existing.CsvImportType = effectiveCsvImportType;

            if (request.Description != null)
            {
                existing.Description = request.Description;
            }

            // Turnover is non-nullable in TrialBalanceRequest.
            // This assumes the frontend sends Turnover on every update.
            existing.Turnover = request.Turnover;

            // ========================================================
            // SAVE NEW CSV FILE, IF PROVIDED
            // ========================================================

            if (effectiveCsvFile != null)
            {
                existing.CsvFilePath =
                    await db.SaveFile(effectiveCsvFile, "csv");
            }

            // ========================================================
            // SAVE NEW ATTACHMENT, IF PROVIDED
            // ========================================================

            if (request.Attachment != null)
            {
                existing.AttachmentFilePath =
                    await db.SaveFile(request.Attachment, "attachments");
            }

            // ========================================================
            // GET RELATED JOURNALS
            // ========================================================

            existing.JournalIds ??= new List<string>();
            existing.ImportIds ??= new List<string>();

            var allRelatedJournals =
                await db.GetJournalsByIdsAsync(existing.JournalIds);

            var journals = allRelatedJournals
                .Where(x =>
                    x.TrialBalance != null &&
                    x.TrialBalance.Id == existing.Id)
                .ToList();

            List<Journal> journalsToUpdate;

            if (!string.IsNullOrWhiteSpace(request.JournalId))
            {
                var selectedJournal = journals.FirstOrDefault(
                    x => x.Id == request.JournalId
                );

                if (selectedJournal == null)
                {
                    response.AddError(
                        "The specified journal does not belong to this trial balance or was not found."
                    );
                    return BadRequest(response);

                }

                journalsToUpdate = new List<Journal>
            {
                selectedJournal
            };
            }
            else
            {
                journalsToUpdate = journals;
            }

            // ========================================================
            // UPDATE JOURNAL METADATA
            // Do not modify journal transaction items or their totals.
            // ========================================================

            foreach (var journal in journalsToUpdate)
            {
                journal.TrialBalance = new TrialBalanceReference
                {
                    Id = existing.Id,
                    Name = existing.RefNo
                };

                journal.Type = existing.Type;
                journal.ImportType = existing.ImportType;
                journal.CsvImportType = existing.CsvImportType;

                var journalUpdateResult =
                    await db.GetJournalCollection().ReplaceOneAsync(
                        x => x.Id == journal.Id && !x.IsDeleted,
                        journal
                    );

                if (journalUpdateResult.MatchedCount == 0)
                {
                    response.AddError(
                        $"Failed to update journal {journal.Number}."
                    );

                    return BadRequest(response);

                }
            }

            // ========================================================
            // RELOAD JOURNALS
            // ========================================================

            journals = await db.GetJournalsByTrialBalanceIdAsync(
                existing.Id
            );

            // ========================================================
            // RECALCULATE TRIAL BALANCE TOTALS
            // ========================================================

            existing.TotalDebit = journals.Sum(x => x.TotalDebit);
            existing.TotalCredit = journals.Sum(x => x.TotalCredit);

            existing.Status =
                existing.TotalDebit == existing.TotalCredit
                    ? TrialBalanceStatus.Balanced
                    : TrialBalanceStatus.Unbalanced;

            // TotalProfitLoss is not present in the current
            // TrialBalanceRequest, so preserve its existing value.

            // ========================================================
            // SAVE UPDATED TRIAL BALANCE
            // ========================================================

            var updateResult = await db.GetCollection().ReplaceOneAsync(
                x => x.Id == existing.Id && !x.IsDeleted,
                existing
            );

            if (updateResult.MatchedCount == 0)
            {
                response.AddError(
                    "Failed to update trial balance."
                );
                response.Message = "Trial balance update failed.";

                return BadRequest(response);

            }

            // ========================================================
            // RESPONSE
            // Uses the existing ApiResponse<TrialBalance> structure
            // ========================================================

            response.Status = true;
            response.Message =
                "Trial balance updated successfully.";
            response.Result = existing;


            return Ok(response);
        }
        catch (Exception ex)
        {
            response.AddError(ex.Message);
            response.Message = "Failed to update trial balance.";

            return StatusCode(500, response);
        }
    }
    #endregion

    #region trial balance delete
    [HttpDelete("{id}")]
    public async Task<ActionResult<ApiResponse<bool>>> Delete(string id)
    {
        var response = new ApiResponse<bool>();

        try
        {
            var db = GetDB();
            var existing = await db.GetByIdAsync(id);
            if (existing == null)
            {
                response.AddError("Trial balance not found.");
                return response;
            }

            var result = await db.DeleteAsync(id, softDelete: true);
            if (result)
            {
                response.Result = true;
                response.Message = "Trial balance deleted successfully.";
            }
            else
            {
                response.Message = "Failed to delete trial balance.";
                if (db.LastError != null)
                    response.AddError(db.LastError.Message);
            }
        }
        catch (Exception ex)
        {
            response.AddError(ex.Message);

            return StatusCode(500, response);
        }

        return response;
    }
    #endregion


    #region Trial Balance Import

    [HttpPost("{RefNo}/imports")]
    public async Task<ActionResult<ApiResponse<object>>> Import(
        string RefNo,
        [FromBody] Import request)
    {
        var response = new ApiResponse<object>();

        try
        {
            var db = GetDB();
            var ImportDb = GetImportDB();

            var trialBalance =
                await db.GetByRefNoOrIdAsync(RefNo);

            if (trialBalance == null)
            {
                response.AddError("Trial balance not found.");

                return BadRequest(response);
            }

            // ========================================================
            // VALIDATE IMPORT ROWS
            // ========================================================

            if (request.Rows == null || request.Rows.Count == 0)
            {
                response.AddError(
                    "Import must contain at least one row.");

                return BadRequest(response);

            }

            // ========================================================
            // GENERATE IMPORT NUMBER
            // Example: TB-01-I01
            // ========================================================

            var importNumber =
                await db.GenerateEntityNumberAsync(
                    db.GetImportCollection(),
                    trialBalance.Id,
                    trialBalance.RefNo,
                    "-I",
                    x => x.Number
                );

            // ========================================================
            // CREATE IMPORT
            // ========================================================

            var import = new Import
            {
                Number = importNumber,

                TrialBalance = new TrialBalanceReference
                {
                    Id = trialBalance.Id,
                    Name = trialBalance.RefNo
                },

                Columns = request.Columns
                    ?? new List<ImportColumn>(),

                Headers = request.Headers
                    ?? new List<string>(),

                Rows = request.Rows,

                ImportType = ImportType.Csv,

                PeriodStart = trialBalance.PeriodStart,

                PeriodEnd = trialBalance.PeriodEnd,

                CsvImportType = request.CsvImportType,

                CreatedAt = DateTime.UtcNow
            };

            // ========================================================
            // SAVE IMPORT
            // ========================================================

            var importSaved =
                await ImportDb.AddImportAsync(import);

            if (!importSaved)
            {
                response.AddError(
                    db.LastError != null
                        ? $"Failed to save import: {db.LastError.Message}"
                        : "Failed to save import.");

                return BadRequest(response);
            }

            // ========================================================
            // LINK IMPORT TO TRIAL BALANCE
            // ========================================================

            var importLinked =
                await db.AddImportIdAsync(
                    trialBalance.Id,
                    import.Id);

            if (!importLinked)
            {
                // Compensate for the failed relationship update.
                await ImportDb.DeleteAsync(import.Id);

                response.AddError(
                    "Import was created but could not be linked to the trial balance.");

                return BadRequest(response);
            }

            // Keep the in-memory object in sync.
            trialBalance.ImportIds ??= new List<string>();

            if (!trialBalance.ImportIds.Contains(import.Id))
            {
                trialBalance.ImportIds.Add(import.Id);
            }

            // ========================================================
            // GENERATE JOURNAL NUMBER
            // ========================================================

            var journalNumber =
                await db.GenerateEntityNumberAsync(
                    db.GetJournalCollection(),
                    trialBalance.Id,
                    trialBalance.RefNo,
                    "-J",
                    x => x.Number
                );

            // ========================================================
            // CREATE EMPTY JOURNAL
            // No JournalItems are created during import.
            // ========================================================

            var journal = new Journal
            {
                Number = journalNumber,

                TrialBalance = new TrialBalanceReference
                {
                    Id = trialBalance.Id,
                    Name = trialBalance.RefNo
                },

                Imports = new ImportsReference
                {
                    Id = import.Id,
                    Name = import.Number
                },

                Type = trialBalance.Type,

                Status = TrialBalanceStatus.Unbalanced,

                ImportType = ImportType.Csv,

                CsvImportType = request.CsvImportType,

                Items = new List<JournalItem>(),

                ItemsCount = 0,

                TotalDebit = 0,

                TotalCredit = 0,

                CreatedAt = DateTime.UtcNow
            };

            // ========================================================
            // SAVE JOURNAL
            // ========================================================

            var journalCollection = db.GetJournalCollection();

            try
            {
                journal.Id = ObjectId.GenerateNewId().ToString();
                journal.IsDeleted = false;

                await journalCollection.InsertOneAsync(journal);
            }
            catch (Exception ex)
            {
                // Roll back the import and its TrialBalance reference.
                await db.GetImportCollection().DeleteOneAsync(
                    x => x.Id == import.Id);

                await db.GetCollection().UpdateOneAsync(
                    x => x.Id == trialBalance.Id,
                    Builders<TrialBalance>.Update.Pull(
                        x => x.ImportIds,
                        import.Id));

                response.AddError(
                    $"Journal creation failed: {ex.Message}");

                return response;
            }

            // ========================================================
            // LINK JOURNAL TO TRIAL BALANCE
            // ========================================================

            var journalLinked =
                await db.GetCollection().UpdateOneAsync(
                    x => x.Id == trialBalance.Id && !x.IsDeleted,
                    Builders<TrialBalance>.Update.AddToSet(
                        x => x.JournalIds,
                        journal.Id));

            if (journalLinked.MatchedCount == 0)
            {
                // The TrialBalance disappeared or was deleted.
                await journalCollection.DeleteOneAsync(
                    x => x.Id == journal.Id);

                await db.GetImportCollection().DeleteOneAsync(
                    x => x.Id == import.Id);

                await db.GetCollection().UpdateOneAsync(
                    x => x.Id == trialBalance.Id,
                    Builders<TrialBalance>.Update.Pull(
                        x => x.ImportIds,
                        import.Id));

                response.AddError(
                    "Journal could not be linked to the trial balance.");


                return BadRequest(response);
            }

            // ========================================================
            // RESPONSE
            // ========================================================

            response.Status = true;
            response.Message = "Import and journal created successfully.";

            response.Result = new
            {
                import = new
                {
                    id = import.Id,
                    number = import.Number,
                    rowsCount = import.Rows.Count,
                    importType = import.ImportType,
                    csvImportType = import.CsvImportType
                },

                journal = new
                {
                    id = journal.Id,
                    number = journal.Number,
                    imports = journal.Imports,
                    itemsCount = journal.ItemsCount,
                    totalDebit = journal.TotalDebit,
                    totalCredit = journal.TotalCredit,
                    status = journal.Status
                },

                trialBalance = new
                {
                    id = trialBalance.Id,
                    refNo = trialBalance.RefNo,
                    importIds = trialBalance.ImportIds,
                    journalIds = trialBalance.JournalIds
                        .Append(journal.Id)
                        .Distinct()
                        .ToList()
                }
            };

            return response;
        }
        catch (Exception ex)
        {
            response.AddError(ex.Message);

            return StatusCode(500, response);
        }
    }
    #endregion





    #region Import Data To Journal

    [HttpPost("{RefNo}/imports/{importsId}")]
    public async Task<ActionResult<ApiResponse<object>>> ImportDataToJournal(
        string RefNo,
        string importsId)
    {
        var response = new ApiResponse<object>();

        try
        {
            var db = GetDB();
            var ImportDb = GetImportDB();

            // ========================================================
            // FIND TRIAL BALANCE
            // ========================================================

            var trialBalance =
                await db.GetByRefNoOrIdAsync(RefNo);

            if (trialBalance == null)
            {
                response.AddError("Trial balance not found.");
                return BadRequest(response);
            }

            // ========================================================
            // FIND EXISTING IMPORT
            // Do not create a new Import document.
            // ========================================================

            var importsData =
                await ImportDb.GetByIdAsync(importsId);

            if (importsData == null)
            {
                response.AddError("Imports data not found.");
                return BadRequest(response);
            }

            // Ensure the import belongs to this Trial Balance.
            if (importsData.TrialBalance == null ||
                importsData.TrialBalance.Id != trialBalance.Id)
            {
                response.AddError(
                    "The specified import does not belong to this trial balance.");
                return BadRequest(response);
            }

            // ========================================================
            // VALIDATE IMPORT ROWS
            // ========================================================

            if (importsData.Rows == null ||
                importsData.Rows.Count == 0)
            {
                response.AddError(
                    "Import does not contain any rows.");
                return BadRequest(response);
            }

            // ========================================================
            // CONVERT IMPORT ROWS TO JOURNAL ITEMS
            // Preserve existing mapping logic.
            // ========================================================

            var journalItems = importsData.Rows
                .Where(row =>
                    row != null &&
                    (!string.IsNullOrWhiteSpace(row.Code) ||
                     !string.IsNullOrWhiteSpace(row.Name)))
                .Select(row => new JournalItem
                {
                    AccountCode = row.Code,
                    AccountName = row.Name,
                    Nature = row.Nature,
                    Note = row.Note,
                    Debit = row.Debit,
                    Credit = row.Credit,
                    Amount = row.Debit + row.Credit
                })
                .ToList();

            // ========================================================
            // GENERATE JOURNAL NUMBER
            // ========================================================

            var journalCollection = db.GetJournalCollection();

            var journalNumber =
                await db.GenerateEntityNumberAsync(
                    journalCollection,
                    trialBalance.Id,
                    trialBalance.RefNo,
                    "-J",
                    x => x.Number
                );

            // ========================================================
            // CREATE JOURNAL
            // ========================================================

            var journal = new Journal
            {
                Id = ObjectId.GenerateNewId().ToString(),

                Number = journalNumber,

                TrialBalance = new TrialBalanceReference
                {
                    Id = trialBalance.Id,
                    Name = trialBalance.RefNo
                },

                // Link to the existing Import.
                Imports = new ImportsReference
                {
                    Id = importsData.Id,
                    Name = importsData.Number
                },

                JournalType = JournalType.Normal,

                Type = trialBalance.Type,

                JournalStatus = JournalStatus.Drafted,

                Status = TrialBalanceStatus.Unbalanced,

                ImportType = importsData.ImportType,

                CsvImportType = importsData.CsvImportType,

                PeriodStart = importsData.PeriodStart,

                PeriodEnd = importsData.PeriodEnd,

                Description = importsData.Description,

                Items = journalItems,

                ItemsCount = journalItems.Count,

                TotalDebit = journalItems.Sum(item => item.Debit),

                TotalCredit = journalItems.Sum(item => item.Credit),

                CreatedAt = DateTime.UtcNow,

                IsDeleted = false
            };

            // ========================================================
            // INSERT JOURNAL
            // ========================================================

            await journalCollection.InsertOneAsync(journal);

            // ========================================================
            // UPDATE TRIAL BALANCE
            // Preserve existing IDs and append the new Journal ID.
            // ========================================================

            var updatedJournalIds =
                trialBalance.JournalIds ?? new List<string>();

            if (!updatedJournalIds.Contains(journal.Id))
            {
                updatedJournalIds.Add(journal.Id);
            }

            var trialBalanceUpdate =
                Builders<TrialBalance>.Update
                    .Set(x => x.Description, importsData.Description)
                    .AddToSet(x => x.JournalIds, journal.Id);

            var trialBalanceUpdateResult =
                await db.GetCollection().UpdateOneAsync(
                    x => x.Id == trialBalance.Id && !x.IsDeleted,
                    trialBalanceUpdate);

            if (trialBalanceUpdateResult.MatchedCount == 0)
            {
                // Compensate if the TrialBalance relationship update fails.
                await journalCollection.DeleteOneAsync(
                    x => x.Id == journal.Id);

                response.AddError(
                    "Journal was created but could not be linked to the trial balance.");

                return BadRequest(response);
            }

            // ========================================================
            // UPDATE EXISTING IMPORT STATUS
            // ========================================================

            var importStatusUpdate =
                await db.GetImportCollection().UpdateOneAsync(
                    x => x.Id == importsData.Id && !x.IsDeleted,
                    Builders<Import>.Update.Set(
                        x => x.Status,
                        ImportStatus.Imported));

            if (importStatusUpdate.MatchedCount == 0)
            {
                response.AddError(
                    "Journal was created, but the import status could not be updated.");


                return BadRequest(response);
            }

            // ========================================================
            // RESPONSE
            // ========================================================

            response.Status = true;
            response.Message = "Import data transferred to journal successfully.";

            response.Result = new
            {
                journal = new
                {
                    id = journal.Id,
                    number = journal.Number,
                    importsId = journal.Imports.Id,
                    description = journal.Description,
                    periodStart = journal.PeriodStart,
                    periodEnd = journal.PeriodEnd,
                    itemsCount = journal.ItemsCount,
                    totalDebit = journal.TotalDebit,
                    totalCredit = journal.TotalCredit,
                    status = journal.Status,
                    items = journal.Items
                },

                trialBalance = new
                {
                    id = trialBalance.Id,
                    name = trialBalance.RefNo,
                    description = importsData.Description,
                    journalIds = updatedJournalIds
                }
            };

            return response;
        }
        catch (Exception ex)
        {
            response.AddError(ex.Message);
            return StatusCode(500, response);
        }
    }

    #endregion

    #region Get Trial Balance Imports

    [HttpGet("{trialBalanceId}/imports")]
    public async Task<ActionResult<ApiResponse<object>>> GetImports(
        string trialBalanceId)
    {
        var response = new ApiResponse<object>();

        try
        {
            var db = GetDB();
            var chartAccountDb = GetChartAccountDB();
            var ImportDb = GetImportDB();

            // ========================================================
            // GET TRIAL BALANCE
            // ========================================================

            var trialBalance =
                await db.GetByRefNoOrIdAsync(trialBalanceId);

            if (trialBalance == null)
            {
                response.AddError("Trial balance not found.");
                return BadRequest(response);
            }

            // ========================================================
            // GET ALL IMPORTS LINKED TO TRIAL BALANCE
            // ========================================================

            var imports =
                await ImportDb.GetImportsByTrialBalanceIdAsync(
                    trialBalance.Id);

            // ========================================================
            // BUILD UNIQUE ACCOUNT CODE LIST
            // ========================================================

            var allRows = imports
                .Where(x => x.Rows != null)
                .SelectMany(x => x.Rows)
                .ToList();

            var codes = allRows
                .Select(x => x.Code)
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct()
                .ToList();

            // ========================================================
            // BUILD ACCOUNT ITEMS
            // ========================================================

            var items = new List<object>();

            foreach (var code in codes)
            {
                // Get active chart account.
                var account = await chartAccountDb.GetAllbyCode(code);

                // Get all rows belonging to this account.
                var rows = allRows
                    .Where(x => x.Code == code)
                    .ToList();

                // Calculate one amount for each import.
                var amounts = imports
                    .Select(import =>
                    {
                        var row = import.Rows?
                            .FirstOrDefault(x => x.Code == code);

                        if (row == null)
                        {
                            return 0m;
                        }

                        return row.Credit > 0
                            ? -row.Credit
                            : row.Debit;
                    })
                    .ToList();

                var firstRow = rows.FirstOrDefault();

                items.Add(new
                {
                    type =
                        firstRow?.Nature ??
                        AccountNature.Debit,

                    code,

                    amounts,

                    rAmounts = imports
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
                });
            }
            response.Status = true;
            response.Message = "Imports retrieved successfully.";

            response.Result = new
            {
                trialBalances = new[]
                {
                new
                {
                    periodStart = trialBalance.PeriodStart,
                    periodEnd = trialBalance.PeriodEnd,
                    totalDebit = trialBalance.TotalDebit,
                    totalCredit = trialBalance.TotalCredit,
                    name = trialBalance.RefNo,
                    id = trialBalance.Id
                }
            },

                items
            };

            return response;
        }
        catch (Exception ex)
        {
            response.AddError(ex.Message);
            return BadRequest(response);
        }
    }

    #endregion


    #region Get Trial Balance Import By Id

    [HttpGet("{RefNo}/imports/{importId}")]
    public async Task<ActionResult<ApiResponse<object>>> GetImport(
        string RefNo,
        string importId)
    {
        var response = new ApiResponse<object>();

        try
        {
            var db = GetDB();
            var ImportDb = GetImportDB();

            // ========================================================
            // GET TRIAL BALANCE
            // ========================================================

            var trialBalance =
                await db.GetByRefNoOrIdAsync(RefNo);

            if (trialBalance == null)
            {
                response.AddError("Trial balance not found.");
                return BadRequest(response);
            }

            // ========================================================
            // GET IMPORT
            // ========================================================
            if (importId == "0")
            {
                response.Status = true;
                response.Message = "Trial balance retrieved successfully.";

                response.Result = new
                {
                    // executionTime = 0,

                    // result = new
                    // {
                    id = string.Empty,
                    number = string.Empty,
                    description = trialBalance.Description ?? string.Empty,

                    periodStart = trialBalance.PeriodStart,
                    periodEnd = trialBalance.PeriodEnd,

                    trialBalance = new
                    {
                        name = trialBalance.RefNo,
                        id = trialBalance.Id
                    },

                    header = new List<string>(),
                    hasHeader = false,

                    csvImportType = trialBalance.CsvImportType,

                    status = trialBalance.Status
               .ToString()
               .ToLowerInvariant(),

                    rows = new List<string[]>()

                };

                return response;
            }

            var imports =
                await ImportDb.GetImportsByTrialBalanceIdAsync(
                    trialBalance.Id);

            var import = imports
                .FirstOrDefault(x => x.Id == importId);

            if (import == null)
            {
                response.AddError("Import not found.");
                return BadRequest(response);
            }

            // ========================================================
            // BUILD IMPORT ROWS
            // Preserve the original imported values.
            // ========================================================

            var rows = (import.Rows ?? new List<ImportRow>())
                .Select(row => new[]
                {
                row.Code,
                row.Name,

                row.Debit.ToString(
                    System.Globalization.CultureInfo.InvariantCulture),

                row.Credit.ToString(
                    System.Globalization.CultureInfo.InvariantCulture)
                })
                .ToList();

            // ========================================================
            // RESPONSE
            // ========================================================

            response.Status = true;
            response.Message = "Import retrieved successfully.";

            response.Result = new

            {
                // =================================================
                // IMPORT
                // =================================================

                id = import.Id,

                number = import.Number,

                description = import.Description ?? string.Empty,

                periodStart = import.PeriodStart,

                periodEnd = import.PeriodEnd,

                // =================================================
                // TRIAL BALANCE
                // =================================================

                trialBalance = new
                {
                    name = trialBalance.RefNo,
                    id = trialBalance.Id
                },

                // =================================================
                // CSV HEADER
                // =================================================

                header = import.Headers ?? new List<string>(),

                hasHeader =
                        import.Headers != null &&
                        import.Headers.Count > 0,

                // =================================================
                // CSV IMPORT TYPE
                // =================================================

                csvImportType = import.CsvImportType,

                // =================================================
                // IMPORT STATUS
                // =================================================

                status = import.Status
                        .ToString()
                        .ToLowerInvariant(),

                // =================================================
                // CSV ROWS
                // =================================================

                rows
                // },

                // status = true
            };

            return response;
        }
        catch (Exception ex)
        {
            response.AddError(ex.Message);
            return StatusCode(500, response);
        }
    }

    #endregion


    #region Update Trial Balance Import

    [HttpPut("imports/{importId}")]
    public async Task<ActionResult<ApiResponse<object>>> UpdateImport(
        string importId,
        [FromBody] Import request)
    {
        var response = new ApiResponse<object>();

        try
        {
            var db = GetDB();
            var chartAccountDb = GetChartAccountDB();
            var ImportDb = GetImportDB();
            // var importCollection = db.GetImportCollection();

            var existing = await ImportDb.GetByIdAsync(importId);
            // .Find(x => x.Id == importId && !x.IsDeleted)
            // .FirstOrDefaultAsync();

            if (existing == null)
            {
                response.AddError("Import not found.");
                return BadRequest(response);
            }
            if (request.Rows == null || request.Rows.Count == 0)
            {
                response.AddError(
                    "Import must contain at least one row.");

                return BadRequest(response);
            }

            foreach (var row in request.Rows)
            {
                if (string.IsNullOrWhiteSpace(row.Code))
                {
                    response.AddError("Account code is required.");
                    return BadRequest(response);
                }

                var account = await chartAccountDb.GetAllbyCode(
                    row.Code);

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

            existing.Rows = request.Rows;

            existing.Columns = request.Columns
                ?? new List<ImportColumn>();

            existing.Headers = request.Headers
                ?? new List<string>();

            // Update other editable fields only if they are intended
            // to be editable in your application.

            // ========================================================
            // SAVE UPDATED IMPORT
            // ========================================================

            var updateResult = await ImportDb.UpdateAsync(importId, existing);
            // x => x.Id == importId && !x.IsDeleted,
            // existing);

            if (!updateResult)
            {
                response.AddError(
                    "Import not found or could not be updated.");

                return BadRequest(response);
            }

            // ========================================================
            // RESPONSE
            // ========================================================

            response.Status = true;
            response.Message = "Import updated successfully.";

            response.Result = new
            {
                id = existing.Id,
                number = existing.Number,
                trialBalance = existing.TrialBalance,
                columns = existing.Columns,
                headers = existing.Headers,
                rows = existing.Rows,
                periodStart = existing.PeriodStart,
                periodEnd = existing.PeriodEnd,
                importType = existing.ImportType,
                csvImportType = existing.CsvImportType,
                status = existing.Status
            };

            return response;
        }
        catch (Exception ex)
        {
            response.AddError(ex.Message);

            return StatusCode(500, response);
        }


    }

    #endregion



    #region Trial Balance Detail
    [HttpGet("{RefNo}/details")]
    public async Task<ActionResult<ApiResponse<object>>> GetTrialBalanceDetails(string RefNo)
    {
        var response = new ApiResponse<object>();

        try
        {
            var db = GetDB();
            var trialBalance = await db.GetByRefNoOrIdAsync(RefNo);

            if (trialBalance == null)
            {
                if (db.LastError != null)
                {
                    response.AddError("Failed to retrieve trial balance.");
                    response.Message = "Database error while retrieving trial balance.";
                    return BadRequest(response);
                }

                response.AddError("Trial balance not found.");
                response.Message = "Trial balance not found.";
                return BadRequest(response);
            }
            object? period = null;

            if (!string.IsNullOrWhiteSpace(trialBalance.PeriodId))
            {
                var periodDB = GetAcctPeriodDB();

                period = await periodDB.GetByIdAsync(trialBalance.PeriodId);

                if (period == null && periodDB.LastError != null)
                {
                    response.AddError("Failed to retrieve accounting period.");
                    response.Message = "Database error while retrieving accounting period.";
                    return BadRequest(response);
                }
            }

            // Management TB may not have an accounting period.

            if (period == null)
            {
                period = new
                {
                    id = trialBalance.PeriodId ?? string.Empty,
                    periodStart = trialBalance.PeriodStart,
                    periodEnd = trialBalance.PeriodEnd,
                    isActive = true,
                    isClosed = false
                };
            }

            // ============================================================
            // 3. GET PENDING IMPORTS
            // ============================================================

            var importIds = trialBalance.ImportIds ?? new List<string>();
            var imports = new List<Import>();

            if (importIds.Count > 0)
            {
                var importDB = GetImportDB();

                imports = await importDB.GetAllAsync(
                    x => importIds.Contains(x.Id) &&
                         x.Status == ImportStatus.Pending
                );

                if (importDB.LastError != null)
                {
                    response.AddError("Failed to retrieve imports.");
                    response.Message = "Database error while retrieving imports.";
                    return BadRequest(response);
                }
            }

            var importResponse = imports
                .OrderBy(x => x.CreatedAt)
                .Select(import => new
                {
                    id = import.Id,
                    refNo = import.Number,
                    description = import.Description
                        ?? trialBalance.Description
                        ?? string.Empty,
                    fileName = import.FileName ?? string.Empty,
                    status = import.Status.ToString(),
                    importedOn = import.CreatedAt,
                    action = new
                    {
                        id = import.Id,
                        edit = true,
                        delete = true
                    }
                })
                .ToList();

            // ============================================================
            // 4. GET JOURNALS
            // ============================================================

            var journalIds = trialBalance.JournalIds ?? new List<string>();

            var journals = await db.GetJournalsByTrialBalanceIdAsync(
                trialBalance.Id

            );

            if (db.LastError != null)
            {
                response.AddError("Failed to retrieve journals.");
                response.Message = "Database error while retrieving journals.";
                return BadRequest(response);
            }


            // Enforce ownership: a journal must belong to this TB.
            journals = journals
                .Where(x =>
                    x.TrialBalance != null &&
                    x.TrialBalance.Id == trialBalance.Id)
                .OrderBy(x => x.CreatedAt)
                .ToList();

            // ============================================================
            // 5. BUILD JOURNAL RESPONSE
            // ============================================================

            var journalResponse = journals
                .Select(journal => new
                {
                    id = journal.Id,
                    number = journal.Number,
                    imports = journal.Imports,

                    TrialBalance = new TrialBalanceReference
                    {
                        Id = trialBalance.Id,
                        Name = trialBalance.RefNo
                    },

                    description = journal.Description ?? string.Empty,

                    itemsCount = journal.ItemsCount > 0
                        ? journal.ItemsCount
                        : journal.Items?.Count ?? 0,

                    journalType = journal.JournalType.ToString(),
                    journalStatus = journal.JournalStatus.ToString(),

                    importType = journal.ImportType == ImportType.Csv
                        ? "Default"
                        : "Manual",

                    status = journal.IsActive,
                    createdOn = journal.CreatedAt,
                    totalDebit = journal.TotalDebit,
                    totalCredit = journal.TotalCredit,

                    action = new
                    {
                        id = journal.Id,
                        edit = journal.JournalStatus != JournalStatus.Posted,
                        delete = journal.JournalStatus != JournalStatus.Posted,
                        unpost = journal.JournalStatus == JournalStatus.Posted,
                        download = true,
                        addAttachment = true
                    }
                })
                .ToList();

            // ============================================================
            // 6. CALCULATE TOTALS FROM JOURNALS
            // ============================================================

            var totalDebit = journals.Sum(x => x.TotalDebit);
            var totalCredit = journals.Sum(x => x.TotalCredit);

            string balanceStatus;

            if (totalDebit == totalCredit)
            {
                balanceStatus = "Balance";
            }
            else if (totalDebit > totalCredit)
            {
                balanceStatus = $"Debit €{totalDebit - totalCredit:N2}";
            }
            else
            {
                balanceStatus = $"Credit €{totalCredit - totalDebit:N2}";
            }

            // ============================================================
            // 7. BUILD RESULT
            // ============================================================

            response.Status = true;
            response.Message = "Trial balance details retrieved successfully.";

            response.Result = new
            {
                trialBalance = new
                {
                    id = trialBalance.Id,
                    refNo = trialBalance.RefNo,
                    period,
                    description = trialBalance.Description,
                    periodStart = trialBalance.PeriodStart,
                    periodEnd = trialBalance.PeriodEnd,
                    type = trialBalance.Type.ToString()
                },

                imports = importResponse,
                journals = journalResponse,

                summary = new
                {
                    totalDebit,
                    totalCredit,
                    difference = Math.Abs(totalDebit - totalCredit),
                    status = balanceStatus,
                    profitLoss = trialBalance.TotalProfitLoss
                }
            };

            return response;
        }
        catch (Exception ex)
        {
            response.AddError(ex.Message);
            response.Message = "Failed to retrieve trial balance details.";
            return StatusCode(500, response);
        }
    }

    #endregion

    #region Trial Balance Posted Data

    [HttpGet("{tbId}/posted-data")]
    public async Task<ActionResult<ApiResponse<object>>> GetPostedTrialBalanceData(string tbId)
    {
        var response = new ApiResponse<object>();

        try
        {
            var db = GetDB();
            var chartAccountDb = GetChartAccountDB();

            // ============================================================
            // 1. GET TRIAL BALANCE
            // ============================================================

            var trialBalance = await db.GetByIdAsync(tbId);

            if (trialBalance == null)
            {
                if (db.LastError != null)
                {
                    response.AddError("Failed to retrieve trial balance.");
                    response.Message = "Database error while retrieving trial balance.";
                    return BadRequest(response);
                }

                response.AddError("Trial balance not found.");
                response.Message = "Trial balance not found.";
                return BadRequest(response);
            }

            // ============================================================
            // 2. GET POSTED JOURNALS
            // Reuse the existing custom DB method.
            // ============================================================

            var journals = await db.GetJournalsByTrialBalanceIdAsync(
                trialBalance.Id,
                onlyPosted: true
            );

            if (db.LastError != null)
            {
                response.AddError("Failed to retrieve posted journals.");
                response.Message = "Database error while retrieving journals.";
                return BadRequest(response);
            }

            // ============================================================
            // 3. GET ALL JOURNAL ITEMS
            // ============================================================

            var journalItems = journals
                .Where(j => j.Items != null)
                .SelectMany(j => j.Items!)
                .ToList();

            // ============================================================
            // 4. GROUP ITEMS BY ACCOUNT CODE ONCE
            // ============================================================

            var accountGroups = journalItems
                .Where(x => !string.IsNullOrWhiteSpace(x.AccountCode))
                .GroupBy(x => x.AccountCode)
                .ToList();

            var accountCodes = accountGroups
                .Select(g => g.Key)
                .ToList();

            // ============================================================
            // 5. GET CHART ACCOUNTS IN ONE QUERY
            // ============================================================

            var chartAccounts = accountCodes.Count == 0
                ? new List<ChartAccount>()
                : await chartAccountDb.GetAllAsync(x =>
                        accountCodes.Contains(x.Code) &&
                        !x.IsDeleted);
            // .ToListAsync();

            // Use the first account for a code if duplicate codes exist.
            var accountMap = chartAccounts
                .GroupBy(x => x.Code)
                .ToDictionary(g => g.Key, g => g.First());

            // ============================================================
            // 6. BUILD ACCOUNT ITEMS
            // ============================================================

            var items = accountGroups
                .Select(group =>
                {
                    var code = group.Key;
                    var accountEntries = group.ToList();

                    accountMap.TryGetValue(code, out var account);

                    var debit = accountEntries.Sum(x => x.Debit);
                    var credit = accountEntries.Sum(x => x.Credit);
                    var amount = debit - credit;

                    return new
                    {
                        groupName = account?.AccountGroup ?? string.Empty,
                        group = account?.AccountGroup ?? string.Empty,

                        type = account?.AccountTypeId ?? string.Empty,
                        code,

                        amounts = new[] { amount },

                        accountNature = amount >= 0
                            ? AccountNature.Debit
                            : AccountNature.Credit,

                        name = account?.AccountName
                            ?? accountEntries.FirstOrDefault()?.AccountName
                            ?? string.Empty,

                        id = account?.Id ?? string.Empty,

                        debit,
                        credit,
                        totalDebit = debit,
                        totalCredit = credit
                    };
                })
                .ToList();

            // ============================================================
            // 7. CALCULATE TOTALS
            // ============================================================

            var totalDebit = journalItems.Sum(x => x.Debit);
            var totalCredit = journalItems.Sum(x => x.Credit);

            // ============================================================
            // 8. BUILD RESPONSE
            // ============================================================

            response.Status = true;
            response.Message = "Posted trial balance data retrieved successfully.";

            response.Result = new
            {
                executionTime = 0,

                result = new
                {
                    trialBalances = new[]
                    {
                    new
                    {
                        periodStart = trialBalance.PeriodStart,
                        periodEnd = trialBalance.PeriodEnd,
                        totalDebit,
                        totalCredit,
                        name = trialBalance.RefNo,
                        id = trialBalance.Id
                    }
                },

                    items
                },

                status = true
            };

            return response;
        }
        catch (Exception ex)
        {
            response.AddError(ex.Message);
            response.Message = "Failed to retrieve posted trial balance data.";
            return StatusCode(500, response);
        }
    }


    #endregion

    #region Post Journal

    [HttpPost("{RefNo}/journals/{journalId}")]
    [Consumes("multipart/form-data")]
    public async Task<ActionResult<ApiResponse<object>>> CreateOrUpdateJournal(
        string RefNo,
        string journalId,
        [FromForm] string journal,
        IFormFile? attachment)
    {
        var response = new ApiResponse<object>();

        try
        {
            var db = GetDB();
            var journalDB = GetJournalDB();

            // 1. Find trial balance by reference number or MongoDB ID.

            var trialBalance = await db.GetByRefNoOrIdAsync(RefNo);

            if (trialBalance == null)
            {
                response.AddError(
                    db.LastError != null
                        ? "Failed to retrieve trial balance."
                        : "Trial balance not found."
                );

                response.Message = "Unable to retrieve trial balance.";
                return BadRequest(response);
            }

            if (trialBalance.IsLocked)
            {
                response.AddError("Locked trial balance cannot be modified.");
                response.Message = "Trial balance is locked.";
                return BadRequest(response);
            }


            Journal request;

            try
            {
                request =
                    System.Text.Json.JsonSerializer.Deserialize<Journal>(
                        journal,
                        new System.Text.Json.JsonSerializerOptions
                        {
                            PropertyNameCaseInsensitive = true
                        }) ?? throw new System.Text.Json.JsonException();
            }
            catch (System.Text.Json.JsonException)
            {
                response.AddError("Invalid journal data.");
                response.Message = "Invalid journal data.";
                return BadRequest(response);
            }

            if (request.Items == null || request.Items.Count == 0)
            {
                response.AddError("At least one journal item is required.");
                response.Message = "Journal validation failed.";
                return BadRequest(response);
            }

            // 3. Calculate totals.

            var totalDebit = request.Items.Sum(x => x.Debit);
            var totalCredit = request.Items.Sum(x => x.Credit);

            // 4. Handle attachment upload once for both operations.

            Attachment? newAttachment = null;

            if (attachment != null && attachment.Length > 0)
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

                await using (var stream = new FileStream(
                    filePath,
                    FileMode.CreateNew))
                {
                    await attachment.CopyToAsync(stream);
                }

                newAttachment = new Attachment
                {
                    Name = Path.GetFileName(attachment.FileName),
                    Path = Path.Combine(
                        "uploads",
                        "journals",
                        fileName
                    ).Replace("\\", "/")
                };
            }

            // ============================================================
            // CREATE JOURNAL
            // journalId = "0"
            // ============================================================

            if (journalId == "0")
            {
                request.Id = string.Empty;

                request.Number = await db.GenerateEntityNumberAsync(
                    journalDB.GetCollection(),
                    trialBalance.Id,
                    trialBalance.RefNo,
                    "-J",
                    x => x.Number
                );

                request.TrialBalance = new TrialBalanceReference
                {
                    Id = trialBalance.Id,
                    Name = trialBalance.RefNo
                };

                request.PeriodStart = trialBalance.PeriodStart;
                request.PeriodEnd = trialBalance.PeriodEnd;
                request.Description = request.Description
                                   ?? $"Trial Balance From {trialBalance.PeriodStart:dd MMM yyyy} - {trialBalance.PeriodEnd:dd MMM yyyy}";
                request.ItemsCount = request.Items.Count;
                request.TotalDebit = totalDebit;
                request.TotalCredit = totalCredit;

                if (newAttachment != null)
                {
                    request.Attachment = newAttachment;
                }

                var added = await journalDB.AddAsync(request);

                if (!added)
                {
                    response.AddError("Failed to create journal.");
                    response.Message = "Journal creation failed.";
                    return BadRequest(response);
                }

                var linked = await journalDB.AddJournalIdAsync(
                    trialBalance.Id,
                    request.Id
                );

                if (!linked)
                {
                    response.AddError(
                        "Journal was created, but linking it to the trial balance failed."
                    );

                    response.Message = "Journal created but linking failed.";
                    return BadRequest(response);
                }

                var recalculated = await db.RecalculateTotalsAsync(
                    trialBalance.Id
                );

                if (!recalculated)
                {
                    response.AddError(
                        "Journal was created, but trial balance totals could not be recalculated."
                    );

                    response.Message = "Journal created but recalculation failed.";
                    return BadRequest(response);
                }

                response.Status = true;
                response.Message = "Journal created successfully.";
                response.Result = request;

                return response;
            }

            // ============================================================
            // UPDATE JOURNAL
            // ============================================================

            if (!MongoDB.Bson.ObjectId.TryParse(journalId, out _))
            {
                response.AddError(
                    "Invalid journal ID. Use 0 to create a journal or a valid MongoDB ObjectId to update."
                );

                response.Message = "Invalid journal ID.";
                return BadRequest(response);
            }

            var existingJournal = await db.GetJournalByIdAsync(
                journalId,
                trialBalance.Id
            );

            if (existingJournal == null)
            {
                response.AddError(
                    journalDB.LastError != null
                        ? "Failed to retrieve journal."
                        : "Journal not found for this trial balance."
                );

                response.Message = "Unable to retrieve journal.";
                return BadRequest(response);
            }

            // Preserve the existing attachment if no replacement is supplied.

            var update = Builders<Journal>.Update
                .Set(x => x.Description, request.Description)
                .Set(x => x.JournalType, request.JournalType)
                .Set(x => x.Type, request.Type)
                .Set(x => x.JournalStatus, request.JournalStatus)
                .Set(x => x.ImportType, request.ImportType)
                .Set(x => x.CsvImportType, request.CsvImportType)
                .Set(x => x.IsActive, request.IsActive)
                .Set(x => x.Items, request.Items)
                .Set(x => x.ItemsCount, request.Items.Count)
                .Set(x => x.TotalDebit, totalDebit)
                .Set(x => x.TotalCredit, totalCredit);

            if (newAttachment != null)
            {
                update = update.Set(x => x.Attachment, newAttachment);
            }

            var updated = await journalDB.UpdateFieldsAsync(
                existingJournal.Id,
                update
            );

            if (!updated)
            {
                response.AddError("Failed to update journal.");
                response.Message = "Journal update failed.";
                return BadRequest(response);
            }

            var recalculatedTotals = await db.RecalculateTotalsAsync(
                trialBalance.Id
            );

            if (!recalculatedTotals)
            {
                response.AddError(
                    "Journal was updated, but trial balance totals could not be recalculated."
                );

                response.Message = "Journal updated but recalculation failed.";
                return BadRequest(response);
            }

            var updatedJournal = await journalDB.GetByIdAsync(journalId);

            if (updatedJournal == null)
            {
                response.AddError("Journal updated but could not be reloaded.");
                response.Message = "Journal updated, retrieval failed.";
                return BadRequest(response);
            }

            response.Status = true;
            response.Message = "Journal updated successfully.";
            response.Result = updatedJournal;

            return response;
        }
        catch (Exception ex)
        {
            response.AddError(ex.Message);
            response.Message = "Failed to create or update journal.";
            return StatusCode(500, response);
        }
    }

    #endregion

    #region Trial Balance PDF

    [HttpPost("{tbId}/posted-data/pdf")]
    public async Task<ActionResult<ApiResponse<object>>> DownloadPostedTrialBalancePdf(
        string tbId)
    {
        var response = new ApiResponse<object>();

        try
        {
            var db = GetDB();
            var chartAccountDB = GetChartAccountDB();

            // 1. Retrieve trial balance.
            var trialBalance = await db.GetByIdAsync(tbId);

            if (trialBalance == null)
            {
                response.AddError(
                     "Trial balance not found."
                );

                response.Message = "Unable to retrieve trial balance.";
                return BadRequest(response);
            }

            // 2. Retrieve only posted journals.
            var journals = await db.GetJournalsByTrialBalanceIdAsync(
                trialBalance.Id,
                onlyPosted: true
            );

            if (db.LastError != null)
            {
                response.AddError("Failed to retrieve posted journals.");
                // response.Message = "Failed to retrieve posted journals.";
                return BadRequest(response);
            }

            var journalItems = journals
                .Where(x => x.Items != null)
                .SelectMany(x => x.Items)
                .ToList();

            // 3. Group journal items by account code.
            var accountGroups = journalItems
                .Where(x => !string.IsNullOrWhiteSpace(x.AccountCode))
                .GroupBy(x => x.AccountCode)
                .ToList();

            var accountCodes = accountGroups
                .Select(x => x.Key)
                .ToList();

            // 4. Retrieve chart accounts through ChartAccountDB.
            var chartAccounts = accountCodes.Count == 0
                ? new List<ChartAccount>()
                : await chartAccountDB.GetAllAsync(
                    x => accountCodes.Contains(x.Code)
                );

            if (chartAccountDB.LastError != null)
            {
                response.AddError("Failed to retrieve chart accounts.");
                // response.Message = "Failed to retrieve chart accounts.";
                return BadRequest(response);
            }

            var accountMap = chartAccounts
                .GroupBy(x => x.Code)
                .ToDictionary(x => x.Key, x => x.First());

            // 5. Prepare account summaries.
            var accountSummaries = accountGroups
                .Select(group =>
                {
                    var entries = group.ToList();

                    accountMap.TryGetValue(group.Key, out var account);

                    return new
                    {
                        Code = group.Key,
                        AccountName =
                            account?.AccountName ??
                            entries.FirstOrDefault()?.AccountName ??
                            string.Empty,
                        Debit = entries.Sum(x => x.Debit),
                        Credit = entries.Sum(x => x.Credit)
                    };
                })
                .ToList();

            // 6. Generate PDF.
            byte[] pdfBytes = Document.Create(document =>
            {
                document.Page(page =>
                {
                    page.Size(PageSizes.A4);

                    page.MarginLeft(40);
                    page.MarginRight(40);
                    page.MarginTop(35);
                    page.MarginBottom(35);

                    page.DefaultTextStyle(x => x.FontSize(9));

                    page.Header()
                        .Column(column =>
                        {
                            column.Item()
                                .Text("client_name")
                                .FontSize(18)
                                .Bold();

                            column.Item()
                                .PaddingTop(4)
                                .Text("Trial Balance Report")
                                .FontSize(13)
                                .Bold();

                            column.Item()
                                .PaddingTop(8);

                            column.Item()
                                .LineHorizontal(1);
                        });

                    page.Content()
                        .PaddingTop(5)
                        .Table(table =>
                        {
                            table.ColumnsDefinition(columns =>
                            {
                                columns.ConstantColumn(55);
                                columns.RelativeColumn(4);
                                columns.RelativeColumn(2);
                                columns.RelativeColumn(2);
                            });

                            table.Header(header =>
                            {
                                header.Cell()
                                    .RowSpan(2)
                                    .Element(HeaderCell)
                                    .Text("Code");

                                header.Cell()
                                    .RowSpan(2)
                                    .Element(HeaderCell)
                                    .Text("Account Name");

                                header.Cell()
                                    .ColumnSpan(2)
                                    .Element(HeaderCell)
                                    .AlignCenter()
                                    .Text(
                                        $"{trialBalance.PeriodStart:dd/MM/yyyy} - " +
                                        $"{trialBalance.PeriodEnd:dd/MM/yyyy}"
                                    );

                                header.Cell()
                                    .Element(HeaderCell)
                                    .AlignRight()
                                    .Text("Debit");

                                header.Cell()
                                    .Element(HeaderCell)
                                    .AlignRight()
                                    .Text("Credit");
                            });

                            var totalDebit = accountSummaries.Sum(x => x.Debit);
                            var totalCredit = accountSummaries.Sum(x => x.Credit);

                            foreach (var account in accountSummaries)
                            {
                                table.Cell()
                                    .Element(DataCell)
                                    .Text(account.Code);

                                table.Cell()
                                    .Element(DataCell)
                                    .Text(account.AccountName);

                                table.Cell()
                                    .Element(DataCell)
                                    .AlignRight()
                                    .Text(
                                        account.Debit == 0
                                            ? "-"
                                            : $"£{account.Debit:N2}"
                                    );

                                table.Cell()
                                    .Element(DataCell)
                                    .AlignRight()
                                    .Text(
                                        account.Credit == 0
                                            ? "-"
                                            : $"£{account.Credit:N2}"
                                    );
                            }

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

                    page.Footer()
                        .AlignCenter()
                        .Text(
                            $"Generated on {DateTime.Now:dd/MM/yyyy HH:mm}"
                        );
                });
            }).GeneratePdf();

            // 7. Return PDF as Base64 inside ApiResponse.
            var year = trialBalance.PeriodEnd.Year;
            var fileName = $"client_name_trialbalance_{year}.pdf";

            response.Status = true;
            response.Message = "Trial balance PDF generated successfully.";
            response.Result = new
            {
                fileName,
                contentType = "application/pdf",
                fileData = Convert.ToBase64String(pdfBytes)
            };

            return response;
        }
        catch (Exception ex)
        {
            response.AddError(ex.Message);
            response.Message = "Failed to generate trial balance PDF.";
            return StatusCode(500, response);
        }

        static IContainer HeaderCell(IContainer container)
        {
            return container
                .Background("#EEEEEE")
                .Padding(5)
                .BorderBottom(1);
        }

        static IContainer DataCell(IContainer container)
        {
            return container
                .PaddingVertical(4)
                .PaddingHorizontal(5)
                .BorderBottom(0.5f);
        }

        static IContainer TotalCell(IContainer container)
        {
            return container
                .PaddingVertical(5)
                .PaddingHorizontal(5)
                .BorderTop(1);
        }
    }

    // }

    #endregion

    #region Trial Balance Posted JournalById

    [HttpGet("{RefNo}/journals/{journalId}")]
    public async Task<ActionResult<ApiResponse<object>>> GetById(
        string RefNo,
        string journalId)
    {
        var response = new ApiResponse<object>();

        try
        {
            var db = GetDB();
            var periodDB = GetAcctPeriodDB();
            var journalDB = GetJournalDB();

            // 1. Retrieve trial balance by RefNo or MongoDB ID.
            var tb = await db.GetByRefNoOrIdAsync(RefNo);

            if (tb == null)
            {
                response.AddError("Unable to retrieve trial balance."
                );
                // response.Message = "Unable to retrieve trial balance.";

                return BadRequest(response);

            }

            // 2. Retrieve accounting period.
            object? period = null;

            if (!string.IsNullOrWhiteSpace(tb.PeriodId))
            {
                var accountingPeriod =
                    await periodDB.GetByIdAsync(tb.PeriodId);

                if (periodDB.LastError != null)
                {
                    response.AddError("Failed to retrieve accounting period.");

                    return BadRequest(response);

                }

                period = accountingPeriod;
            }

            // Management trial balances may use custom dates.
            if (period == null &&
                (tb.PeriodStart != default ||
                 tb.PeriodEnd != default))
            {
                period = new
                {
                    id = string.Empty,
                    periodFrom = tb.PeriodStart,
                    periodTo = tb.PeriodEnd,
                    periodStart = tb.PeriodStart,
                    periodEnd = tb.PeriodEnd,
                    isActive = true,
                    isClosed = false
                };
            }

            // 3. Retrieve the requested journal, if any.
            Journal? journal = null;

            if (journalId != "0")
            {
                if (!ObjectId.TryParse(journalId, out _))
                {
                    response.AddError("Invalid journal ID.");
                    response.Message = "Invalid journal ID.";

                    return BadRequest(response);

                }

                journal =
                    await db.GetJournalByIdAsync(journalId);

                if (journal == null)
                {
                    response.AddError("Journal not found for this trial balance."
                    );

                    // response.Message = "Unable to retrieve journal.";
                    return BadRequest(response);
                }
            }

            var hasJournal = journal != null;

            var journalIds = hasJournal
                ? new List<string> { journal!.Id }
                : new List<string>();

            // 4. Build journal items.
            var items = hasJournal && journal!.Items != null
                ? journal.Items.Select(item => new
                {
                    journalId = journal.Id,
                    journalNumber = journal.Number,

                    account = new
                    {
                        id = string.Empty,
                        name = item.AccountName,
                        code = item.AccountCode
                    },

                    note = item.Note,

                    amount = item.Credit > 0
                        ? -item.Credit
                        : item.Debit,

                    debit = item.Debit,
                    credit = item.Credit,
                    type = item.Nature
                }).ToList()
                : new List<object>()
                    .Select(_ => new
                    {
                        journalId = "",
                        journalNumber = "",
                        account = new
                        {
                            id = "",
                            name = "",
                            code = ""
                        },
                        note = (string?)null,
                        amount = 0m,
                        debit = 0m,
                        credit = 0m,
                        type = default(AccountNature)
                    })
                    .ToList();

            var totalDebit = hasJournal
                ? journal!.TotalDebit
                : 0m;

            var totalCredit = hasJournal
                ? journal!.TotalCredit
                : 0m;

            var status = hasJournal
                ? (totalDebit == totalCredit
                    ? TrialBalanceStatus.Balanced
                    : TrialBalanceStatus.Unbalanced)
                : tb.Status;

            // 5. Build journal response.
            var journalResponse = hasJournal
                ? new[]
                {
                new
                {
                    id = journal!.Id,
                    number = journal.Number,

                    trialBalance = new
                    {
                        id = journal.TrialBalance.Id,
                        name = journal.TrialBalance.Name
                    },

                    type = journal.Type,
                    status = journal.Status,
                    description = journal.Description,
                    journalStatus = journal.JournalStatus,

                    itemsCount = journal.Items?.Count ?? 0,
                    totalDebit = journal.TotalDebit,
                    totalCredit = journal.TotalCredit,

                    importType = journal.ImportType,
                    csvImportType = journal.CsvImportType,
                    isActive = journal.IsActive,

                    periodStart = journal.PeriodStart,
                    periodEnd = journal.PeriodEnd,
                    createdAt = journal.CreatedAt,
                    createdBy = journal.CreatedBy
                }
                }
                : Array.Empty<object>();

            // 6. Return API response.
            response.Status = true;
            response.Message = "Journal details retrieved successfully.";

            response.Result = new
            {
                trialBalance = new
                {
                    name = tb.RefNo,
                    id = tb.Id
                },

                period,
                periodStart = tb.PeriodStart,
                periodEnd = tb.PeriodEnd,
                description = tb.Description,
                type = tb.Type,
                status,
                importType = tb.ImportType,
                csvImportType = tb.CsvImportType,

                items,
                itemsCount = items.Count,

                totalDebit,
                totalCredit,
                totalProfitLoss = tb.TotalProfitLoss,
                turnover = tb.Turnover,

                validation = new { },

                journalId = hasJournal ? journal!.Id : "0",
                journalIds,
                journals = journalResponse,

                importIds = tb.ImportIds ?? new List<string>(),

                attachments = journal?.Attachment == null
                        ? null
                        : new
                        {
                            name = journal.Attachment.Name,
                            path = journal.Attachment.Path
                        },

                csvFilePath = tb.CsvFilePath,
                id = tb.Id

            };

            return response;
        }
        catch (Exception ex)
        {
            response.AddError(ex.Message);
            response.Message = "Failed to retrieve journal details.";

            return StatusCode(500, response);


        }
    }
    #endregion  

}
//    #endregion  