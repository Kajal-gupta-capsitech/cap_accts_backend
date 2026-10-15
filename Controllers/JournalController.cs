using BackendAcctTask.Data.MongoDB;
using BackendAcctTask.Models;
using MongoDB.Bson;
using MongoDB.Driver;
using Microsoft.AspNetCore.Mvc;
namespace BackendAcctTask.Controllers;

[ApiController]
[Route("api/Journals")]
public class JournalsController : ApiControllerBase
{
    // private readonly IMongoCollection<Journal> _journals;
    // private readonly IMongoCollection<TrialBalance> _trialBalances;
    // private readonly IMongoCollection<ChartAccount> _chartAccounts;
    // private readonly IMongoCollection<AccountType> _accountTypes;
   private readonly IWebHostEnvironment _environment;
 
    public JournalsController(DBConfiguration dBconfiguration,IWebHostEnvironment env):base(dBconfiguration)
    {
       _environment = env;

    }

       private JournalDB GetDB() => new JournalDB(_dbConfig, _environment);
       private ChartAccountDB GetChartAccount() => new ChartAccountDB(_dbConfig);
       private AccountTypeDB GetAccountType() => new AccountTypeDB(_dbConfig);

   
    #region jounal delete
    [HttpDelete("{id}")]
    public async Task<ApiResponse<bool>> Delete(string id)
    {
        var response = new ApiResponse<bool>();

        try
        {
            var db = GetDB();
            var existing = await db.GetByIdAsync(id);
            if (existing == null)
            {
                response.AddError("Journals  not found.");
                return response;
            }

            var result = await db.DeleteAsync(id, softDelete: true);
            if (result)
            {
                response.Result = true;
                response.Message = "Journal deleted successfully.";
            }
            else
            {
                response.Message = "Failed to delete Journal.";
                if (db.LastError != null)
                    response.AddError(db.LastError.Message);
            }
        }
        catch (Exception ex)
        {
            response.AddError(ex.Message);
        }

        return response;
    }
    #endregion

}