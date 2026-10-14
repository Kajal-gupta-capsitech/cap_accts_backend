using BackendAcctTask.Models;
using BackendAcctTask.Data.MongoDB;
using Microsoft.AspNetCore.Mvc;

namespace BackendAcctTask.Controllers
{
    [ApiController]
    [Route("api/AccountTypes")]
    public class AccountTypesController : ApiControllerBase
    {
        public AccountTypesController(DBConfiguration dBConfiguration) : base(dBConfiguration)
        {
        }

        private AccountTypeDB GetDB()
        {
            return new AccountTypeDB(_dbConfig);
        }

        #region account type list get
        [HttpGet]
        public async Task<List<AccountType>> GetAll()
        {
            var db = GetDB();
            return await db.GetAllAsync(x => true);
        }
        #endregion

        #region account type get by id
        [HttpGet("{id}")]
        public async Task<ActionResult<AccountType>> GetById(string id)
        {
            try
            {
                var db = GetDB();
                var accountType = await db.GetByIdAsync(id);

                if (accountType == null)
                {
                    return NotFound(new ApiResponse<AccountType>
                    {
                        Status = false,
                        Message = "Account type not found."
                    });
                }

                return Ok(accountType);
            }
            catch (Exception ex)
            {
                var response = new ApiResponse<AccountType>();
                response.AddError(ex.Message);
                return StatusCode(500, response);
            }
        }
        #endregion

        #region account type add / save
        [HttpPost]
        [HttpPost("save")]
        public async Task<ApiResponse<AccountType>> Save([FromBody] AccountTypeRequest request)
        {
            var response = new ApiResponse<AccountType>();

            if (ValidateModel(response))
                return response;

            try
            {
                var db = GetDB();

                var existing = await db.GetAllAsync(x => x.Name.ToLower() == request.Name.ToLower());
                if (existing.Any())
                {
                    response.AddError($"Account type '{request.Name}' already exists.");
                    return response;
                }

                var accountType = new AccountType
                {
                    Name = request.Name.Trim(),
                    Nature = request.Nature
                };

                var result = await db.AddAsync(accountType);
                if (result)
                {
                    response.Result = accountType;
                    response.Message = "Account type saved successfully.";
                }
                else
                {
                    response.Message = "Failed to save account type.";
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

        #region account type update
        [HttpPut("{id}")]
        [HttpPatch("{id}")]
        [HttpPost("update/{id}")]
        public async Task<ApiResponse<AccountType>> Update(string id, [FromBody] AccountTypeRequest request)
        {
            var response = new ApiResponse<AccountType>();

            if (ValidateModel(response))
                return response;

            try
            {
                var db = GetDB();
                var existing = await db.GetByIdAsync(id);
                if (existing == null)
                {
                    response.AddError("Account type not found.");
                    return response;
                }

                existing.Name = request.Name.Trim();
                existing.Nature = request.Nature;

                var result = await db.UpdateAsync(id, existing);
                if (result)
                {
                    response.Result = existing;
                    response.Message = "Account type updated successfully.";
                }
                else
                {
                    response.Message = "Failed to update account type.";
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

        #region account type delete
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
                    response.AddError("Account type not found.");
                    return response;
                }

                var result = await db.DeleteAsync(id);
                if (result)
                {
                    response.Result = true;
                    response.Message = "Account type deleted successfully.";
                }
                else
                {
                    response.Message = "Failed to delete account type.";
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