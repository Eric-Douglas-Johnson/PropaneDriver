using System.Net.Http.Json;
using PropaneDriver.Server.Services;
using PropaneDriver.Shared.Dtos;

namespace PropaneDriver.Server.Endpoints
{
    public static class GeocodingEndpoints
    {
        public static IEndpointRouteBuilder MapGeocodingEndpoints(this IEndpointRouteBuilder app)
        {
            // Geocode an address via Google (proxied so the API key stays server-side).
            // Tries the Geocoding API first (structured, cheap). On ZERO_RESULTS falls
            // back to Places Text Search, which behaves like the maps.google.com search
            // box and handles rural / colloquial addresses much better.
            app.MapGet("api/geocode", async (
                string? street, string? city, string? state, string? zip,
                IHttpClientFactory httpFactory,
                IConfiguration config,
                ILogger<Program> logger) =>
            {
                var apiKey = config["GoogleGeocoding:ApiKey"]
                    ?? Environment.GetEnvironmentVariable("GOOGLE_GEOCODING_API_KEY");
                if (string.IsNullOrWhiteSpace(apiKey))
                {
                    logger.LogError("Google Geocoding API key is not configured.");
                    return Results.Problem(
                        detail: "Geocoding is not configured.",
                        statusCode: 500);
                }

                var parts = new List<string>();
                if (!string.IsNullOrWhiteSpace(street)) parts.Add(street.Trim());
                if (!string.IsNullOrWhiteSpace(city)) parts.Add(city.Trim());
                if (!string.IsNullOrWhiteSpace(state)) parts.Add(state.Trim());
                if (!string.IsNullOrWhiteSpace(zip)) parts.Add(zip.Trim());

                if (parts.Count == 0)
                    return Results.BadRequest(new { Message = "No address parts provided." });

                var rawAddress = string.Join(", ", parts);
                var encodedAddress = Uri.EscapeDataString(rawAddress);
                var http = httpFactory.CreateClient();

                // --- Attempt 1: Geocoding API -----------------------------------------
                var geocodeUrl = $"https://maps.googleapis.com/maps/api/geocode/json?address={encodedAddress}&region=us&key={apiKey}";

                GoogleGeocodeResult? best = null;
                string? geocodeStatus = null;
                string? geocodeError = null;
                string? placesStatus = null;
                string? placesError = null;

                try
                {
                    var resp = await http.GetFromJsonAsync<GoogleGeocodeResponse>(geocodeUrl);
                    geocodeStatus = resp?.Status;
                    geocodeError = resp?.ErrorMessage;

                    if (resp is not null && resp.Status == "OK" && resp.Results.Length > 0)
                    {
                        best = resp.Results[0];
                    }
                    else if (resp is not null && resp.Status != "OK" && resp.Status != "ZERO_RESULTS")
                    {
                        logger.LogWarning("Google Geocoding returned status {Status}: {Error}", resp.Status, resp.ErrorMessage);
                    }
                }
                catch (Exception ex)
                {
                    geocodeError = ex.Message;
                    logger.LogError(ex, "Google geocoding request failed");
                }

                // --- Attempt 2: Places Text Search fallback ---------------------------
                // Used when Geocoding says ZERO_RESULTS (or fails). Text Search is what
                // the Maps search box uses and handles rural / partial addresses much
                // better — at the cost of a slightly more expensive API call.
                if (best is null)
                {
                    var placesUrl = $"https://maps.googleapis.com/maps/api/place/textsearch/json?query={encodedAddress}&region=us&key={apiKey}";

                    try
                    {
                        var placesResp = await http.GetFromJsonAsync<GoogleGeocodeResponse>(placesUrl);
                        placesStatus = placesResp?.Status;
                        placesError = placesResp?.ErrorMessage;

                        if (placesResp is not null && placesResp.Status == "OK" && placesResp.Results.Length > 0)
                        {
                            best = placesResp.Results[0];
                        }
                        else if (placesResp is not null && placesResp.Status != "OK" && placesResp.Status != "ZERO_RESULTS")
                        {
                            logger.LogWarning(
                                "Google Places Text Search returned status {Status}: {Error}",
                                placesResp.Status, placesResp.ErrorMessage);
                        }
                    }
                    catch (Exception ex)
                    {
                        placesError = ex.Message;
                        logger.LogError(ex, "Google Places Text Search request failed");
                    }
                }

                if (best is null)
                {
                    // Include Google's status codes in the response so the client can
                    // surface them — makes it easy to tell whether the key is
                    // misconfigured, the API isn't enabled, or the address truly
                    // doesn't exist in Google's index.
                    return Results.Json(
                        new
                        {
                            Query = rawAddress,
                            GeocodeStatus = geocodeStatus,
                            GeocodeError = geocodeError,
                            PlacesStatus = placesStatus,
                            PlacesError = placesError
                        },
                        statusCode: 404);
                }

                return Results.Ok(new GeocodingResultDto
                {
                    Latitude = best.Geometry.Location.Lat,
                    Longitude = best.Geometry.Location.Lng,
                    DisplayName = string.IsNullOrWhiteSpace(best.FormattedAddress) ? rawAddress : best.FormattedAddress
                });
            });

            return app;
        }
    }
}
