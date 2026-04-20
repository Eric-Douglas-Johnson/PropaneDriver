using Microsoft.EntityFrameworkCore;
using PropaneDriver.Server.Data;
using PropaneDriver.Shared.Dtos;

// Projection type for the schema diagnostic query.
file record SchemaColumnRow(string TableName, string ColumnName, string TypeName, bool IsNullable);

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
                var cols = await db.Database
                    .SqlQueryRaw<SchemaColumnRow>(@"
                        SELECT
                            t.name   AS TableName,
                            c.name   AS ColumnName,
                            tp.name  AS TypeName,
                            c.is_nullable AS IsNullable
                        FROM sys.columns c
                        JOIN sys.tables  t  ON t.object_id = c.object_id
                        JOIN sys.types   tp ON tp.user_type_id = c.user_type_id
                        WHERE t.name IN ('Deliveries','DeliveryTimes','Addresses')
                        ORDER BY t.name, c.column_id")
                    .ToListAsync();
                return Results.Ok(cols);
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
