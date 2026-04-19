using Microsoft.EntityFrameworkCore;

namespace PropaneDriver.Server.Data
{
    // Startup DDL bootstrap. We do not use EF migrations; instead we run
    // idempotent raw SQL against the Azure SQL database on app start so a
    // fresh deployment self-heals its schema. Each block is guarded with an
    // IF NOT EXISTS / schema-probe so re-running is safe.
    public static class DatabaseInitializer
    {
        public static void EnsureCreated(IServiceProvider services, ILogger logger)
        {
            using var scope = services.CreateScope();
            try
            {
                var db = scope.ServiceProvider.GetRequiredService<PropaneDriverDbContext>();
                db.Database.ExecuteSqlRaw(@"
                    IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'ErrorLog')
                    BEGIN
                        CREATE TABLE [ErrorLog] (
                            [Id] uniqueidentifier NOT NULL PRIMARY KEY,
                            [Source] nvarchar(200) NOT NULL,
                            [Level] nvarchar(50) NOT NULL,
                            [Message] nvarchar(max) NOT NULL,
                            [Timestamp] datetime2 NOT NULL
                        );
                        CREATE INDEX [IX_ErrorLog_Source] ON [ErrorLog] ([Source]);
                        CREATE INDEX [IX_ErrorLog_Timestamp] ON [ErrorLog] ([Timestamp]);
                    END

                    IF EXISTS (SELECT * FROM sys.tables WHERE name = 'DeliveryStatus')
                    BEGIN
                        DROP TABLE [DeliveryStatus];
                    END

                    IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'Routes')
                    BEGIN
                        CREATE TABLE [Routes] (
                            [Id] uniqueidentifier NOT NULL PRIMARY KEY,
                            [DriverId] uniqueidentifier NOT NULL,
                            [Date] date NOT NULL,
                            [CreatedAt] datetime2 NOT NULL
                        );
                        CREATE INDEX [IX_Routes_DriverId] ON [Routes] ([DriverId]);
                        CREATE INDEX [IX_Routes_Date] ON [Routes] ([Date]);
                        CREATE INDEX [IX_Routes_DriverId_Date] ON [Routes] ([DriverId], [Date]);
                    END

                    IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'Deliveries')
                    BEGIN
                        CREATE TABLE [Deliveries] (
                            [Id] uniqueidentifier NOT NULL PRIMARY KEY,
                            [RouteId] uniqueidentifier NOT NULL,
                            [CustomerName] nvarchar(200) NOT NULL,
                            [Street] nvarchar(200) NOT NULL,
                            [City] nvarchar(100) NOT NULL,
                            [State] nvarchar(50) NOT NULL,
                            [ZipCode] nvarchar(20) NOT NULL,
                            [Latitude] float NOT NULL,
                            [Longitude] float NOT NULL,
                            [Status] int NOT NULL,
                            [AvgDeliveryTimeMinutes] float NOT NULL,
                            [SortOrder] int NOT NULL,
                            [CreatedAt] datetime2 NOT NULL,
                            CONSTRAINT [FK_Deliveries_Routes_RouteId] FOREIGN KEY ([RouteId])
                                REFERENCES [Routes] ([Id]) ON DELETE CASCADE
                        );
                        CREATE INDEX [IX_Deliveries_RouteId] ON [Deliveries] ([RouteId]);
                        CREATE INDEX [IX_Deliveries_RouteId_SortOrder] ON [Deliveries] ([RouteId], [SortOrder]);
                    END

                    IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'Alerts')
                    BEGIN
                        CREATE TABLE [Alerts] (
                            [Id] uniqueidentifier NOT NULL PRIMARY KEY,
                            [DeliveryId] uniqueidentifier NOT NULL,
                            [Message] nvarchar(500) NOT NULL,
                            [CreatedAt] datetime2 NOT NULL,
                            [Seen] bit NOT NULL CONSTRAINT [DF_Alerts_Seen] DEFAULT 0,
                            CONSTRAINT [FK_Alerts_Deliveries_DeliveryId] FOREIGN KEY ([DeliveryId])
                                REFERENCES [Deliveries] ([Id]) ON DELETE CASCADE
                        );
                        CREATE INDEX [IX_Alerts_DeliveryId] ON [Alerts] ([DeliveryId]);
                    END
                    ELSE IF NOT EXISTS (
                        SELECT 1 FROM sys.columns
                        WHERE Name = N'Seen' AND Object_ID = Object_ID(N'[dbo].[Alerts]'))
                    BEGIN
                        ALTER TABLE [Alerts]
                            ADD [Seen] bit NOT NULL CONSTRAINT [DF_Alerts_Seen] DEFAULT 0;
                    END
                ");
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Startup table creation failed; will retry on first request.");
            }
        }
    }
}
