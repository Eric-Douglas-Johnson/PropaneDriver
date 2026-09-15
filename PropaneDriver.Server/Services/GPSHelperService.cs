using PropaneDriver.Server.Data;

namespace PropaneDriver.Server.Services
{
    // Synchronous, self-contained route-time estimator. Kept static + pure so
    // route creation stays fast and deterministic — no external API calls
    // (Google Directions would need an HTTP round-trip per leg).
    public static class GPSHelperService
    {
        // When a delivery has no historical average stored, assume the driver
        // spends this long at the stop.
        private const double DEFAULT_DELIVERY_MINUTES = 10.0;

        // Straight-line (great-circle) distance underestimates real driving
        // distance. ~1.3x is a standard road-network winding factor used for
        // back-of-envelope routing estimates.
        private const double ROAD_WINDING_FACTOR = 1.3;

        // Average driving speed across mixed road types
        private const double AVG_MPH = 40.0;

        private const double EARTH_RADIUS_IN_MILES = 3958.7613;

        // Returns the estimated total route time in whole minutes:
        // sum(delivery servicing time)  +  sum(drive time between stops)
        public static async Task<int> GetEstimatedRouteTime(List<DeliveryDbRecord> deliveries, List<AddressDbRecord> addresses)
        {
            if (deliveries is null || deliveries.Count == 0)
                return 0;

            var addressDictionary = addresses.ToDictionary(a => a.Id);
            var orderedDeliveries = deliveries.OrderBy(d => d.SortOrder).ToList();

            double deliveryMinutes = 0;
            double avgMinutesForAddress = 0;

            foreach (var delivery in orderedDeliveries)
            {
                avgMinutesForAddress = addressDictionary.TryGetValue(delivery.AddressId, out var addr)
                    ? addr.AvgDeliveryTimeMinutes
                    : 0;

                if (avgMinutesForAddress > 0)
                {
                    deliveryMinutes += avgMinutesForAddress;
                }
                else
                {
                    deliveryMinutes += DEFAULT_DELIVERY_MINUTES;
                }
            }

            double driveMinutes = 0;

            for (int i = 1; i < orderedDeliveries.Count; i++)
            {
                var previousDelivery = orderedDeliveries[i - 1];
                var currentDelivery = orderedDeliveries[i];

                if (!addressDictionary.TryGetValue(previousDelivery.AddressId, out var previousAddress) ||
                    !addressDictionary.TryGetValue(currentDelivery.AddressId, out var currentAddress))
                {
                    continue;
                }

                // Stops with unset coordinates contribute their servicing time but no
                // drive leg, so a missing pin can't add a cross-ocean leg through (0,0).
                if (!HasCoordinates(previousAddress) || !HasCoordinates(currentAddress))
                {
                    continue;
                }

                driveMinutes += EstimateDriveMinutes(
                    previousAddress.Latitude.GetValueOrDefault(), previousAddress.Longitude.GetValueOrDefault(),
                    currentAddress.Latitude.GetValueOrDefault(), currentAddress.Longitude.GetValueOrDefault());
            }

            return (int)Math.Round(deliveryMinutes + driveMinutes);
        }

        private static bool HasCoordinates(AddressDbRecord address)
            => address.Latitude.GetValueOrDefault() != 0 || address.Longitude.GetValueOrDefault() != 0;

        private static double EstimateDriveMinutes(double lat1, double lng1, double lat2, double lng2)
        {
            var miles = HaversineMiles(lat1, lng1, lat2, lng2) * ROAD_WINDING_FACTOR;
            return miles / AVG_MPH * 60.0;
        }

        private static double HaversineMiles(double lat1, double lng1, double lat2, double lng2)
        {
            var dLat = DegreesToRadians(lat2 - lat1);
            var dLng = DegreesToRadians(lng2 - lng1);

            var a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2)
                  + Math.Cos(DegreesToRadians(lat1)) * Math.Cos(DegreesToRadians(lat2))
                  * Math.Sin(dLng / 2) * Math.Sin(dLng / 2);

            var c = 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));

            return EARTH_RADIUS_IN_MILES * c;
        }

        private static double DegreesToRadians(double deg) => deg * Math.PI / 180.0;
    }
}
