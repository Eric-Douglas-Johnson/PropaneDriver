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
        public DbSet<DriverEntity> Drivers => Set<DriverEntity>();
        public DbSet<PasswordResetTokenEntity> PasswordResetTokens => Set<PasswordResetTokenEntity>();
        public DbSet<ErrorLogEntity> ErrorLogs => Set<ErrorLogEntity>();
        public DbSet<DeliveryStatusEntity> DeliveryStatuses => Set<DeliveryStatusEntity>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            modelBuilder.Entity<DeliveryTimeEntity>(entity =>
            {
                entity.ToTable("DeliveryTimes");
                entity.HasIndex(e => e.Address);
                entity.HasIndex(e => e.DeliveryId);
            });

            modelBuilder.Entity<DriverEntity>(entity =>
            {
                entity.ToTable("Drivers");
                entity.HasIndex(e => e.UserName).IsUnique();
                entity.HasIndex(e => e.Email);
            });

            modelBuilder.Entity<PasswordResetTokenEntity>(entity =>
            {
                entity.ToTable("PasswordResetTokens");
                entity.HasIndex(e => e.DriverId);
                entity.HasIndex(e => e.TokenHash);
            });

            modelBuilder.Entity<ErrorLogEntity>(entity =>
            {
                entity.ToTable("ErrorLog");
                entity.HasIndex(e => e.Source);
                entity.HasIndex(e => e.Timestamp);
            });

            modelBuilder.Entity<DeliveryStatusEntity>(entity =>
            {
                entity.ToTable("DeliveryStatus");
                entity.HasKey(e => e.DeliveryId);
                entity.Property(e => e.DeliveryId).HasMaxLength(100);
                entity.HasIndex(e => e.UpdatedAt);
            });
        }
    }
}
