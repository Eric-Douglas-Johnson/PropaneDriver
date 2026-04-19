namespace PropaneDriver.Tests;

// GeocodingEndpoints.cs is mostly I/O against Google's APIs, which we don't
// want to exercise in unit tests. What's worth pinning down is the pure
// input-assembly logic: how the endpoint concatenates street/city/state/zip
// into the query string, and the whitespace/null handling.
public class GeocodingEndpointsTests
{
    // Mirrors the address-part assembly block at the top of the handler.
    private static string? BuildAddressQuery(string? street, string? city, string? state, string? zip)
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(street)) parts.Add(street.Trim());
        if (!string.IsNullOrWhiteSpace(city)) parts.Add(city.Trim());
        if (!string.IsNullOrWhiteSpace(state)) parts.Add(state.Trim());
        if (!string.IsNullOrWhiteSpace(zip)) parts.Add(zip.Trim());

        return parts.Count == 0 ? null : string.Join(", ", parts);
    }

    [Fact]
    public void BuildAddressQuery_AllParts_CommaSeparated()
    {
        var q = BuildAddressQuery("100 Analytic Way", "Hibbing", "MN", "55746");
        Assert.Equal("100 Analytic Way, Hibbing, MN, 55746", q);
    }

    [Fact]
    public void BuildAddressQuery_TrimsEachPart()
    {
        var q = BuildAddressQuery("  100 Main  ", " Hibbing ", " MN ", " 55746 ");
        Assert.Equal("100 Main, Hibbing, MN, 55746", q);
    }

    [Fact]
    public void BuildAddressQuery_SkipsNullAndBlank()
    {
        var q = BuildAddressQuery(null, "Hibbing", "", "55746");
        Assert.Equal("Hibbing, 55746", q);
    }

    [Fact]
    public void BuildAddressQuery_NothingProvided_ReturnsNull()
    {
        // Endpoint returns BadRequest when no parts are supplied.
        Assert.Null(BuildAddressQuery(null, null, null, null));
        Assert.Null(BuildAddressQuery("", " ", "\t", null));
    }

    [Fact]
    public void BuildAddressQuery_SingleFieldOnly_StillValid()
    {
        Assert.Equal("55746", BuildAddressQuery(null, null, null, "55746"));
        Assert.Equal("6565 Dewey Lake Shores Rd", BuildAddressQuery("6565 Dewey Lake Shores Rd", null, null, null));
    }

    [Fact]
    public void UriEscape_PreservesSpacesAsPercent20()
    {
        var q = BuildAddressQuery("100 Analytic Way", "Hibbing", "MN", "55746");
        Assert.NotNull(q);
        var encoded = Uri.EscapeDataString(q!);

        // Google expects %20 (or +) for spaces in the address parameter.
        Assert.DoesNotContain(" ", encoded);
        Assert.Contains("%20", encoded);
        Assert.Contains("%2C", encoded); // comma
    }
}
