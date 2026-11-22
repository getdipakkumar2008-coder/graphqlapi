using System.Text.Json;

namespace WeatherApi;

public sealed class SeedWeatherProvider : IWeatherProvider
{
    private readonly SeedRecord[] records;
    private readonly TimeProvider clock;

    public SeedWeatherProvider(string path, TimeProvider? clock = null)
    {
        using var stream = File.OpenRead(path);
        records = JsonSerializer.Deserialize<SeedData>(stream, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })?.Locations
            ?? throw new InvalidDataException("Seed data must contain locations.");
        this.clock = clock ?? TimeProvider.System;
    }

    private static Location ToLocation(SeedRecord record) => new(record.Name, record.Country, record.Timezone);
    private static bool Equal(string a, string b) => string.Equals(a.Trim(), b.Trim(), StringComparison.OrdinalIgnoreCase);
    private SeedRecord Find(Location location) => records.First(r => r.Name == location.Name && r.Country == location.Country);
    private DateOnly Today(SeedRecord record) =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(clock.GetUtcNow(), TimeZoneInfo.FindSystemTimeZoneById(record.Timezone)).DateTime);
    private static string Date(DateOnly date) => date.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);

    public Task<IReadOnlyList<Location>> Locations(CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<Location>>(records.Select(ToLocation).ToArray());

    public Task<IReadOnlyList<Location>> SearchLocations(string name, int count, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<Location>>(records.Where(r => r.Name.Contains(name.Trim(), StringComparison.OrdinalIgnoreCase)).Take(count).Select(ToLocation).ToArray());

    public Task<Location> Location(string city, string? country, CancellationToken cancellationToken)
    {
        var matches = records.Where(r => Equal(r.Name, city) && (country is null || Equal(r.Country, country))).ToArray();
        if (matches.Length == 0)
            throw new WeatherProviderException($"No weather data for {(country is null ? city : $"{city}, {country}")}. Query \"locations\" for available cities.");
        if (matches.Length > 1)
            throw new WeatherProviderException($"Multiple locations named {city}; specify a country");
        return Task.FromResult(ToLocation(matches[0]));
    }

    public Task<CurrentWeather> Current(Location location, CancellationToken cancellationToken) => Task.FromResult(Find(location).Current);

    public Task<IReadOnlyList<ForecastDay>> Forecast(Location location, int days, CancellationToken cancellationToken)
    {
        var record = Find(location);
        var today = Today(record);
        return Task.FromResult<IReadOnlyList<ForecastDay>>(record.Forecast.Take(days)
            .Select((day, i) => new ForecastDay(Date(today.AddDays(i + 1)), day.MinTemperatureC, day.MaxTemperatureC, day.Condition)).ToArray());
    }

    public Task<AirQuality?> AirQuality(Location location, CancellationToken cancellationToken) => Task.FromResult(Find(location).AirQuality);

    public Task<IReadOnlyList<WeatherAlert>> Alerts(Location location, CancellationToken cancellationToken)
    {
        var record = Find(location);
        var today = Today(record);
        return Task.FromResult<IReadOnlyList<WeatherAlert>>(record.Alerts.Select(a =>
            new WeatherAlert(a.Title, a.Severity, Date(today.AddDays(a.StartDayOffset)), Date(today.AddDays(a.EndDayOffset)))).ToArray());
    }

    private sealed record SeedData(SeedRecord[] Locations);
    private sealed record SeedRecord(string Name, string Country, string Timezone, CurrentWeather Current, SeedForecast[] Forecast, AirQuality? AirQuality, SeedAlert[] Alerts);
    private sealed record SeedForecast(double MinTemperatureC, double MaxTemperatureC, string Condition);
    private sealed record SeedAlert(string Title, string Severity, int StartDayOffset, int EndDayOffset);
}
