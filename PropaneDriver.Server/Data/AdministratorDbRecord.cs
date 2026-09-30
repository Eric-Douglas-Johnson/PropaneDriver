using System.ComponentModel.DataAnnotations;

namespace PropaneDriver.Server.Data
{
    // A row here is what makes a user an administrator; administrator-only columns belong here, not on UserDbRecord.
    public class AdministratorDbRecord
    {
        [Key]
        public Guid UserId { get; set; }
    }
}
