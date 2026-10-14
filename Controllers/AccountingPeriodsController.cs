using BackendAcctTask.Models;
using BackendAcctTask.Data.MongoDB;
using Microsoft.AspNetCore.Mvc;

namespace BackendAcctTask.Controllers
{
    [ApiController]
    [Route("api/AccountingPeriods")]
    public class AccountingPeriodsController : ApiControllerBase
    {
        public AccountingPeriodsController(DBConfiguration dBConfiguration) : base(dBConfiguration)
        {
        }

        private AccountingPeriodDB GetDB()
        {
            return new AccountingPeriodDB(_dbConfig);
        }

        #region period list get
        [HttpGet]
                // [HttpGet("list")]
        public async Task<List<AccountingPeriod>> GetAll()
        {
            var db = GetDB();
            return await db.GetAllAsync(x => true);
        }
        #endregion

        #region period get by id
        [HttpGet("{id}")]
        public async Task<ActionResult<AccountingPeriod>> GetById(string id)
        {
            try
            {
                var db = GetDB();
                var period = await db.GetByIdAsync(id);

                if (period == null)
                {
                    return NotFound(new ApiResponse<AccountingPeriod>
                    {
                        Status = false,
                        Message = "Accounting period not found."
                    });
                }

                return Ok(period);
            }
            catch (Exception ex)
            {
                var errorResponse = new ApiResponse<AccountingPeriod>();
                errorResponse.AddError(ex.Message);
                return StatusCode(500, errorResponse);
            }
        }
        #endregion

        #region period add / save
        [HttpPost]
        [HttpPost("save")]
        public async Task<ApiResponse<AccountingPeriod>> Save([FromBody] AccountingPeriodRequest request)
        {
            var response = new ApiResponse<AccountingPeriod>();

            if (ValidateModel(response))
                return response;

            if (request.PeriodFrom >= request.PeriodTo)
            {
                response.AddError("Period From must be earlier than Period To.");
                return response;
            }

            try
            {
                var db = GetDB();

                var existing = await db.GetAllAsync(x => x.PeriodFrom == request.PeriodFrom && x.PeriodTo == request.PeriodTo);
                if (existing.Any())
                {
                    response.AddError("Accounting period with the same dates already exists.");
                    return response;
                }

                var period = new AccountingPeriod
                {
                    PeriodFrom = request.PeriodFrom,
                    PeriodTo = request.PeriodTo,
                    Status = request.Status ?? AccountingPeriodStatus.Unlocked,
                    IsActive = request.IsActive ?? true,
                    IsClosed = request.IsClosed ?? false
                };

                var result = await db.AddAsync(period);
                if (result)
                {
                    response.Result = period;
                    response.Message = "Accounting period saved successfully.";
                }
                else
                {
                    response.Message = "Failed to save accounting period";
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

        #region period update
        [HttpPut("{id}")]
        [HttpPatch("{id}")]
        [HttpPost("update/{id}")]
        public async Task<ApiResponse<AccountingPeriod>> Update(string id, [FromBody] UpdateAccountingPeriodRequest request)
        {
            var response = new ApiResponse<AccountingPeriod>();

            try
            {
                var db = GetDB();
                var existing = await db.GetByIdAsync(id);
                if (existing == null)
                {
                    response.AddError("Accounting period not found.");
                    return response;
                }

                if (request.PeriodFrom.HasValue)
                    existing.PeriodFrom = request.PeriodFrom.Value;

                if (request.PeriodTo.HasValue)
                    existing.PeriodTo = request.PeriodTo.Value;

                if (request.IsActive.HasValue)
                    existing.IsActive = request.IsActive.Value;

                if (request.IsClosed.HasValue)
                    existing.IsClosed = request.IsClosed.Value;

                if (request.Status.HasValue)
                    existing.Status = request.Status.Value;

                if (existing.PeriodFrom >= existing.PeriodTo)
                {
                    response.AddError("Period From must be earlier than Period To.");
                    return response;
                }

                var result = await db.UpdateAsync(id, existing);
                if (result)
                {
                    response.Result = existing;
                    response.Message = "Accounting period updated successfully.";
                }
                else
                {
                    response.Message = "Failed to update accounting period.";
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

        #region period delete
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
                    response.AddError("Accounting period not found.");
                    return response;
                }

                var result = await db.DeleteAsync(id);
                if (result)
                {
                    response.Result = true;
                    response.Message = "Accounting period deleted successfully.";
                }
                else
                {
                    response.Message = "Failed to delete accounting period.";
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