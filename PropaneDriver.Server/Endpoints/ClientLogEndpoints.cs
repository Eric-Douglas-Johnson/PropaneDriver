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

            // Run each DeliveryTimes repair step individually and report results
            app.MapGet("api/admin/repair-delivery-times", async (PropaneDriverDbContext db) =>
            {
                var conn = db.Database.GetDbConnection();
                await conn.OpenAsync();

                var steps = new[]
                {
                    "TRUNCATE TABLE [DeliveryTimes]",
                    "IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE Name=N'AddressId' AND Object_ID=Object_ID(N'[dbo].[DeliveryTimes]')) ALTER TABLE [DeliveryTimes] ADD [AddressId] uniqueidentifier NOT NULL CONSTRAINT [DF_DeliveryTimes_AddressId] DEFAULT '00000000-0000-0000-0000-000000000000'",
                    "IF EXISTS (SELECT 1 FROM sys.default_constraints WHERE name=N'DF_DeliveryTimes_AddressId') ALTER TABLE [DeliveryTimes] DROP CONSTRAINT [DF_DeliveryTimes_AddressId]",
                    "IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name=N'FK_DeliveryTimes_Addresses_AddressId') ALTER TABLE [DeliveryTimes] ADD CONSTRAINT [FK_DeliveryTimes_Addresses_AddressId] FOREIGN KEY ([AddressId]) REFERENCES [Addresses] ([Id])",
                    "IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name=N'IX_DeliveryTimes_AddressId' AND object_id=OBJECT_ID(N'[dbo].[DeliveryTimes]')) CREATE INDEX [IX_DeliveryTimes_AddressId] ON [DeliveryTimes] ([AddressId])"
                };

                var results = new List<object>();
                foreach (var sql in steps)
                {
                    try
                    {
                        await using var cmd = conn.CreateCommand();
                        cmd.CommandText = sql;
                        await cmd.ExecuteNonQueryAsync();
                        results.Add(new { sql = sql[..Math.Min(60, sql.Length)], ok = true });
                    }
                    catch (Exception ex)
                    {
                        results.Add(new { sql = sql[..Math.Min(60, sql.Length)], ok = false, error = ex.Message });
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
