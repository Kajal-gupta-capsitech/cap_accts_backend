namespace Capsitech.Data.MongoDB
{
    public class ApiResponse<T>
    {
        public bool Status { get; set; } = true;

        public string Message { get; set; } = string.Empty;

        public T? Result { get; set; }

        public List<string> Errors { get; set; } = new();

        public void AddError(string error)
        {
            Errors.Add(error);
            Status = false;
        }

        public void AddErrors(IEnumerable<string> errors)
        {
            Errors.AddRange(errors);
            Status = false;
        }
    }

    public class ApiResponse : ApiResponse<object>
    {
    }
}
