using Capsitech.Data.MongoDB;
using MongoDB.Bson.Serialization.Attributes;
using System.ComponentModel.DataAnnotations;

namespace BackendAcctTask.Models
{
    public enum AccountingPeriodStatus
    {
        Unlocked = 1,
        Locked = 2
    }

    [BsonIgnoreExtraElements]
    public class AccountingPeriod : RecordBase
    {
        public DateTime PeriodFrom { get; set; }

        public DateTime PeriodTo { get; set; }

        public AccountingPeriodStatus Status { get; set; } = AccountingPeriodStatus.Unlocked;

        public bool IsActive { get; set; } = true;

        public bool IsClosed { get; set; } = false;
    }

    public class AccountingPeriodRequest
    {
        [Required(ErrorMessage = "Period From is required")]
        public DateTime PeriodFrom { get; set; }

        [Required(ErrorMessage = "Period To is required")]
        public DateTime PeriodTo { get; set; }

        public AccountingPeriodStatus? Status { get; set; }

        public bool? IsActive { get; set; }

        public bool? IsClosed { get; set; }
    }

    public class AccountingPeriodResponse
    {
        public string Id { get; set; } = null!;

        public DateTime PeriodFrom { get; set; }

        public DateTime PeriodTo { get; set; }

        public AccountingPeriodStatus Status { get; set; }

        public bool IsActive { get; set; } = true;

        public bool IsClosed { get; set; } = false;

        public List<DirectorResponse> Directors { get; set; } = new();
    }

    public class DirectorResponse
    {
        public string Id { get; set; } = null!;

        public string Name { get; set; } = null!;
    }

    public class AccountingPeriodDB : RecordDB<AccountingPeriod>
    {
        public AccountingPeriodDB(DBConfiguration dBConfiguration) : base(dBConfiguration)
        {
        }

        public override string CollectionName => "AccountingPeriods";
    }
}