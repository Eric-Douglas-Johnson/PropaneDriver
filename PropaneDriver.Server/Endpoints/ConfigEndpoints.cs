namespace PropaneDriver.Server.Endpoints
{
    public static class ConfigEndpoints
    {
        public static IEndpointRouteBuilder MapConfigEndpoints(this IEndpointRouteBuilder app)
        {
            // Return the browser-facing Google Maps JS API key. This key is HTTP-referrer
            // restricted in the Google Cloud Console, so exposing it here is acceptable.
            // Separate from the server-side Geocoding key, which never leaves the server.
            app.MapGet("api/config/maps-key", (IConfiguration config, ILogger<Program> logger) =>
            {
                var key = config["GoogleMaps:JsApiKey"]
                    ?? Environment.GetEnvironmentVariable("GOOGLE_MAPS_JS_API_KEY");

                if (string.IsNullOrWhiteSpace(key))
                {
                    logger.LogError("Google Maps JS API key is not configured.");
                    return Results.Problem(
                        detail: "Maps key is not configured.",
                        statusCode: 500);
                }

                return Results.Ok(new { key });
            });

            return app;
        }
    }
}
