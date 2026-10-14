using Microsoft.AspNetCore.Mvc;

namespace Capsitech.Data.MongoDB
{
    [ApiController]
    public abstract class ApiControllerBase : ControllerBase
    {
        protected readonly DBConfiguration _dbConfig;

        protected ApiControllerBase(DBConfiguration dbConfiguration)
        {
            _dbConfig = dbConfiguration;
        }

        protected bool ValidateModel<T>(ApiResponse<T> response)
        {
            if (!ModelState.IsValid)
            {
                response.Status = false;
                response.Message = "Validation failed.";
                foreach (var state in ModelState.Values)
                {
                    foreach (var error in state.Errors)
                    {
                        if (!string.IsNullOrWhiteSpace(error.ErrorMessage))
                        {
                            response.AddError(error.ErrorMessage);
                        }
                        else if (error.Exception != null)
                        {
                            response.AddError(error.Exception.Message);
                        }
                    }
                }
                return true;
            }
            return false;
        }
    }
}
