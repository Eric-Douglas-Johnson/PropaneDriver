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
        private const double DefaultDeliveryMinutes = 10.0;

        // Straight-line (great-circle) distance underestimates real driving
        // distance. ~1.3x is a standard road-network winding factor used for
        // back-of-envelope routing estimates.
        private const double RoadWindingFactor = 1.3;

        // Average driving speed across mixed rural/county roads in the service
        // area. Generous enough to not systematically underestimate, tight
        // enough to not overstate.
        private const double AverageMph = 40.0;

        private const double EarthRadiusMiles = 3958.7613;

        // Returns the estimated total route time in whole minutes:
        //   sum(delivery servicing time)  +  sum(drive time between stops)
        //
        // Deliveries are visited in SortOrder. Drive legs between consecutive
        // stops are estimated from each stop's GPS coordinates. Stops with
        // unset coordinates (0,0) contribute their servicing time but no
        // drive leg — no silent cross-ocean legs.
        public static int GetEstimatedRouteTime(List<DeliveryEntity> deliveryEntities)
        {
            if (deliveryEntities is null || deliveryEntities.Count == 0)
                return 0;

            var ordered = deliveryEntities.OrderBy(d => d.SortOrder).ToList();

            double deliveryMinutes = 0;
            foreach (var d in ordered)
            {
                deliveryMinutes += d.AvgDeliveryTimeMinutes > 0
                    ? d.AvgDeliveryTimeMinutes
                    : DefaultDeliveryMinutes;
            }

            double driveMinutes = 0;
            for (int i = 1; i < ordered.Count; i++)
            {
                var prev = ordered[i - 1];
                var curr = ordered[i];

                if (!HasCoordinates(prev) || !HasCoordinates(curr))
                    continue;

                driveMinutes += EstimateDriveMinutes(
                    prev.Latitude, prev.Longitude,
                    curr.Latitude, curr.Longitude);
            }

            return (int)Math.Round(deliveryMinutes + driveMinutes);
        }

        private static bool HasCoordinates(DeliveryEntity d)
            => d.Latitude != 0 || d.Longitude != 0;

        private static double EstimateDriveMinutes(double lat1, double lng1, double lat2, double lng2)
        {
            var miles = HaversineMiles(lat1, lng1, lat2, lng2) * RoadWindingFactor;
            return miles / AverageMph * 60.0;
        }

        private static double HaversineMiles(double lat1, double lng1, double lat2, double lng2)
        {
            var dLat = DegreesToRadians(lat2 - lat1);
            var dLng = DegreesToRadians(lng2 - lng1);
            var a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2)
                  + Math.Cos(DegreesToRadians(lat1)) * Math.Cos(DegreesToRadians(lat2))
                  * Math.Sin(dLng / 2) * Math.Sin(dLng / 2);
            var c = 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
            return EarthRadiusMiles * c;
        }

        private static double DegreesToRadians(double deg) => deg * Math.PI / 180.0;
    }
}
