using PropaneDriver.Server.Data;
using PropaneDriver.Shared.Dtos;

namespace PropaneDriver.Server.Endpoints
{
    public static class ClientLogEndpoints
    {
        public static IEndpointRouteBuilder MapClientLogEndpoints(this IEndpointRouteBuilder app)
        {
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
