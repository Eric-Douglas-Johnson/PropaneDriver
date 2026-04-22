using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using PropaneDriver.Server.Data;
using PropaneDriver.Shared.Dtos;

namespace PropaneDriver.Server.Endpoints
{
    public static class ClientLogEndpoints
    {
        public static IEndpointRouteBuilder MapClientLogEndpoints(this IEndpointRouteBuilder app)
        {
            // Read recent error logs (diagnostic)
            app.MapGet("api/client-logs", async (PropaneDriverDbContext db) =>
            {
                var logs = await db.ErrorLogs
                    .OrderByDescending(e => e.Timestamp)
                    .Take(50)
                    .ToListAsync();
                return Results.Ok(logs);
            });

            // Return columns for key tables so we can diagnose live schema state
            app.MapGet("api/admin/schema", async (PropaneDriverDbContext db) =>
            {
                var conn = db.Database.GetDbConnection();
                await conn.OpenAsync();
                await using var cmd = conn.CreateCommand();
                cmd.CommandText = @"
                    SELECT t.name, c.name, tp.name, c.is_nullable
                    FROM sys.columns c
                    JOIN sys.tables  t  ON t.object_id = c.object_id
                    JOIN sys.types   tp ON tp.user_type_id = c.user_type_id
                    WHERE t.name IN ('Deliveries','DeliveryTimes','Addresses')
                    ORDER BY t.name, c.column_id";

                var rows = new List<object>();
                await using var reader = await cmd.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                    rows.Add(new { Table = reader.GetString(0), Column = reader.GetString(1), Type = reader.GetString(2), Nullable = reader.GetBoolean(3) });

                return Results.Ok(rows);
            });

            // Run full schema repair in order and report each step
            app.MapGet("api/admin/repair-delivery-times", async (PropaneDriverDbContext db) =>
            {
                var conn = db.Database.GetDbConnection();
                await conn.OpenAsync();

                var steps = new[]
                {
                    // 1. Create Addresses table (root dependency for all FKs)
                    @"IF NOT EXISTS (SELECT * FROM sys.tables WHERE name='Addresses')
                      CREATE TABLE [Addresses] (
                          [Id] uniqueidentifier NOT NULL PRIMARY KEY,
                          [Street] nvarchar(200) NOT NULL,
                          [City] nvarchar(100) NOT NULL,
                          [State] nvarchar(50) NOT NULL,
                          [ZipCode] nvarchar(20) NOT NULL,
                          [Latitude] float NOT NULL DEFAULT 0,
                          [Longitude] float NOT NULL DEFAULT 0,
                          [AvgDeliveryTimeSeconds] float NOT NULL DEFAULT 0,
                          CONSTRAINT [UQ_Addresses_Location] UNIQUE ([Street],[City],[State],[ZipCode])
                      )",

                    // 2. Seed Addresses from Deliveries if Street column still exists.
                    //    Wrapped in EXEC() so SQL Server defers column-name resolution until
                    //    runtime — otherwise the outer IF guard doesn't help when Deliveries
                    //    has already been migrated and d.[Street] no longer exists, which
                    //    trips "Invalid column name" at compile time.
                    @"IF EXISTS (SELECT 1 FROM sys.columns WHERE Name=N'Street' AND Object_ID=Object_ID(N'[dbo].[Deliveries]'))
                      EXEC('INSERT INTO [Addresses] ([Id],[Street],[City],[State],[ZipCode],[Latitude],[Longitude],[AvgDeliveryTimeSeconds])
                            SELECT NEWID(),d.[Street],d.[City],d.[State],d.[ZipCode],AVG(d.[Latitude]),AVG(d.[Longitude]),0
                            FROM [Deliveries] d
                            WHERE LEN(TRIM(d.[Street]))>0 AND LEN(TRIM(d.[City]))>0 AND LEN(TRIM(d.[State]))>0 AND LEN(TRIM(d.[ZipCode]))>0
                              AND NOT EXISTS (SELECT 1 FROM [Addresses] a WHERE a.[Street]=d.[Street] AND a.[City]=d.[City] AND a.[State]=d.[State] AND a.[ZipCode]=d.[ZipCode])
                            GROUP BY d.[Street],d.[City],d.[State],d.[ZipCode]')",

                    // 3. Add AddressId to Deliveries if Street column still exists
                    @"IF EXISTS (SELECT 1 FROM sys.columns WHERE Name=N'Street' AND Object_ID=Object_ID(N'[dbo].[Deliveries]'))
                      AND NOT EXISTS (SELECT 1 FROM sys.columns WHERE Name=N'AddressId' AND Object_ID=Object_ID(N'[dbo].[Deliveries]'))
                      ALTER TABLE [Deliveries] ADD [AddressId] uniqueidentifier NULL",

                    // 4. Link deliveries to addresses. Same EXEC() trick as step 2.
                    @"IF EXISTS (SELECT 1 FROM sys.columns WHERE Name=N'Street' AND Object_ID=Object_ID(N'[dbo].[Deliveries]'))
                      EXEC('UPDATE d SET d.[AddressId]=a.[Id] FROM [Deliveries] d
                            JOIN [Addresses] a ON d.[Street]=a.[Street] AND d.[City]=a.[City] AND d.[State]=a.[State] AND d.[ZipCode]=a.[ZipCode]
                            WHERE d.[AddressId] IS NULL')",

                    // 5. Drop unlinked deliveries
                    @"IF EXISTS (SELECT 1 FROM sys.columns WHERE Name=N'AddressId' AND Object_ID=Object_ID(N'[dbo].[Deliveries]'))
                      DELETE FROM [Deliveries] WHERE [AddressId] IS NULL",

                    // 6. Make Deliveries.AddressId NOT NULL
                    @"IF EXISTS (SELECT 1 FROM sys.columns WHERE Name=N'AddressId' AND Object_ID=Object_ID(N'[dbo].[Deliveries]') AND is_nullable=1)
                      ALTER TABLE [Deliveries] ALTER COLUMN [AddressId] uniqueidentifier NOT NULL",

                    // 7. Add FK Deliveries→Addresses
                    @"IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name=N'FK_Deliveries_Addresses_AddressId')
                      ALTER TABLE [Deliveries] ADD CONSTRAINT [FK_Deliveries_Addresses_AddressId] FOREIGN KEY ([AddressId]) REFERENCES [Addresses] ([Id])",

                    // 8. Drop old Deliveries address columns
                    @"IF EXISTS (SELECT 1 FROM sys.columns WHERE Name=N'Street' AND Object_ID=Object_ID(N'[dbo].[Deliveries]'))
                      ALTER TABLE [Deliveries] DROP COLUMN [Street]",
                    @"IF EXISTS (SELECT 1 FROM sys.columns WHERE Name=N'City' AND Object_ID=Object_ID(N'[dbo].[Deliveries]'))
                      ALTER TABLE [Deliveries] DROP COLUMN [City]",
                    @"IF EXISTS (SELECT 1 FROM sys.columns WHERE Name=N'State' AND Object_ID=Object_ID(N'[dbo].[Deliveries]'))
                      ALTER TABLE [Deliveries] DROP COLUMN [State]",
                    @"IF EXISTS (SELECT 1 FROM sys.columns WHERE Name=N'ZipCode' AND Object_ID=Object_ID(N'[dbo].[Deliveries]'))
                      ALTER TABLE [Deliveries] DROP COLUMN [ZipCode]",
                    @"IF EXISTS (SELECT 1 FROM sys.columns WHERE Name=N'Latitude' AND Object_ID=Object_ID(N'[dbo].[Deliveries]'))
                      ALTER TABLE [Deliveries] DROP COLUMN [Latitude]",
                    @"IF EXISTS (SELECT 1 FROM sys.columns WHERE Name=N'Longitude' AND Object_ID=Object_ID(N'[dbo].[Deliveries]'))
                      ALTER TABLE [Deliveries] DROP COLUMN [Longitude]",

                    // 9. Fix DeliveryTimes FK now that Addresses exists
                    @"IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name=N'FK_DeliveryTimes_Addresses_AddressId')
                      AND EXISTS (SELECT 1 FROM sys.columns WHERE Name=N'AddressId' AND Object_ID=Object_ID(N'[dbo].[DeliveryTimes]'))
                      ALTER TABLE [DeliveryTimes] ADD CONSTRAINT [FK_DeliveryTimes_Addresses_AddressId] FOREIGN KEY ([AddressId]) REFERENCES [Addresses] ([Id])",

                    // 10. Drop stale legacy columns on DeliveryTimes left behind by
                    //     a half-applied migration. With NOT NULL + no defaults these
                    //     columns reject every INSERT the app tries to do.
                    @"IF EXISTS (SELECT 1 FROM sys.columns WHERE Name=N'Address' AND Object_ID=Object_ID(N'[dbo].[DeliveryTimes]'))
                      ALTER TABLE [DeliveryTimes] DROP COLUMN [Address]",
                    @"IF EXISTS (SELECT 1 FROM sys.columns WHERE Name=N'Latitude' AND Object_ID=Object_ID(N'[dbo].[DeliveryTimes]'))
                      ALTER TABLE [DeliveryTimes] DROP COLUMN [Latitude]",
                    @"IF EXISTS (SELECT 1 FROM sys.columns WHERE Name=N'Longitude' AND Object_ID=Object_ID(N'[dbo].[DeliveryTimes]'))
                      ALTER TABLE [DeliveryTimes] DROP COLUMN [Longitude]",
                    @"IF EXISTS (SELECT 1 FROM sys.columns WHERE Name=N'Street' AND Object_ID=Object_ID(N'[dbo].[DeliveryTimes]'))
                      ALTER TABLE [DeliveryTimes] DROP COLUMN [Street]",
                    @"IF EXISTS (SELECT 1 FROM sys.columns WHERE Name=N'City' AND Object_ID=Object_ID(N'[dbo].[DeliveryTimes]'))
                      ALTER TABLE [DeliveryTimes] DROP COLUMN [City]",
                    @"IF EXISTS (SELECT 1 FROM sys.columns WHERE Name=N'State' AND Object_ID=Object_ID(N'[dbo].[DeliveryTimes]'))
                      ALTER TABLE [DeliveryTimes] DROP COLUMN [State]",
                    @"IF EXISTS (SELECT 1 FROM sys.columns WHERE Name=N'ZipCode' AND Object_ID=Object_ID(N'[dbo].[DeliveryTimes]'))
                      ALTER TABLE [DeliveryTimes] DROP COLUMN [ZipCode]",
                };

                var results = new List<object>();
                foreach (var sql in steps)
                {
                    try
                    {
                        await using var cmd = conn.CreateCommand();
                        cmd.CommandText = sql;
                        await cmd.ExecuteNonQueryAsync();
                        results.Add(new { sql = sql.Trim()[..Math.Min(60, sql.Trim().Length)], ok = true });
                    }
                    catch (Exception ex)
                    {
                        results.Add(new { sql = sql.Trim()[..Math.Min(60, sql.Trim().Length)], ok = false, error = ex.Message });
                    }
                }

                return Results.Ok(results);
            });

            // Log a client-side error
            app.MapPost("api/client-logs", async (
                ClientLogDto log,
                PropaneDriverDbContext db,
                ILogger<Program> logger) =>
            {
                try
                {
                    var entity = new ErrorLogEntity
                    {
                        Id = Guid.NewGuid(),
                        Source = log.Source ?? "Unknown",
                        Level = log.Level ?? "Error",
                        Message = log.Message ?? "",
                        Timestamp = log.Timestamp ?? DateTime.UtcNow
                    };

                    db.ErrorLogs.Add(entity);
                    await db.SaveChangesAsync();

                    return Results.Ok(new { entity.Id });
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Failed to persist client error log");
                    return Results.Problem(detail: ex.Message, statusCode: 500);
                }
            });

            return app;
        }
    }
}
