
using Microsoft.EntityFrameworkCore;

namespace PropaneDriver.Server.Data
{
    public class PropaneDriverDbContext : DbContext
    {
        public PropaneDriverDbContext(DbContextOptions<PropaneDriverDbContext> options)
            : base(options)
        {
        }

        public DbSet<AddressDbRecord> Addresses => Set<AddressDbRecord>();
        public DbSet<DeliveryTimeDbRecord> DeliveryTimes => Set<DeliveryTimeDbRecord>();
        public DbSet<DriverDbRecord> Drivers => Set<DriverDbRecord>();
        public DbSet<PasswordResetTokenDbRecord> PasswordResetTokens => Set<PasswordResetTokenDbRecord>();
        public DbSet<ErrorLogDbRecord> ErrorLogs => Set<ErrorLogDbRecord>();
        public DbSet<RouteDbRecord> Routes => Set<RouteDbRecord>();
        public DbSet<DeliveryDbRecord> Deliveries => Set<DeliveryDbRecord>();
        public DbSet<AlertDbRecord> Alerts => Set<AlertDbRecord>();
        public DbSet<FuelLogEntryDbRecord> FuelLogEntries => Set<FuelLogEntryDbRecord>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            modelBuilder.Entity<AddressDbRecord>(entity =>
            {
                entity.ToTable("Addresses");
                entity.HasIndex(e => new { e.Street, e.City, e.State, e.ZipCode }).IsUnique();
            });

            modelBuilder.Entity<DeliveryTimeDbRecord>(entity =>
            {
                entity.ToTable("DeliveryTimes");
                entity.HasIndex(e => e.AddressId);
                entity.HasIndex(e => e.DeliveryId);
            });

            modelBuilder.Entity<DriverDbRecord>(entity =>
            {
                entity.ToTable("Drivers");
                entity.HasIndex(e => e.UserName).IsUnique();
                entity.HasIndex(e => e.Email);
            });

            modelBuilder.Entity<PasswordResetTokenDbRecord>(entity =>
            {
                entity.ToTable("PasswordResetTokens");
                entity.HasIndex(e => e.DriverId);
                entity.HasIndex(e => e.TokenHash);
            });

            modelBuilder.Entity<ErrorLogDbRecord>(entity =>
            {
                entity.ToTable("ErrorLog");
                entity.HasIndex(e => e.Source);
                entity.HasIndex(e => e.Timestamp);
            });

            modelBuilder.Entity<RouteDbRecord>(entity =>
            {
                entity.ToTable("Routes");
                entity.HasIndex(e => e.DriverId);
                entity.HasIndex(e => e.Date);
                entity.HasIndex(e => new { e.DriverId, e.Date });
            });

            modelBuilder.Entity<DeliveryDbRecord>(entity =>
            {
                entity.ToTable("Deliveries");
                entity.HasIndex(e => e.RouteId);
                entity.HasIndex(e => new { e.RouteId, e.SortOrder });
                entity.HasIndex(e => e.AddressId);
                // Relationships kept FK-only (no navigation properties) so the
                // schema and cascade behavior are unchanged; queries join by id.
                entity.HasOne<RouteDbRecord>()
                      .WithMany()
                      .HasForeignKey(e => e.RouteId)
                      .OnDelete(DeleteBehavior.Cascade);
                entity.HasOne<AddressDbRecord>()
                      .WithMany()
                      .HasForeignKey(e => e.AddressId)
                      .OnDelete(DeleteBehavior.Restrict);
            });

            modelBuilder.Entity<AlertDbRecord>(entity =>
            {
                entity.ToTable("Alerts");
                entity.HasIndex(e => e.DeliveryId);
                entity.HasOne<DeliveryDbRecord>()
                      .WithMany()
                      .HasForeignKey(e => e.DeliveryId)
                      .OnDelete(DeleteBehavior.Cascade);
            });

            modelBuilder.Entity<FuelLogEntryDbRecord>(entity =>
            {
                entity.ToTable("FuelLogEntries");
                entity.HasIndex(e => e.DriverId);
                entity.HasIndex(e => new { e.DriverId, e.SortOrder });
                entity.Property(e => e.MeterValue).HasPrecision(18, 2);
                entity.Property(e => e.GallonsPumped).HasPrecision(18, 2);
            });
        }
    }
}
