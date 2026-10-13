C#
using BackendAcctTask.Models;
using Microsoft.AspNetCore.Mvc;
using MongoDB.Bson;
using MongoDB.Driver;

namespace BackendAcctTask.Controllers;

public class AccountTypesController : BaseController
{
    // BaseController ko IMongoDatabase pass kar rahe hain
    public AccountTypesController(IMongoDatabase database) : base(database)
    {
    }
    // GET: api/AccountTypes
    [HttpGet]
    public async Task<ActionResult<List<AccountType>>> Get()
    {
        var accountTypes = await AccountTypeDB.GetAllAsync(_database);

        return Ok(accountTypes);
    }

    // GET: api/AccountTypes/{id}
    [HttpGet("{id}")]
    public async Task<ActionResult<AccountType>> Get(string id)
    {
        var accountType = await AccountTypeDB.GetByIdAsync(_database, id);

        if (accountType == null)
        {
            return NotFound();
        }

        return Ok(accountType);
    }

    // POST: api/AccountTypes
    [HttpPost]
    public async Task<ActionResult<AccountType>> Create(
        AccountType accountType)
    {
        accountType.Id = ObjectId.GenerateNewId().ToString();

        await AccountTypeDB.CreateAsync(_database, accountType);

        return CreatedAtAction(
            nameof(Get),
            new { id = accountType.Id },
            accountType);
    }

    // PUT: api/AccountTypes/{id}
    [HttpPut("{id}")]
    public async Task<IActionResult> Update(
        string id,
        AccountType accountType)
    {
        var existingAccountType =
            await AccountTypeDB.GetByIdAsync(_database, id);

        if (existingAccountType == null)
        {
            return NotFound();
        }

        accountType.Id = id;

        await AccountTypeDB.UpdateAsync(_database, id, accountType);

        return NoContent();
    }

    // DELETE: api/AccountTypes/{id}
    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(string id)
    {
        var existingAccountType =
            await AccountTypeDB.GetByIdAsync(_database, id);

        if (existingAccountType == null)
        {
            return NotFound();
        }

        await AccountTypeDB.DeleteAsync(_database, id);

        return NoContent();
    }
}