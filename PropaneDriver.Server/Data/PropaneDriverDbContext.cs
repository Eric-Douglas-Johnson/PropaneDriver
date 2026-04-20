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
        public DbSet<RouteEntity> Routes => Set<RouteEntity>();
        public DbSet<DeliveryEntity> Deliveries => Set<DeliveryEntity>();
        public DbSet<AlertEntity> Alerts => Set<AlertEntity>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            modelBuilder.Entity<DeliveryTimeEntity>(entity =>
            {
                entity.ToTable("DeliveryTimes");
                entity.HasIndex(e => new { e.Street, e.City, e.State, e.ZipCode });
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

            modelBuilder.Entity<RouteEntity>(entity =>
            {
                entity.ToTable("Routes");
                entity.HasIndex(e => e.DriverId);
                entity.HasIndex(e => e.Date);
                entity.HasIndex(e => new { e.DriverId, e.Date });
            });

            modelBuilder.Entity<DeliveryEntity>(entity =>
            {
                entity.ToTable("Deliveries");
                entity.HasIndex(e => e.RouteId);
                entity.HasIndex(e => new { e.RouteId, e.SortOrder });
                entity.HasOne(e => e.Route)
                      .WithMany(r => r.Deliveries)
                      .HasForeignKey(e => e.RouteId)
                      .OnDelete(DeleteBehavior.Cascade);
            });

            modelBuilder.Entity<AlertEntity>(entity =>
            {
                entity.ToTable("Alerts");
                entity.HasIndex(e => e.DeliveryId);
                entity.HasOne(e => e.Delivery)
                      .WithMany(d => d.Alerts)
                      .HasForeignKey(e => e.DeliveryId)
                      .OnDelete(DeleteBehavior.Cascade);
            });
        }
    }
}
