using BackendAcctTask.Models;
using BackendAcctTask.Data.MongoDB;
using Microsoft.AspNetCore.Mvc;

namespace BackendAcctTask.Controllers
{
    [ApiController]
    [Route("api/ChartAccounts")]
    public class ChartAccountsController : ApiControllerBase
    {
        public ChartAccountsController(DBConfiguration dBConfiguration) : base(dBConfiguration)
        {
        }

        private ChartAccountDB GetDB()
        {
            return new ChartAccountDB(_dbConfig);
        }

        private AccountTypeDB GetAccountTypeDB()
        {
            return new AccountTypeDB(_dbConfig);
        }

        #region chart accounts list get
        [HttpGet]
        public async Task<List<ChartAccountResponse>> GetAll()
        {
            var db = GetDB();
            var accountTypeDb = GetAccountTypeDB();

            return await db.GetAllWithAccountTypeAsync(accountTypeDb);
        }
        #endregion

        #region chart account get by id
        [HttpGet("{id}")]
        public async Task<ActionResult<ChartAccountResponse>> GetById(string id)
        {
            try
            {
                var db = GetDB();
                var accountTypeDb = GetAccountTypeDB();

                var chartAccount = await db.GetByIdWithAccountTypeAsync(id, accountTypeDb);

                if (chartAccount == null)
                {
                    return NotFound(new ApiResponse<ChartAccountResponse>
                    {
                        Status = false,
                        Message = "Chart account not found."
                    });
                }

                return Ok(chartAccount);
            }
            catch (Exception ex)
            {
                var response = new ApiResponse<ChartAccountResponse>();
                response.AddError(ex.Message);
                return StatusCode(500, response);
            }
        }
        #endregion

        #region chart account add
        [HttpPost]
        public async Task<ApiResponse<ChartAccount>> Save([FromBody] ChartAccountRequest request)
        {
            var response = new ApiResponse<ChartAccount>();

            if (ValidateModel(response))
                return response;

            try
            {
                var db = GetDB();
                var accountTypeDb = GetAccountTypeDB();

                var accountType = await accountTypeDb.GetByIdAsync(request.AccountTypeId);
                if (accountType == null)
                {
                    response.AddError("Invalid AccountTypeId. Account type not found.");
                    return response;
                }

                if (await db.AccountNameExistsAsync(request.AccountName))
                {
                    response.AddError($"AccountName '{request.AccountName}' already exists.");
                    return response;
                }

                var nextCode = string.IsNullOrWhiteSpace(request.Code)
                    ? await db.GetNextCodeAsync()
                    : request.Code.Trim();

                var chartAccount = new ChartAccount
                {
                    Code = nextCode,
                    AccountName = request.AccountName.Trim(),
                    AccountTypeId = request.AccountTypeId.Trim(),
                    AccountGroup = request.AccountGroup?.Trim() ?? string.Empty,
                    ForClients = request.ForClients,
                    Archive = request.Archive
                };

                var result = await db.AddAsync(chartAccount);
                if (result)
                {
                    response.Result = chartAccount;
                    response.Message = "Chart account saved successfully.";
                }
                else
                {
                    response.Message = "Failed to save chart account.";
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

        #region chart account update
        [HttpPut("{id}")]
        [HttpPatch("{id}")]
        [HttpPost("update/{id}")]
        public async Task<ApiResponse<ChartAccount>> Update(string id, [FromBody] UpdateChartAccountRequest request)
        {
            var response = new ApiResponse<ChartAccount>();

            try
            {
                var db = GetDB();
                var accountTypeDb = GetAccountTypeDB();

                var existing = await db.GetByIdAsync(id);
                if (existing == null)
                {
                    response.AddError("Chart account not found.");
                    return response;
                }

                if (!string.IsNullOrWhiteSpace(request.AccountTypeId))
                {
                    var accountType = await accountTypeDb.GetByIdAsync(request.AccountTypeId);
                    if (accountType == null)
                    {
                        response.AddError("Invalid AccountTypeId. Account type not found.");
                        return response;
                    }
                    existing.AccountTypeId = request.AccountTypeId.Trim();
                }

                if (!string.IsNullOrWhiteSpace(request.AccountName))
                {
                    if (!string.Equals(existing.AccountName, request.AccountName, StringComparison.OrdinalIgnoreCase) &&
                        await db.AccountNameExistsAsync(request.AccountName))
                    {
                        response.AddError($"AccountName '{request.AccountName}' already exists.");
                        return response;
                    }
                    existing.AccountName = request.AccountName.Trim();
                }

                if (request.AccountGroup != null)
                    existing.AccountGroup = request.AccountGroup.Trim();

                if (request.ForClients.HasValue)
                    existing.ForClients = request.ForClients.Value;

                if (request.Archive.HasValue)
                    existing.Archive = request.Archive.Value;

                var result = await db.UpdateAsync(id, existing);
                if (result)
                {
                    response.Result = existing;
                    response.Message = "Chart account updated successfully.";
                }
                else
                {
                    response.Message = "Failed to update chart account.";
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

        #region chart account delete
        [HttpDelete("{id}")]
        [HttpPost("delete/{id}")]
        public async Task<ApiResponse<bool>> Delete(string id)
        {
            var response = new ApiResponse<bool>();

            try
            {
                var db = GetDB();
                var existing = await db.GetByIdAsync(id);
                if (existing == null)
                {
                    response.AddError("Chart account not found.");
                    return response;
                }

                var result = await db.DeleteAsync(id);
                if (result)
                {
                    response.Result = true;
                    response.Message = "Chart account deleted successfully.";
                }
                else
                {
                    response.Message = "Failed to delete chart account.";
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
}