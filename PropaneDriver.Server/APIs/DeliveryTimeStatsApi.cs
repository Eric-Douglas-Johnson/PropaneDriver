
using Microsoft.EntityFrameworkCore;
using PropaneDriver.Server.Data;
using PropaneDriver.Shared.Dtos;

namespace PropaneDriver.Server.Endpoints
{
    public static class DeliveryTimeStatsApi
    {
        private record DeliveryTimeStatsRow(
            Guid AddressId,
            double TimeIntervalSeconds,
            DateTime RecordedAt,
            string Street,
            string City,
            string State);

        public static IEndpointRouteBuilder MapDeliveryTimeStatsEndpoints(this IEndpointRouteBuilder app)
        {
            var group = app.MapGroup("api/delivery-time-stats");

            // Aggregate delivery time statistics, optionally bounded by recorded date
            group.MapGet("", async (
                DateTime? from,
                DateTime? to,
                PropaneDriverDbContext db) =>
            {
                var query = db.DeliveryTimes.AsQueryable();

                if (from.HasValue)
                {
                    var fromUtc = from.Value.Kind == DateTimeKind.Utc ? from.Value : from.Value.ToUniversalTime();
                    query = query.Where(deliveryTime => deliveryTime.RecordedAt >= fromUtc);
                }
                if (to.HasValue)
                {
                    var toUtc = to.Value.Kind == DateTimeKind.Utc ? to.Value : to.Value.ToUniversalTime();
                    query = query.Where(deliveryTime => deliveryTime.RecordedAt <= toUtc);
                }

                var records = await query
                    .Join(db.Addresses,
                        deliveryTime => deliveryTime.AddressId,
                        address => address.Id,
                        (deliveryTime, address) => new DeliveryTimeStatsRow(
                            deliveryTime.AddressId,
                            deliveryTime.TimeIntervalSeconds,
                            deliveryTime.RecordedAt,
                            address.Street,
                            address.City,
                            address.State))
                    .ToListAsync();

                var stats = new DeliveryTimeStatsApiDto { SampleCount = records.Count };

                if (records.Count == 0)
                    return Results.Ok(stats);

                var sortedSeconds = records
                    .Select(record => record.TimeIntervalSeconds)
                    .OrderBy(seconds => seconds)
                    .ToArray();

                stats.OldestRecordedAt = records.Min(record => record.RecordedAt);
                stats.NewestRecordedAt = records.Max(record => record.RecordedAt);
                stats.MinimumSeconds = sortedSeconds[0];
                stats.MaximumSeconds = sortedSeconds[^1];
                stats.TotalSeconds = sortedSeconds.Sum();
                stats.MeanSeconds = sortedSeconds.Average();
                stats.MedianSeconds = ComputeMedian(sortedSeconds);
                stats.StandardDeviationSeconds = ComputeStandardDeviation(sortedSeconds, stats.MeanSeconds);

                // Five buckets in minutes. The last is unbounded so very slow
                // outliers (bulk fills, problem stops) all land in one place.
                var bucketLowerEdgesMinutes = new[] { 0, 2, 5, 10, 20 };
                for (var bucketIndex = 0; bucketIndex < bucketLowerEdgesMinutes.Length; bucketIndex++)
                {
                    var lowerMinutes = bucketLowerEdgesMinutes[bucketIndex];
                    int? upperMinutes = bucketIndex == bucketLowerEdgesMinutes.Length - 1
                        ? null
                        : bucketLowerEdgesMinutes[bucketIndex + 1];

                    var lowerSeconds = lowerMinutes * 60.0;
                    var upperSeconds = upperMinutes.HasValue ? upperMinutes.Value * 60.0 : double.PositiveInfinity;

                    var bucketCount = records.Count(record =>
                        record.TimeIntervalSeconds >= lowerSeconds &&
                        record.TimeIntervalSeconds < upperSeconds);

                    stats.Distribution.Add(new DeliveryTimeDistributionBucketDto
                    {
                        Label = upperMinutes.HasValue
                            ? $"{lowerMinutes}–{upperMinutes} min"
                            : $"{lowerMinutes}+ min",
                        Count = bucketCount,
                        Percentage = bucketCount * 100.0 / records.Count
                    });
                }

                var perAddressStats = records
                    .GroupBy(record => record.AddressId)
                    .Select(addressGroup =>
                    {
                        var sortedTimesForAddress = addressGroup
                            .Select(record => record.TimeIntervalSeconds)
                            .OrderBy(seconds => seconds)
                            .ToArray();

                        var firstRowForAddress = addressGroup.First();

                        return new DeliveryTimeAddressStatDto
                        {
                            AddressId = addressGroup.Key,
                            Street = firstRowForAddress.Street,
                            City = firstRowForAddress.City,
                            State = firstRowForAddress.State,
                            SampleCount = sortedTimesForAddress.Length,
                            MeanSeconds = sortedTimesForAddress.Average(),
                            MedianSeconds = ComputeMedian(sortedTimesForAddress)
                        };
                    })
                    .ToList();

                // Slowest only includes addresses with at least 2 samples so
                // a single bad reading can't dominate the leaderboard.
                stats.SlowestAddresses = perAddressStats
                    .Where(addressStat => addressStat.SampleCount >= 2)
                    .OrderByDescending(addressStat => addressStat.MeanSeconds)
                    .Take(10)
                    .ToList();

                stats.MostFrequentAddresses = perAddressStats
                    .OrderByDescending(addressStat => addressStat.SampleCount)
                    .ThenByDescending(addressStat => addressStat.MeanSeconds)
                    .Take(10)
                    .ToList();

                return Results.Ok(stats);
            }).RequireAuthorization("AdminOnly");

            return app;
        }

        private static double ComputeMedian(IReadOnlyList<double> sortedValues)
        {
            if (sortedValues.Count == 0) return 0;
            var middleIndex = sortedValues.Count / 2;
            return sortedValues.Count % 2 == 1
                ? sortedValues[middleIndex]
                : (sortedValues[middleIndex - 1] + sortedValues[middleIndex]) / 2.0;
        }

        private static double ComputeStandardDeviation(IReadOnlyList<double> values, double mean)
        {
            if (values.Count < 2) return 0;

            double sumOfSquaredDeviations = 0;

            foreach (var value in values)
            {
                var deviation = value - mean;
                sumOfSquaredDeviations += deviation * deviation;
            }

            return Math.Sqrt(sumOfSquaredDeviations / (values.Count - 1));
        }
    }
}
