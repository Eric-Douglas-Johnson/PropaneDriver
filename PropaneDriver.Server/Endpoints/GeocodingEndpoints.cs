using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using PropaneDriver.Server.Data;
using PropaneDriver.Server.Services;
using PropaneDriver.Shared.Dtos;

namespace PropaneDriver.Server.Endpoints
{
    public static class GeocodingEndpoints
    {
        public static IEndpointRouteBuilder MapGeocodingEndpoints(this IEndpointRouteBuilder app)
        {
            // Resolve an address to coordinates + parsed fields.
            //
            //   1. Check the Addresses table first (strict case-insensitive
            //      match). Saves a Google call and, more importantly, re-uses
            //      the Id so downstream code can link Deliveries/DeliveryTimes
            //      to an existing Address row.
            //   2. Fall through to Google Geocoding API, then Places Text
            //      Search on ZERO_RESULTS (rural addresses like the ones in
            //      our service area get better coverage from Text Search).
            //   3. Either way, return Street/City/State/Zip/Lat/Lon so the
            //      Admin form can auto-fill every input on one round-trip.
            app.MapGet("api/geocode", async (
                string? street, string? city, string? state, string? zip,
                PropaneDriverDbContext db,
                IHttpClientFactory httpFactory,
                IConfiguration config,
                ILogger<Program> logger) =>
            {
                var trimmedStreet = street?.Trim() ?? string.Empty;
                var trimmedCity = city?.Trim() ?? string.Empty;
                var trimmedState = state?.Trim() ?? string.Empty;
                var trimmedZip = zip?.Trim() ?? string.Empty;

                if (string.IsNullOrWhiteSpace(trimmedStreet)
                    && string.IsNullOrWhiteSpace(trimmedCity)
                    && string.IsNullOrWhiteSpace(trimmedState)
                    && string.IsNullOrWhiteSpace(trimmedZip))
                {
                    return Results.BadRequest(new { Message = "No address parts provided." });
                }

                // --- Attempt 0: existing row in the Addresses table ------------------
                // Strict, case-insensitive match. When all four fields are present we
                // can hit the UQ_Addresses_Location unique index; when only Street is
                // given we settle for the first match (bounded by the non-unique
                // IX_Addresses_Location index on the same columns).
                var dbMatch = await FindExistingAddressAsync(
                    db, trimmedStreet, trimmedCity, trimmedState, trimmedZip);

                if (dbMatch is not null)
                {
                    return Results.Ok(new GeocodingResultDto
                    {
                        Latitude = dbMatch.Latitude,
                        Longitude = dbMatch.Longitude,
                        Street = dbMatch.Street,
                        City = dbMatch.City,
                        State = dbMatch.State,
                        ZipCode = dbMatch.ZipCode,
                        DisplayName = $"{dbMatch.Street}, {dbMatch.City}, {dbMatch.State} {dbMatch.ZipCode}",
                        Source = "Database"
                    });
                }

                // --- Attempt 1 + 2: Google --------------------------------------------
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
                if (!string.IsNullOrWhiteSpace(trimmedStreet)) parts.Add(trimmedStreet);
                if (!string.IsNullOrWhiteSpace(trimmedCity)) parts.Add(trimmedCity);
                if (!string.IsNullOrWhiteSpace(trimmedState)) parts.Add(trimmedState);
                if (!string.IsNullOrWhiteSpace(trimmedZip)) parts.Add(trimmedZip);

                var rawAddress = string.Join(", ", parts);
                var encodedAddress = Uri.EscapeDataString(rawAddress);
                var http = httpFactory.CreateClient();

                GoogleGeocodeResult? best = null;
                string? geocodeStatus = null;
                string? geocodeError = null;
                string? placesStatus = null;
                string? placesError = null;

                var geocodeUrl = $"https://maps.googleapis.com/maps/api/geocode/json?address={encodedAddress}&region=us&key={apiKey}";

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

                // Places Text Search fallback — handles rural/partial addresses that
                // Geocoding returns ZERO_RESULTS on. Slightly more expensive per call,
                // but only invoked when Geocoding actually failed.
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

                // Parse the structured components so the client can fill every field.
                // Fallback to the user's original input if a component is missing —
                // Places Text Search sometimes returns results without full
                // address_components.
                var parsed = ParseAddressComponents(best.AddressComponents);

                return Results.Ok(new GeocodingResultDto
                {
                    Latitude = best.Geometry.Location.Lat,
                    Longitude = best.Geometry.Location.Lng,
                    DisplayName = string.IsNullOrWhiteSpace(best.FormattedAddress) ? rawAddress : best.FormattedAddress,
                    Street = string.IsNullOrWhiteSpace(parsed.Street) ? trimmedStreet : parsed.Street,
                    City = string.IsNullOrWhiteSpace(parsed.City) ? trimmedCity : parsed.City,
                    State = string.IsNullOrWhiteSpace(parsed.State) ? trimmedState : parsed.State,
                    ZipCode = string.IsNullOrWhiteSpace(parsed.Zip) ? trimmedZip : parsed.Zip,
                    Source = "Google"
                });
            });

            return app;
        }

        // Strict case-insensitive match against the Addresses table. When all
        // four fields are present we match on the unique location index; with
        // only a street we take the first row (most-recent GUID isn't
        // meaningful, so "any matching row" is close enough — duplicates on
        // street alone across different cities are unlikely for a single-
        // company delivery area).
        private static async Task<AddressDbRecord?> FindExistingAddressAsync(
            PropaneDriverDbContext db,
            string street,
            string city,
            string state,
            string zip)
        {
            if (string.IsNullOrWhiteSpace(street))
                return null;

            var query = db.Addresses.AsNoTracking()
                .Where(a => EF.Functions.Collate(a.Street, "SQL_Latin1_General_CP1_CI_AS") == street);

            if (!string.IsNullOrWhiteSpace(city))
                query = query.Where(a => EF.Functions.Collate(a.City, "SQL_Latin1_General_CP1_CI_AS") == city);
            if (!string.IsNullOrWhiteSpace(state))
                query = query.Where(a => EF.Functions.Collate(a.State, "SQL_Latin1_General_CP1_CI_AS") == state);
            if (!string.IsNullOrWhiteSpace(zip))
                query = query.Where(a => EF.Functions.Collate(a.ZipCode, "SQL_Latin1_General_CP1_CI_AS") == zip);

            return await query.FirstOrDefaultAsync();
        }

        // Map Google's address_components array into our four flat fields.
        // Google's schema: each component carries a "types" array; we pick
        // the single component matching each role. Street is assembled from
        // street_number + route because Google returns them separately.
        private static (string Street, string City, string State, string Zip) ParseAddressComponents(
            GoogleAddressComponent[] components)
        {
            string streetNumber = string.Empty;
            string route = string.Empty;
            string city = string.Empty;
            string stateShort = string.Empty;
            string zip = string.Empty;

            foreach (var c in components)
            {
                if (c.Types.Contains("street_number"))
                    streetNumber = c.LongName;
                else if (c.Types.Contains("route"))
                    route = c.LongName;
                else if (c.Types.Contains("locality"))
                    city = c.LongName;
                // Some rural addresses have no "locality" — Google tags them as
                // "sublocality" or "administrative_area_level_3" instead. Fall
                // back to those only if we don't already have a locality match.
                else if (c.Types.Contains("sublocality") && string.IsNullOrEmpty(city))
                    city = c.LongName;
                else if (c.Types.Contains("administrative_area_level_3") && string.IsNullOrEmpty(city))
                    city = c.LongName;
                else if (c.Types.Contains("administrative_area_level_1"))
                    stateShort = c.ShortName; // "MN" rather than "Minnesota"
                else if (c.Types.Contains("postal_code"))
                    zip = c.LongName;
            }

            var street = string.Join(" ",
                new[] { streetNumber, route }.Where(s => !string.IsNullOrWhiteSpace(s)));

            return (street, city, stateShort, zip);
        }
    }
}
