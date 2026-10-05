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

    private readonly IMongoCollection<TrialBalance>
        _trialBalances;

    private readonly IMongoCollection<ChartAccount>
        _chartAccounts;

    private readonly IMongoCollection<AccountType>
        _accountTypes;

            private List<TrialBalanceItem> BuildTrialBalanceItems(
            List<JournalItem> journalItems)
        {
            return journalItems
                .GroupBy(x => x.AccountCode)
                .Select(group =>
                {
                    var first = group.First();

                    return new TrialBalanceItem
                    {
                        AccountCode = first.AccountCode,
                        AccountName = first.AccountName,
                        Nature = first.Nature,
                        Debit = group.Sum(x => x.Debit),
                        Credit = group.Sum(x => x.Credit),
                        Note = first.Note
                    };
                })
                .ToList();
        }


    public JournalsController(
        IMongoDatabase database)
    {
        _journals =
            database.GetCollection<Journal>(
                "Journals"
            );

        _trialBalances =
            database.GetCollection<TrialBalance>(
                "TrialBalances"
            );

        _chartAccounts =
            database.GetCollection<ChartAccount>(
                "ChartAccounts"
            );

        _accountTypes =
            database.GetCollection<AccountType>(
                "AccountTypes"
            );
    }


    // ============================================================
    // GENERATE JOURNAL NUMBER
    //
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
                    x.TrialBalanceId ==
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


    // ============================================================
    // BUILD TRIAL BALANCE ITEMS
    //
    // Combines ALL journal items belonging
    // to the Trial Balance.
    // ============================================================

    private List<TrialBalanceItem>
        BuildTrialBalanceItems(
            List<Journal> journals)
    {
        var journalItems =
            journals
                .SelectMany(x => x.Items)
                .ToList();

        return journalItems
            .GroupBy(x => x.AccountCode)
            .Select(group =>
            {
                var first =
                    group.First();

                return new TrialBalanceItem
                {
                    AccountCode =
                        first.AccountCode,

                    AccountName =
                        first.AccountName,

                    Nature =
                        first.Nature,

                    Debit =
                        group.Sum(
                            x => x.Debit
                        ),

                    Credit =
                        group.Sum(
                            x => x.Credit
                        ),

                    Note =
                        first.Note
                };
            })
            .ToList();
    }


    // ============================================================
    // REFRESH TRIAL BALANCE FROM ALL JOURNALS
    // ============================================================

    private async Task
        RefreshTrialBalanceFromJournals(
            TrialBalance trialBalance)
    {
        var journalIds =
            trialBalance.JournalIds ??
            new List<string>();

        if (journalIds.Count == 0)
        {
            trialBalance.Items =
                new List<TrialBalanceItem>();

            trialBalance.TotalDebit = 0;
            trialBalance.TotalCredit = 0;

            trialBalance.Status =
                TrialBalanceStatus.Unbalanced;

            return;
        }


        var journals =
            await _journals
                .Find(x =>
                    journalIds.Contains(x.Id)
                )
                .ToListAsync();


        trialBalance.Items =
            BuildTrialBalanceItems(
                journals
            );


        trialBalance.TotalDebit =
            trialBalance.Items.Sum(
                x => x.Debit
            );


        trialBalance.TotalCredit =
            trialBalance.Items.Sum(
                x => x.Credit
            );


        trialBalance.Status =
            trialBalance.TotalDebit ==
            trialBalance.TotalCredit
                ? TrialBalanceStatus.Balanced
                : TrialBalanceStatus.Unbalanced;
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
        // VALIDATE TRIAL BALANCE ID
        // ========================================================

        if (
            string.IsNullOrWhiteSpace(
                request.TrialBalanceId
            )
        )
        {
            return BadRequest(new
            {
                status = false,
                message =
                    "TrialBalanceId is required."
            });
        }


        // ========================================================
        // GET TRIAL BALANCE
        // ========================================================

        var trialBalance =
            await _trialBalances
                .Find(x =>
                    x.Id ==
                    request.TrialBalanceId
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
        // VALIDATE CHART ACCOUNTS
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
                    message =
                        "Account code is required."
                });
            }


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


            // ====================================================
            // RESOLVE ACCOUNT TYPE
            // ====================================================

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


            // ====================================================
            // FILL ACCOUNT INFORMATION
            // ====================================================

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
        // GENERATE ID
        // ========================================================

        request.Id =
            ObjectId.GenerateNewId()
                .ToString();


        // ========================================================
        // COPY TRIAL BALANCE TYPE
        // ========================================================

        request.Type =
            trialBalance.Type;


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
        // IMPORTANT:
        // One Trial Balance can have MANY journals.
        // ========================================================

        if (
            trialBalance.JournalIds == null
        )
        {
            trialBalance.JournalIds =
                new List<string>();
        }


        trialBalance.JournalIds
            .Add(request.Id);


        // ========================================================
        // RECALCULATE TRIAL BALANCE
        //
        // Uses ALL journals, not only the
        // newly-created journal.
        // ========================================================

        await RefreshTrialBalanceFromJournals(
            trialBalance
        );


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