using System.Net;
using System.Net.Http.Json;
using PropaneDriver.Shared.Dtos;

namespace PropaneDriver.Tests.Integration;

// Past fuel logs: Start new log completes the open log into history, drivers
// see only their own logs, and supervisors/admins can review everyone's.
public class FuelLogHistoryTests : IClassFixture<PropaneDriverWebAppFactory>
{
    private readonly PropaneDriverWebAppFactory _factory;

    public FuelLogHistoryTests(PropaneDriverWebAppFactory factory)
    {
        _factory = factory;
    }

    private static SaveFuelLogDto StartRowPlus(params (string EquipmentNumber, decimal MeterValue, decimal GallonsPumped)[] fillRows)
    {
        var saveDto = new SaveFuelLogDto();
        saveDto.Entries.Add(new FuelLogEntryDto { EquipmentNumber = "Start", MeterValue = 1000m });
        foreach (var fillRow in fillRows)
        {
            saveDto.Entries.Add(new FuelLogEntryDto
            {
                EquipmentNumber = fillRow.EquipmentNumber,
                MeterValue = fillRow.MeterValue,
                GallonsPumped = fillRow.GallonsPumped
            });
        }
        return saveDto;
    }

    [Fact]
    public async Task StartNew_CompletesOpenLogIntoHistory()
    {
        var driver = _factory.SeedUser("fuel-history-driver", role: "driver");
        using var client = _factory.CreateClientForUser(driver);

        var saveResponse = await client.PutAsJsonAsync("/api/fuel-log",
            StartRowPlus(("104", 1020m, 20m), ("105", 1050m, 30m)));
        Assert.Equal(HttpStatusCode.OK, saveResponse.StatusCode);

        var startNewResponse = await client.PostAsync("/api/fuel-log/start-new", content: null);
        Assert.Equal(HttpStatusCode.OK, startNewResponse.StatusCode);

        var currentEntries = await client.GetFromJsonAsync<List<FuelLogEntryDto>>("/api/fuel-log");
        Assert.Empty(currentEntries!);

        var history = await client.GetFromJsonAsync<List<FuelLogSummaryDto>>("/api/fuel-log/history");
        var pastLog = Assert.Single(history!);
        Assert.NotNull(pastLog.CompletedAt);
        Assert.Equal(2, pastLog.EntryCount);
        Assert.Equal(50m, pastLog.TotalGallons);

        var pastEntries = await client.GetFromJsonAsync<List<FuelLogEntryDto>>($"/api/fuel-log/logs/{pastLog.Id}");
        Assert.Equal(new[] { "Start", "104", "105" }, pastEntries!.Select(entry => entry.EquipmentNumber));
    }

    [Fact]
    public async Task StartNew_WithOnlyStartRow_KeepsNoHistory()
    {
        var driver = _factory.SeedUser("fuel-history-blank-driver", role: "driver");
        using var client = _factory.CreateClientForUser(driver);

        await client.PutAsJsonAsync("/api/fuel-log", StartRowPlus());
        var startNewResponse = await client.PostAsync("/api/fuel-log/start-new", content: null);
        Assert.Equal(HttpStatusCode.OK, startNewResponse.StatusCode);

        var history = await client.GetFromJsonAsync<List<FuelLogSummaryDto>>("/api/fuel-log/history");
        Assert.Empty(history!);
    }

    [Fact]
    public async Task Save_AfterStartNew_LeavesPastLogUntouched()
    {
        var driver = _factory.SeedUser("fuel-history-resave-driver", role: "driver");
        var pastLog = _factory.SeedFuelLog(driver.Id, completed: true);
        using var client = _factory.CreateClientForUser(driver);

        await client.PutAsJsonAsync("/api/fuel-log", StartRowPlus(("200", 1010m, 10m)));

        var pastEntries = await client.GetFromJsonAsync<List<FuelLogEntryDto>>($"/api/fuel-log/logs/{pastLog.Id}");
        Assert.Equal(new[] { "Start", "T-100" }, pastEntries!.Select(entry => entry.EquipmentNumber));

        var currentEntries = await client.GetFromJsonAsync<List<FuelLogEntryDto>>("/api/fuel-log");
        Assert.Equal(new[] { "Start", "200" }, currentEntries!.Select(entry => entry.EquipmentNumber));
    }

    [Fact]
    public async Task History_ReturnsOnlyTheCallersCompletedLogs()
    {
        var driver = _factory.SeedUser("fuel-history-own-driver", role: "driver");
        var otherDriver = _factory.SeedUser("fuel-history-own-other", role: "driver");
        var ownPastLog = _factory.SeedFuelLog(driver.Id, completed: true);
        _factory.SeedFuelLog(driver.Id);
        _factory.SeedFuelLog(otherDriver.Id, completed: true);
        using var client = _factory.CreateClientForUser(driver);

        var history = await client.GetFromJsonAsync<List<FuelLogSummaryDto>>("/api/fuel-log/history");

        var pastLog = Assert.Single(history!);
        Assert.Equal(ownPastLog.Id, Guid.Parse(pastLog.Id));
    }

    [Fact]
    public async Task LogEntries_OtherDriversLog_Returns403()
    {
        var driver = _factory.SeedUser("fuel-history-snoop-driver", role: "driver");
        var otherDriver = _factory.SeedUser("fuel-history-snoop-other", role: "driver");
        var otherPastLog = _factory.SeedFuelLog(otherDriver.Id, completed: true);
        using var client = _factory.CreateClientForUser(driver);

        var response = await client.GetAsync($"/api/fuel-log/logs/{otherPastLog.Id}");
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task LogEntries_SupervisorRole_CanReadAnyDriversLog()
    {
        var supervisor = _factory.SeedUser("fuel-history-read-supervisor", role: "supervisor");
        var driver = _factory.SeedUser("fuel-history-read-target", role: "driver");
        var pastLog = _factory.SeedFuelLog(driver.Id, completed: true);
        using var client = _factory.CreateClientForUser(supervisor);

        var response = await client.GetAsync($"/api/fuel-log/logs/{pastLog.Id}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task AllLogs_DriverRole_Returns403()
    {
        var driver = _factory.SeedUser("fuel-history-all-driver", role: "driver");
        using var client = _factory.CreateClientForUser(driver);

        var response = await client.GetAsync("/api/fuel-log/all");
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Theory]
    [InlineData("supervisor")]
    [InlineData("admin")]
    public async Task AllLogs_SupervisorOrAdmin_SeesEveryDriversLogs(string callerRole)
    {
        var caller = _factory.SeedUser($"fuel-history-all-{callerRole}", role: callerRole);
        var firstDriver = _factory.SeedUser($"fuel-history-all-{callerRole}-first", role: "driver");
        var secondDriver = _factory.SeedUser($"fuel-history-all-{callerRole}-second", role: "driver");
        var firstDriverPastLog = _factory.SeedFuelLog(firstDriver.Id, completed: true);
        var secondDriverOpenLog = _factory.SeedFuelLog(secondDriver.Id);
        using var client = _factory.CreateClientForUser(caller);

        var allLogs = await client.GetFromJsonAsync<List<FuelLogSummaryDto>>("/api/fuel-log/all");

        var allLogIds = allLogs!.Select(fuelLog => Guid.Parse(fuelLog.Id)).ToList();
        Assert.Contains(firstDriverPastLog.Id, allLogIds);
        Assert.Contains(secondDriverOpenLog.Id, allLogIds);
    }
}
