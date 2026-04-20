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
                            [EstimatedRouteTime] float NOT NULL CONSTRAINT [DF_Routes_EstimatedRouteTime] DEFAULT 0,
                            [CreatedAt] datetime2 NOT NULL
                        );
                        CREATE INDEX [IX_Routes_DriverId] ON [Routes] ([DriverId]);
                        CREATE INDEX [IX_Routes_Date] ON [Routes] ([Date]);
                        CREATE INDEX [IX_Routes_DriverId_Date] ON [Routes] ([DriverId], [Date]);
                    END
                    ELSE IF NOT EXISTS (
                        SELECT 1 FROM sys.columns
                        WHERE Name = N'EstimatedRouteTime' AND Object_ID = Object_ID(N'[dbo].[Routes]'))
                    BEGIN
                        ALTER TABLE [Routes]
                            ADD [EstimatedRouteTime] float NOT NULL CONSTRAINT [DF_Routes_EstimatedRouteTime] DEFAULT 0;
                    END

                    -- Addresses must exist before Deliveries (FK dependency).
                    IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'Addresses')
                    BEGIN
                        CREATE TABLE [Addresses] (
                            [Id] uniqueidentifier NOT NULL PRIMARY KEY,
                            [Street] nvarchar(200) NOT NULL,
                            [City] nvarchar(100) NOT NULL,
                            [State] nvarchar(50) NOT NULL,
                            [ZipCode] nvarchar(20) NOT NULL,
                            [Latitude] float NOT NULL CONSTRAINT [DF_Addresses_Latitude] DEFAULT 0,
                            [Longitude] float NOT NULL CONSTRAINT [DF_Addresses_Longitude] DEFAULT 0,
                            [AvgDeliveryTimeSeconds] float NOT NULL CONSTRAINT [DF_Addresses_AvgDeliveryTimeSeconds] DEFAULT 0,
                            CONSTRAINT [UQ_Addresses_Location] UNIQUE ([Street], [City], [State], [ZipCode]),
                            CONSTRAINT [CK_Addresses_Street] CHECK (LEN(TRIM([Street])) > 0),
                            CONSTRAINT [CK_Addresses_City] CHECK (LEN(TRIM([City])) > 0),
                            CONSTRAINT [CK_Addresses_State] CHECK (LEN(TRIM([State])) > 0),
                            CONSTRAINT [CK_Addresses_ZipCode] CHECK (LEN(TRIM([ZipCode])) > 0)
                        );
                        CREATE INDEX [IX_Addresses_Location] ON [Addresses] ([Street], [City], [State], [ZipCode]);
                    END

                    IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'Deliveries')
                    BEGIN
                        CREATE TABLE [Deliveries] (
                            [Id] uniqueidentifier NOT NULL PRIMARY KEY,
                            [RouteId] uniqueidentifier NOT NULL,
                            [AddressId] uniqueidentifier NOT NULL,
                            [CustomerName] nvarchar(200) NOT NULL,
                            [Status] int NOT NULL,
                            [AvgDeliveryTimeMinutes] float NOT NULL,
                            [SortOrder] int NOT NULL,
                            [CreatedAt] datetime2 NOT NULL,
                            CONSTRAINT [FK_Deliveries_Routes_RouteId] FOREIGN KEY ([RouteId])
                                REFERENCES [Routes] ([Id]) ON DELETE CASCADE,
                            CONSTRAINT [FK_Deliveries_Addresses_AddressId] FOREIGN KEY ([AddressId])
                                REFERENCES [Addresses] ([Id])
                        );
                        CREATE INDEX [IX_Deliveries_RouteId] ON [Deliveries] ([RouteId]);
                        CREATE INDEX [IX_Deliveries_RouteId_SortOrder] ON [Deliveries] ([RouteId], [SortOrder]);
                        CREATE INDEX [IX_Deliveries_AddressId] ON [Deliveries] ([AddressId]);
                    END
                    ELSE IF EXISTS (
                        SELECT 1 FROM sys.columns
                        WHERE Name = N'Street' AND Object_ID = Object_ID(N'[dbo].[Deliveries]'))
                    BEGIN
                        -- Migrate: populate Addresses from existing Deliveries data, then
                        -- replace the inline address columns with a FK to Addresses.
                        -- Each step is guarded so partial re-runs are safe.

                        -- Insert only addresses not already in the table (idempotent).
                        INSERT INTO [Addresses] ([Id], [Street], [City], [State], [ZipCode], [Latitude], [Longitude], [AvgDeliveryTimeSeconds])
                        SELECT NEWID(), d.[Street], d.[City], d.[State], d.[ZipCode],
                               AVG(d.[Latitude]), AVG(d.[Longitude]), 0
                        FROM [Deliveries] d
                        WHERE LEN(TRIM(d.[Street])) > 0
                          AND LEN(TRIM(d.[City]))   > 0
                          AND LEN(TRIM(d.[State]))  > 0
                          AND LEN(TRIM(d.[ZipCode])) > 0
                          AND NOT EXISTS (
                              SELECT 1 FROM [Addresses] a
                              WHERE a.[Street] = d.[Street]
                                AND a.[City]   = d.[City]
                                AND a.[State]  = d.[State]
                                AND a.[ZipCode] = d.[ZipCode])
                        GROUP BY d.[Street], d.[City], d.[State], d.[ZipCode];

                        -- Add AddressId column only if it doesn't exist yet.
                        IF NOT EXISTS (
                            SELECT 1 FROM sys.columns
                            WHERE Name = N'AddressId' AND Object_ID = Object_ID(N'[dbo].[Deliveries]'))
                        BEGIN
                            ALTER TABLE [Deliveries] ADD [AddressId] uniqueidentifier NULL;
                        END

                        -- Link each delivery to its matching Address row.
                        UPDATE d SET d.[AddressId] = a.[Id]
                        FROM [Deliveries] d
                        INNER JOIN [Addresses] a
                            ON d.[Street] = a.[Street]
                           AND d.[City]   = a.[City]
                           AND d.[State]  = a.[State]
                           AND d.[ZipCode] = a.[ZipCode]
                        WHERE d.[AddressId] IS NULL;

                        -- Drop deliveries with no match (bad data with empty fields).
                        DELETE FROM [Deliveries] WHERE [AddressId] IS NULL;

                        -- Make column NOT NULL now that all rows are linked.
                        IF EXISTS (
                            SELECT 1 FROM sys.columns
                            WHERE Name = N'AddressId' AND Object_ID = Object_ID(N'[dbo].[Deliveries]')
                              AND is_nullable = 1)
                        BEGIN
                            ALTER TABLE [Deliveries] ALTER COLUMN [AddressId] uniqueidentifier NOT NULL;
                        END

                        IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_Deliveries_Addresses_AddressId')
                            ALTER TABLE [Deliveries] ADD CONSTRAINT [FK_Deliveries_Addresses_AddressId]
                                FOREIGN KEY ([AddressId]) REFERENCES [Addresses] ([Id]);

                        IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_Deliveries_AddressId' AND object_id = OBJECT_ID(N'[dbo].[Deliveries]'))
                            CREATE INDEX [IX_Deliveries_AddressId] ON [Deliveries] ([AddressId]);

                        -- Drop the now-redundant address check constraints.
                        IF EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = N'CK_Deliveries_Street'  AND parent_object_id = OBJECT_ID(N'[dbo].[Deliveries]'))
                            ALTER TABLE [Deliveries] DROP CONSTRAINT [CK_Deliveries_Street];
                        IF EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = N'CK_Deliveries_City'    AND parent_object_id = OBJECT_ID(N'[dbo].[Deliveries]'))
                            ALTER TABLE [Deliveries] DROP CONSTRAINT [CK_Deliveries_City];
                        IF EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = N'CK_Deliveries_State'   AND parent_object_id = OBJECT_ID(N'[dbo].[Deliveries]'))
                            ALTER TABLE [Deliveries] DROP CONSTRAINT [CK_Deliveries_State];
                        IF EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = N'CK_Deliveries_ZipCode' AND parent_object_id = OBJECT_ID(N'[dbo].[Deliveries]'))
                            ALTER TABLE [Deliveries] DROP CONSTRAINT [CK_Deliveries_ZipCode];

                        -- Drop old address columns only after FK is in place.
                        IF EXISTS (SELECT 1 FROM sys.columns WHERE Name = N'Street' AND Object_ID = Object_ID(N'[dbo].[Deliveries]'))
                            ALTER TABLE [Deliveries] DROP COLUMN [Street];
                        IF EXISTS (SELECT 1 FROM sys.columns WHERE Name = N'City' AND Object_ID = Object_ID(N'[dbo].[Deliveries]'))
                            ALTER TABLE [Deliveries] DROP COLUMN [City];
                        IF EXISTS (SELECT 1 FROM sys.columns WHERE Name = N'State' AND Object_ID = Object_ID(N'[dbo].[Deliveries]'))
                            ALTER TABLE [Deliveries] DROP COLUMN [State];
                        IF EXISTS (SELECT 1 FROM sys.columns WHERE Name = N'ZipCode' AND Object_ID = Object_ID(N'[dbo].[Deliveries]'))
                            ALTER TABLE [Deliveries] DROP COLUMN [ZipCode];
                        IF EXISTS (SELECT 1 FROM sys.columns WHERE Name = N'Latitude' AND Object_ID = Object_ID(N'[dbo].[Deliveries]'))
                            ALTER TABLE [Deliveries] DROP COLUMN [Latitude];
                        IF EXISTS (SELECT 1 FROM sys.columns WHERE Name = N'Longitude' AND Object_ID = Object_ID(N'[dbo].[Deliveries]'))
                            ALTER TABLE [Deliveries] DROP COLUMN [Longitude];
                    END

                    IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'DeliveryTimes')
                    BEGIN
                        CREATE TABLE [DeliveryTimes] (
                            [Id] int NOT NULL PRIMARY KEY IDENTITY(1,1),
                            [DeliveryId] nvarchar(450) NOT NULL,
                            [AddressId] uniqueidentifier NOT NULL,
                            [TimeIntervalSeconds] float NOT NULL,
                            [RecordedAt] datetime2 NOT NULL,
                            CONSTRAINT [FK_DeliveryTimes_Addresses_AddressId] FOREIGN KEY ([AddressId])
                                REFERENCES [Addresses] ([Id])
                        );
                        CREATE INDEX [IX_DeliveryTimes_DeliveryId] ON [DeliveryTimes] ([DeliveryId]);
                        CREATE INDEX [IX_DeliveryTimes_AddressId] ON [DeliveryTimes] ([AddressId]);
                    END
                    ELSE IF EXISTS (
                        SELECT 1 FROM sys.columns
                        WHERE Name = N'Street' AND Object_ID = Object_ID(N'[dbo].[DeliveryTimes]'))
                    BEGIN
                        -- Migrate: timing history from the individual-fields schema cannot be
                        -- reliably linked to Addresses rows without a join key. Truncate and
                        -- rebuild with the FK-based schema.
                        TRUNCATE TABLE [DeliveryTimes];

                        IF EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = N'CK_DeliveryTimes_Street'  AND parent_object_id = OBJECT_ID(N'[dbo].[DeliveryTimes]'))
                            ALTER TABLE [DeliveryTimes] DROP CONSTRAINT [CK_DeliveryTimes_Street];
                        IF EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = N'CK_DeliveryTimes_City'    AND parent_object_id = OBJECT_ID(N'[dbo].[DeliveryTimes]'))
                            ALTER TABLE [DeliveryTimes] DROP CONSTRAINT [CK_DeliveryTimes_City];
                        IF EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = N'CK_DeliveryTimes_State'   AND parent_object_id = OBJECT_ID(N'[dbo].[DeliveryTimes]'))
                            ALTER TABLE [DeliveryTimes] DROP CONSTRAINT [CK_DeliveryTimes_State];
                        IF EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = N'CK_DeliveryTimes_ZipCode' AND parent_object_id = OBJECT_ID(N'[dbo].[DeliveryTimes]'))
                            ALTER TABLE [DeliveryTimes] DROP CONSTRAINT [CK_DeliveryTimes_ZipCode];
                        IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_DeliveryTimes_Address' AND object_id = OBJECT_ID(N'[dbo].[DeliveryTimes]'))
                            DROP INDEX [IX_DeliveryTimes_Address] ON [DeliveryTimes];

                        ALTER TABLE [DeliveryTimes] DROP COLUMN [Street], [City], [State], [ZipCode], [Latitude], [Longitude];

                        ALTER TABLE [DeliveryTimes] ADD [AddressId] uniqueidentifier NULL;
                        ALTER TABLE [DeliveryTimes] ALTER COLUMN [AddressId] uniqueidentifier NOT NULL;
                        ALTER TABLE [DeliveryTimes] ADD CONSTRAINT [FK_DeliveryTimes_Addresses_AddressId]
                            FOREIGN KEY ([AddressId]) REFERENCES [Addresses] ([Id]);
                        CREATE INDEX [IX_DeliveryTimes_AddressId] ON [DeliveryTimes] ([AddressId]);
                    END
                    ELSE IF EXISTS (
                        SELECT 1 FROM sys.columns
                        WHERE Name = N'Address' AND Object_ID = Object_ID(N'[dbo].[DeliveryTimes]'))
                    BEGIN
                        -- Migrate from the original flat-string Address column.
                        TRUNCATE TABLE [DeliveryTimes];
                        IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_DeliveryTimes_Address' AND object_id = OBJECT_ID(N'[dbo].[DeliveryTimes]'))
                            DROP INDEX [IX_DeliveryTimes_Address] ON [DeliveryTimes];
                        ALTER TABLE [DeliveryTimes] DROP COLUMN [Address], [Latitude], [Longitude];
                        ALTER TABLE [DeliveryTimes] ADD [AddressId] uniqueidentifier NULL;
                        ALTER TABLE [DeliveryTimes] ALTER COLUMN [AddressId] uniqueidentifier NOT NULL;
                        ALTER TABLE [DeliveryTimes] ADD CONSTRAINT [FK_DeliveryTimes_Addresses_AddressId]
                            FOREIGN KEY ([AddressId]) REFERENCES [Addresses] ([Id]);
                        CREATE INDEX [IX_DeliveryTimes_AddressId] ON [DeliveryTimes] ([AddressId]);
                    END
                    ELSE IF NOT EXISTS (
                        SELECT 1 FROM sys.columns
                        WHERE Name = N'AddressId' AND Object_ID = Object_ID(N'[dbo].[DeliveryTimes]'))
                    BEGIN
                        -- Partial migration: old address columns were already dropped but
                        -- AddressId was never added. Add only what is missing.
                        ALTER TABLE [DeliveryTimes] ADD [AddressId] uniqueidentifier NULL;
                        -- Table has no data at this point so NULL→NOT NULL is safe.
                        ALTER TABLE [DeliveryTimes] ALTER COLUMN [AddressId] uniqueidentifier NOT NULL;
                        IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_DeliveryTimes_Addresses_AddressId')
                            ALTER TABLE [DeliveryTimes] ADD CONSTRAINT [FK_DeliveryTimes_Addresses_AddressId]
                                FOREIGN KEY ([AddressId]) REFERENCES [Addresses] ([Id]);
                        IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_DeliveryTimes_AddressId' AND object_id = OBJECT_ID(N'[dbo].[DeliveryTimes]'))
                            CREATE INDEX [IX_DeliveryTimes_AddressId] ON [DeliveryTimes] ([AddressId]);
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
