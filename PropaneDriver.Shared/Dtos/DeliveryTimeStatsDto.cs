namespace PropaneDriver.Shared.Dtos
{
    // Aggregate statistics over the DeliveryTime table for the admin
    // Tools page. All durations are seconds; the client formats them
    // for display.
    public class DeliveryTimeStatsDto
    {
        public int SampleCount { get; set; }

        public System.DateTime? OldestRecordedAt { get; set; }
        public System.DateTime? NewestRecordedAt { get; set; }

        public double MeanSeconds { get; set; }
        public double MedianSeconds { get; set; }
        public double StandardDeviationSeconds { get; set; }
        public double MinimumSeconds { get; set; }
        public double MaximumSeconds { get; set; }
        public double TotalSeconds { get; set; }

        public System.Collections.Generic.List<DeliveryTimeDistributionBucketDto> Distribution { get; set; } = new();
        public System.Collections.Generic.List<DeliveryTimeAddressStatDto> SlowestAddresses { get; set; } = new();
        public System.Collections.Generic.List<DeliveryTimeAddressStatDto> MostFrequentAddresses { get; set; } = new();
    }

    public class DeliveryTimeDistributionBucketDto
    {
        public string Label { get; set; } = string.Empty;
        public int Count { get; set; }
        public double Percentage { get; set; }
    }

    public class DeliveryTimeAddressStatDto
    {
        public System.Guid AddressId { get; set; }
        public string Street { get; set; } = string.Empty;
        public string City { get; set; } = string.Empty;
        public string State { get; set; } = string.Empty;
        public int SampleCount { get; set; }
        public double MeanSeconds { get; set; }
        public double MedianSeconds { get; set; }
    }
}
