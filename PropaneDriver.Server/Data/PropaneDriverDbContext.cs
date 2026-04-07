using Microsoft.EntityFrameworkCore;

namespace PropaneDriver.Server.Data
{
    public class PropaneDriverDbContext : DbContext
    {
        public PropaneDriverDbContext(DbContextOptions<PropaneDriverDbContext> options)
            : base(options)
        {
        }

        public DbSet<DeliveryTimeEntity> DeliveryTimes => Set<DeliveryTimeEntity>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            modelBuilder.Entity<DeliveryTimeEntity>(entity =>
            {
                entity.ToTable("DeliveryTimes");
                entity.HasIndex(e => e.Address);
                entity.HasIndex(e => e.DeliveryId);
            });
        }
    }
}
