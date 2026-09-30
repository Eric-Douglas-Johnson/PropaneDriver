
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
        public DbSet<UserDbRecord> Users => Set<UserDbRecord>();
        public DbSet<DriverDbRecord> Drivers => Set<DriverDbRecord>();
        public DbSet<SupervisorDbRecord> Supervisors => Set<SupervisorDbRecord>();
        public DbSet<AdministratorDbRecord> Administrators => Set<AdministratorDbRecord>();
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

            modelBuilder.Entity<UserDbRecord>(entity =>
            {
                entity.ToTable("Users");
                entity.HasIndex(e => e.UserName).IsUnique();
                entity.HasIndex(e => e.Email);
            });

            // Role tables depend only on Users, never on each other.
            modelBuilder.Entity<DriverDbRecord>(entity =>
            {
                entity.ToTable("Drivers");
                entity.HasOne<UserDbRecord>()
                      .WithOne()
                      .HasForeignKey<DriverDbRecord>(e => e.UserId)
                      .OnDelete(DeleteBehavior.Cascade);
            });

            modelBuilder.Entity<SupervisorDbRecord>(entity =>
            {
                entity.ToTable("Supervisors");
                entity.HasOne<UserDbRecord>()
                      .WithOne()
                      .HasForeignKey<SupervisorDbRecord>(e => e.UserId)
                      .OnDelete(DeleteBehavior.Cascade);
            });

            modelBuilder.Entity<AdministratorDbRecord>(entity =>
            {
                entity.ToTable("Administrators");
                entity.HasOne<UserDbRecord>()
                      .WithOne()
                      .HasForeignKey<AdministratorDbRecord>(e => e.UserId)
                      .OnDelete(DeleteBehavior.Cascade);
            });

            modelBuilder.Entity<PasswordResetTokenDbRecord>(entity =>
            {
                entity.ToTable("PasswordResetTokens");
                entity.HasIndex(e => e.UserId);
                entity.HasIndex(e => e.TokenHash);
                entity.HasOne<UserDbRecord>()
                      .WithMany()
                      .HasForeignKey(e => e.UserId)
                      .OnDelete(DeleteBehavior.Cascade);
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

                entity.HasOne<DriverDbRecord>()
                      .WithMany()
                      .HasForeignKey(e => e.DriverId)
                      .OnDelete(DeleteBehavior.Restrict);
            });

            modelBuilder.Entity<DeliveryDbRecord>(entity =>
            {
                entity.ToTable("Deliveries");
                entity.HasIndex(e => e.RouteId);
                entity.HasIndex(e => new { e.RouteId, e.SortOrder });
                entity.HasIndex(e => e.AddressId);

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

                entity.HasOne<DriverDbRecord>()
                      .WithMany()
                      .HasForeignKey(e => e.DriverId)
                      .OnDelete(DeleteBehavior.Restrict);
            });
        }
    }
}
