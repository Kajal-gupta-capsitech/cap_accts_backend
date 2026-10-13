using Microsoft.AspNetCore.Mvc;
using MongoDB.Driver;

namespace BackendAcctTask.Controllers;

[ApiController]
[Route("api/[controller]")]
public abstract class BaseController : ControllerBase
{
    protected readonly IMongoDatabase _database;

    protected BaseController(IMongoDatabase database)
    {
        _database = database;
    }
}