using Capsitech.Data.MongoDB;
using MongoDB.Bson.Serialization.Attributes;
using MongoDB.Driver;
using System.ComponentModel.DataAnnotations;

namespace BackendAcctTask.Models
{
    [BsonIgnoreExtraElements]
    public class ChartAccount : RecordBase
    {
        public string Code { get; set; } = string.Empty;

        public string AccountName { get; set; } = string.Empty;

        public string AccountTypeId { get; set; } = string.Empty;

        public string AccountGroup { get; set; } = string.Empty;

        public bool ForClients { get; set; }

        public bool Archive { get; set; }
    }

    public class ChartAccountRequest
    {
        public string? Code { get; set; }

        [Required(ErrorMessage = "Account Name is required")]
        public string AccountName { get; set; } = string.Empty;

        [Required(ErrorMessage = "Account Type Id is required")]
        public string AccountTypeId { get; set; } = string.Empty;

        public string AccountGroup { get; set; } = string.Empty;

        public bool ForClients { get; set; }

        public bool Archive { get; set; }
    }

    public class ChartAccountDB : RecordDB<ChartAccount>
    {
        public ChartAccountDB(DBConfiguration dBConfiguration) : base(dBConfiguration)
        {
        }

        public override string CollectionName => "ChartAccounts";

        public async Task<bool> AccountNameExistsAsync(string accountName)
        {
            return await ExistsAsync(x => x.AccountName.ToLower() == accountName.ToLower() && !x.IsDeleted);
        }

        public async Task<string> GetNextCodeAsync()
        {
            try
            {
                var lastChartAccount = await GetCollection()
                    .Find(x => !x.IsDeleted)
                    .SortByDescending(x => x.Code)
                    .FirstOrDefaultAsync();

                if (lastChartAccount == null || string.IsNullOrWhiteSpace(lastChartAccount.Code))
                {
                    return "1/1";
                }

                var codeParts = lastChartAccount.Code.Split('/');

                if (codeParts.Length != 2 || !int.TryParse(codeParts[1], out int lastNumber))
                {
                    return "1/1";
                }

                return $"1/{lastNumber + 1}";
            }
            catch (Exception ex)
            {
                LastError = ex;
                return "1/1";
            }
        }

        public async Task<List<ChartAccountResponse>> GetAllWithAccountTypeAsync(AccountTypeDB accountTypeDb)
        {
            var chartAccounts = await GetAllAsync(x => !x.IsDeleted);

            if (chartAccounts.Count == 0)
            {
                return new List<ChartAccountResponse>();
            }

            var accountTypeIds = chartAccounts
                .Select(x => x.AccountTypeId)
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Distinct()
                .ToList();

            var accountTypes = accountTypeIds.Count == 0
                ? new List<AccountType>()
                : await accountTypeDb.GetAllAsync(x => accountTypeIds.Contains(x.Id) && !x.IsDeleted);

            var accountTypesById = accountTypes
                .ToDictionary(x => x.Id, StringComparer.Ordinal);

            return chartAccounts
                .Select(chartAccount => new ChartAccountResponse
                {
                    Id = chartAccount.Id,
                    Code = chartAccount.Code,
                    AccountName = chartAccount.AccountName,
                    AccountTypeId = chartAccount.AccountTypeId,
                    AccountType = accountTypesById.TryGetValue(chartAccount.AccountTypeId, out var accountType)
                        ? accountType
                        : null,
                    AccountGroup = chartAccount.AccountGroup,
                    ForClients = chartAccount.ForClients,
                    Archive = chartAccount.Archive
                })
                .ToList();
        }

        public async Task<ChartAccountResponse?> GetByIdWithAccountTypeAsync(string id, AccountTypeDB accountTypeDb)
        {
            var chartAccount = await GetByIdAsync(id);

            if (chartAccount == null)
            {
                return null;
            }

            var accountType = !string.IsNullOrWhiteSpace(chartAccount.AccountTypeId)
                ? await accountTypeDb.GetByIdAsync(chartAccount.AccountTypeId)
                : null;

            return new ChartAccountResponse
            {
                Id = chartAccount.Id,
                Code = chartAccount.Code,
                AccountName = chartAccount.AccountName,
                AccountTypeId = chartAccount.AccountTypeId,
                AccountType = accountType,
                AccountGroup = chartAccount.AccountGroup,
                ForClients = chartAccount.ForClients,
                Archive = chartAccount.Archive
            };
        }
    }
}