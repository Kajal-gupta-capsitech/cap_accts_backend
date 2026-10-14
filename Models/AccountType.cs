using BackendAcctTask.Data.MongoDB;
using MongoDB.Bson.Serialization.Attributes;
using System.ComponentModel.DataAnnotations;

namespace BackendAcctTask.Models
{
    [BsonIgnoreExtraElements]
    public class AccountType : RecordBase
    {
        public string Name { get; set; } = string.Empty;

        public AccountNature Nature { get; set; }
    }

    public class AccountTypeRequest
    {
        [Required(ErrorMessage = "Name is required")]
        public string Name { get; set; } = string.Empty;

        [Required(ErrorMessage = "Nature is required")]
        public AccountNature Nature { get; set; }
    }

    public class AccountTypeResponse
    {
        public string Id { get; set; } = string.Empty;

        public string Name { get; set; } = string.Empty;

        public AccountNature Nature { get; set; }
    }

    public class AccountTypeDB : RecordDB<AccountType>
    {
        public AccountTypeDB(DBConfiguration dBConfiguration) : base(dBConfiguration)
        {
        }

        public override string CollectionName => "AccountTypes";
    }
}
