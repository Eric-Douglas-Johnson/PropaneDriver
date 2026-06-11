using PropaneDriver.Server.Services;

namespace PropaneDriver.Server.Endpoints
{
    public static class FuelPriceEndpoints
    {
        public static IEndpointRouteBuilder MapFuelPriceEndpoints(this IEndpointRouteBuilder app)
        {
            // National weekly fuel price averages for the home page. The
            // home page renders before login, so this stays anonymous; the
            // data is public EIA market data and the service caches it, so
            // there's nothing sensitive or expensive to protect.
            app.MapGet("api/fuel-prices", async (EiaFuelPriceService fuelPriceService) =>
                Results.Ok(await fuelPriceService.GetSnapshotAsync()));

            return app;
        }
    }
}
