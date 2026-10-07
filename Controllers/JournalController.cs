using BackendAcctTask.Models;
using Microsoft.AspNetCore.Mvc;
using MongoDB.Bson;
using MongoDB.Driver;

namespace BackendAcctTask.Controllers;

[ApiController]
[Route("api/Journals")]
public class JournalsController : ControllerBase
{
    private readonly IMongoCollection<Journal> _journals;
    private readonly IMongoCollection<TrialBalance> _trialBalances;
    private readonly IMongoCollection<ChartAccount> _chartAccounts;
    private readonly IMongoCollection<AccountType> _accountTypes;

    public JournalsController(IMongoDatabase database)
    {
        _journals =
            database.GetCollection<Journal>("Journals");

        _trialBalances =
            database.GetCollection<TrialBalance>("TrialBalances");

        _chartAccounts =
            database.GetCollection<ChartAccount>("ChartAccounts");

        _accountTypes =
            database.GetCollection<AccountType>("AccountTypes");
    }


    // ============================================================
    // GENERATE JOURNAL NUMBER
    //
    // Example:
    // TB-21-J01
    // TB-21-J02
    // TB-21-J03
    // ============================================================

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
            !string.IsNullOrWhiteSpace(latest.Number)
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


    // ============================================================
    // CREATE JOURNAL
    //
    // POST
    // /api/Journals
    // ============================================================

    [HttpPost]
    public async Task<IActionResult> Create(
        [FromBody] Journal request)
    {
        // ========================================================
        // VALIDATE REQUEST
        // ========================================================

        if (request == null)
        {
            return BadRequest(new
            {
                status = false,
                message = "Journal request is required."
            });
        }


        // ========================================================
        // VALIDATE TRIAL BALANCE REFERENCE
        // ========================================================

        if (
            request.TrialBalance == null ||
            string.IsNullOrWhiteSpace(
                request.TrialBalance.Id
            )
        )
        {
            return BadRequest(new
            {
                status = false,
                message =
                    "TrialBalance.Id is required."
            });
        }


        // ========================================================
        // GET TRIAL BALANCE
        // ========================================================

        var trialBalance =
            await _trialBalances
                .Find(x =>
                    x.Id ==
                    request.TrialBalance.Id
                )
                .FirstOrDefaultAsync();


        if (trialBalance == null)
        {
            return NotFound(new
            {
                status = false,
                message = "Trial balance not found."
            });
        }


        // ========================================================
        // VALIDATE ITEMS
        // ========================================================

        if (
            request.Items == null ||
            request.Items.Count == 0
        )
        {
            return BadRequest(new
            {
                status = false,
                message =
                    "Journal must contain at least one item."
            });
        }


        // ========================================================
        // VALIDATE AND RESOLVE CHART ACCOUNTS
        // ========================================================

        foreach (var item in request.Items)
        {
            if (
                string.IsNullOrWhiteSpace(
                    item.AccountCode
                )
            )
            {
                return BadRequest(new
                {
                    status = false,
                    message = "Account code is required."
                });
            }


            // ----------------------------------------------------
            // FIND CHART ACCOUNT
            // ----------------------------------------------------

            var account =
                await _chartAccounts
                    .Find(x =>
                        x.Code ==
                        item.AccountCode
                    )
                    .FirstOrDefaultAsync();


            if (account == null)
            {
                return BadRequest(new
                {
                    status = false,
                    message =
                        $"Chart account not found: {item.AccountCode}"
                });
            }


            // ----------------------------------------------------
            // FIND ACCOUNT TYPE
            // ----------------------------------------------------

            var accountType =
                await _accountTypes
                    .Find(x =>
                        x.Id ==
                        account.AccountTypeId
                    )
                    .FirstOrDefaultAsync();


            if (accountType == null)
            {
                return BadRequest(new
                {
                    status = false,
                    message =
                        $"Account type not found for account: {item.AccountCode}"
                });
            }


            // ----------------------------------------------------
            // RESOLVE ACCOUNT INFORMATION
            // ----------------------------------------------------

            item.AccountName =
                account.AccountName;

            item.Nature =
                accountType.Nature;
        }


        // ========================================================
        // CALCULATE JOURNAL TOTALS
        // ========================================================

        request.TotalDebit =
            request.Items.Sum(
                x => x.Debit
            );

        request.TotalCredit =
            request.Items.Sum(
                x => x.Credit
            );

        request.ItemsCount =
            request.Items.Count;


        // ========================================================
        // JOURNAL STATUS
        // ========================================================

        request.Status =
            request.TotalDebit ==
            request.TotalCredit

                ? TrialBalanceStatus.Balanced

                : TrialBalanceStatus.Unbalanced;


        // ========================================================
        // GENERATE JOURNAL NUMBER
        // ========================================================

        request.Number =
            await GenerateJournalNumber(
                trialBalance.Id,
                trialBalance.RefNo
            );


        // ========================================================
        // GENERATE JOURNAL ID
        // ========================================================

        request.Id =
            ObjectId.GenerateNewId()
                .ToString();


        // ========================================================
        // COPY TRIAL BALANCE REFERENCE
        // ========================================================
        //
        // Journal stores:
        //
        // TrialBalance = {
        //     Id   = TrialBalance.Id,
        //     Name = TrialBalance.RefNo
        // }
        //
        // ========================================================

        request.TrialBalance =
            new TrialBalanceReference
            {
                Id = trialBalance.Id,
                Name = trialBalance.RefNo
            };


        // ========================================================
        // COPY TRIAL BALANCE METADATA
        // ========================================================

        request.Type =
            trialBalance.Type;

        request.ImportType =
            trialBalance.ImportType;

        request.CsvImportType =
            trialBalance.CsvImportType;


        // ========================================================
        // CREATED DATE
        // ========================================================

        request.CreatedAt =
            DateTime.UtcNow;


        // ========================================================
        // SAVE JOURNAL
        // ========================================================

        await _journals
            .InsertOneAsync(request);


        // ========================================================
        // ADD JOURNAL ID TO TRIAL BALANCE
        //
        // One Trial Balance can contain MANY journals.
        // ========================================================

        if (trialBalance.JournalIds == null)
        {
            trialBalance.JournalIds =
                new List<string>();
        }


        trialBalance.JournalIds.Add(
            request.Id
        );


        // ========================================================
        // RECALCULATE TRIAL BALANCE TOTALS
        //
        // IMPORTANT:
        //
        // TrialBalance does NOT contain Items anymore.
        //
        // Totals come from Journal records.
        // ========================================================

        var allJournalIds =
            trialBalance.JournalIds;


        var journals =
            await _journals
                .Find(x =>
                    allJournalIds.Contains(x.Id)
                )
                .Project(x => new
                {
                    x.TotalDebit,
                    x.TotalCredit
                })
                .ToListAsync();


        trialBalance.TotalDebit =
            journals.Sum(
                x => x.TotalDebit
            );


        trialBalance.TotalCredit =
            journals.Sum(
                x => x.TotalCredit
            );


        trialBalance.Status =
            trialBalance.TotalDebit ==
            trialBalance.TotalCredit

                ? TrialBalanceStatus.Balanced

                : TrialBalanceStatus.Unbalanced;


        // ========================================================
        // SAVE UPDATED TRIAL BALANCE
        // ========================================================

        await _trialBalances
            .ReplaceOneAsync(
                x =>
                    x.Id ==
                    trialBalance.Id,

                trialBalance
            );


        // ========================================================
        // RESPONSE
        // ========================================================

        return Ok(new
        {
            status = true,

            message =
                "Journal created successfully.",

            result = new
            {
                journal = request,

                trialBalance = new
                {
                    id =
                        trialBalance.Id,

                    name =
                        trialBalance.RefNo,

                    journalIds =
                        trialBalance.JournalIds,

                    totalDebit =
                        trialBalance.TotalDebit,

                    totalCredit =
                        trialBalance.TotalCredit,

                    status =
                        trialBalance.Status
                }
            }
        });
    }



}