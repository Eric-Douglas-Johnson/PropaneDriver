namespace PropaneDriver.Shared.Dtos
{
    public class FuelPriceSnapshotDto
    {
        // Empty when the EIA API key is not configured or every series
        // fetch failed — the home page hides the price strip in that case
        // instead of showing an error.
        public List<FuelPriceDto> Prices { get; set; } = new();
    }

    public class FuelPriceDto
    {
        public string FuelName { get; set; } = string.Empty;

        // Week the survey price applies to. The residential propane and
        // heating oil surveys only run October through mid-March, so during
        // the off-season this is the final week of the previous season.
        public DateOnly PriceDate { get; set; }

        public decimal PricePerGallon { get; set; }

        // Latest price minus the prior week's price; null when EIA only
        // returned a single data point for the series.
        public decimal? ChangeFromPriorWeek { get; set; }
    }
}
