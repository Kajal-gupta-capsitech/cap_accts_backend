using BackendAcctTask.Models;
using BackendAcctTask.Settings;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using MongoDB.Bson;
using MongoDB.Driver;

namespace BackendAcctTask.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AccountingPeriodsController : ControllerBase
{
    private readonly IMongoDatabase _database;

    public AccountingPeriodsController(
        IOptions<MongoDbSettings> mongoDbSettings)
    {
        var mongoClient = new MongoClient(
            mongoDbSettings.Value.ConnectionString);

        _database = mongoClient.GetDatabase(
            mongoDbSettings.Value.DatabaseName);
    }

    // GET: api/AccountingPeriods
    [HttpGet]
    public async Task<ActionResult<List<AccountingPeriod>>> Get()
    {
        var accountingPeriods =
            await AccountingPeriodDB.GetAllAsync(_database);

        return Ok(accountingPeriods);
    }

    // GET: api/AccountingPeriods/{id}
    [HttpGet("{id}")]
    public async Task<ActionResult<AccountingPeriod>> Get(string id)
    {
        var accountingPeriod =
            await AccountingPeriodDB.GetByIdAsync(_database, id);

        if (accountingPeriod == null)
        {
            return NotFound(new
            {
                status = false,
                message = "Accounting period not found."
            });
        }

        return Ok(accountingPeriod);
    }

    // POST: api/AccountingPeriods
    [HttpPost]
    public async Task<ActionResult<AccountingPeriod>> Create(
        AccountingPeriod accountingPeriod)
    {
        accountingPeriod.Id =
            ObjectId.GenerateNewId().ToString();

        await AccountingPeriodDB.CreateAsync(
            _database,
            accountingPeriod);

        return Ok(new
        {
            status = true,
            message = "Accounting period created successfully.",
            result = accountingPeriod
        });
    }

    // PUT/PATCH: api/AccountingPeriods/{id}
    [HttpPut("{id}")]
    [HttpPatch("{id}")]
    public async Task<IActionResult> Update(
        string id,
        UpdateAccountingPeriodRequest request)
    {
        var existingAccountingPeriod =
            await AccountingPeriodDB.GetByIdAsync(_database, id);

        if (existingAccountingPeriod == null)
        {
            return NotFound(new
            {
                status = false,
                message = "Accounting period not found."
            });
        }

        var updated =
            await AccountingPeriodDB.UpdateAsync(
                _database,
                id,
                request);

        if (!updated)
        {
            return BadRequest(new
            {
                status = false,
                message = "At least one field must be provided."
            });
        }

        return Ok(new
        {
            status = true,
            message = "Accounting period updated successfully."
        });
    }

    // DELETE: api/AccountingPeriods/{id}
    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(string id)
    {
        var existingAccountingPeriod =
            await AccountingPeriodDB.GetByIdAsync(_database, id);

        if (existingAccountingPeriod == null)
        {
            return NotFound(new
            {
                status = false,
                message = "Accounting period not found."
            });
        }

        await AccountingPeriodDB.DeleteAsync(_database, id);

        return Ok(new
        {
            status = true,
            message = "Accounting period deleted successfully."
        });
    }
}