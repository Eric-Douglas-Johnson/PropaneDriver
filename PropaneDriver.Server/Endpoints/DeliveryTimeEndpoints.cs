using Microsoft.EntityFrameworkCore;
using PropaneDriver.Server.Data;
using PropaneDriver.Shared.Dtos;

namespace PropaneDriver.Server.Endpoints
{
    public static class DeliveryTimeEndpoints
    {
        public static IEndpointRouteBuilder MapDeliveryTimeEndpoints(this IEndpointRouteBuilder app)
        {
            var group = app.MapGroup("api/delivery-times");

            // Store a delivery time record and refresh the address average.
            group.MapPost("", async (
                DeliveryTimeDto dto,
                PropaneDriverDbContext db,
                ILogger<Program> logger) =>
            {
                if (dto.AddressId == Guid.Empty)
                    return Results.BadRequest(new { Message = "AddressId is required." });

                var address = await db.Addresses.FindAsync(dto.AddressId);
                if (address is null)
                    return Results.BadRequest(new { Message = $"Address {dto.AddressId} not found." });

                try
                {
                    var entity = new DeliveryTimeDbRecord
                    {
                        DeliveryId = dto.DeliveryId,
                        AddressId = dto.AddressId,
                        TimeIntervalSeconds = dto.TimeIntervalSeconds,
                        RecordedAt = DateTime.UtcNow
                    };

                    db.DeliveryTimes.Add(entity);
                    await db.SaveChangesAsync();

                    // Recompute the stored average for this address.
                    var times = await db.DeliveryTimes
                        .Where(t => t.AddressId == dto.AddressId)
                        .Select(t => t.TimeIntervalSeconds)
                        .ToListAsync();

                    times.Sort();
                    if (times.Count > 4)
                    {
                        times.RemoveAt(times.Count - 1);
                        times.RemoveAt(0);
                    }

                    // Stored average is in minutes; recorded intervals are seconds.
                    address.AvgDeliveryTimeMinutes = times.Count > 0 ? times.Average() / 60.0 : 0;
                    await db.SaveChangesAsync();

                    logger.LogInformation("Saved delivery time Id={Id} for Address={AddressId}", entity.Id, dto.AddressId);
                    return Results.Ok(new { entity.Id, entity.RecordedAt });
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Failed to save delivery time for AddressId={AddressId}", dto.AddressId);
                    return Results.Problem(
                        detail: ex.Message,
                        title: "Failed to save delivery time",
                        statusCode: 500);
                }
            });

            // Get average delivery time for an address by its ID.
            group.MapGet("average", async (Guid addressId, PropaneDriverDbContext db) =>
            {
                if (addressId == Guid.Empty)
                    return Results.BadRequest(new { Message = "addressId is required." });

                var address = await db.Addresses.FindAsync(addressId);
                if (address is null)
                    return Results.NotFound(new { Message = $"Address {addressId} not found." });

                return Results.Ok(new
                {
                    AddressId = addressId,
                    address.AvgDeliveryTimeMinutes,
                    address.Street,
                    address.City,
                    address.State,
                    address.ZipCode
                });
            });

            // Aggregate stats over the whole DeliveryTime table, used by the
            // admin Tools page. Optional `from` / `to` bound the window
            // (RecordedAt is UTC). Everything is computed in-process; the
            // table is small enough that an ORDER BY + pull-to-memory beats
            // shipping a half-dozen aggregate queries to SQL.
            group.MapGet("stats", async (
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

                // Join to Addresses by id to pull the address columns the stats
                // rows need (no navigation property on DeliveryTimeDbRecord).
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

                var stats = new DeliveryTimeStatsDto { SampleCount = records.Count };

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

                // Group by address for the leaderboards. A driver who runs
                // the same route weekly will hit the same handful of stops
                // many times, so this surfaces the operationally meaningful
                // ones rather than one-offs.
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

        private record DeliveryTimeStatsRow(
            Guid AddressId,
            double TimeIntervalSeconds,
            DateTime RecordedAt,
            string Street,
            string City,
            string State);

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
