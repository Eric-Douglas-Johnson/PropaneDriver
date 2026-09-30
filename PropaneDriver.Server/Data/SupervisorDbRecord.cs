using System.ComponentModel.DataAnnotations;

namespace PropaneDriver.Server.Data
{
    // A row here is what makes a user a supervisor; supervisor-only columns belong here, not on UserDbRecord.
    public class SupervisorDbRecord
    {
        [Key]
        public Guid UserId { get; set; }
    }
}
