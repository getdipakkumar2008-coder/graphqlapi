using System.Globalization;
using System.Text.Json;
using Microsoft.AspNetCore.WebUtilities;

namespace WeatherApi;

public sealed class LiveWeatherProvider(HttpClient http, SeedWeatherProvider seed) : IWeatherProvider
{
    private const string Geocoding = "https://geocoding-api.open-meteo.com/v1/search";
    private const string ForecastUrl = "https://api.open-meteo.com/v1/forecast";
    private const string AirUrl = "https://air-quality-api.open-meteo.com/v1/air-quality";
    private const string AlertsUrl = "https://api.weather.gov/alerts/active";

    private async Task<JsonDocument> GetJson(string url, Dictionary<string, string?> parameters, CancellationToken cancellationToken, bool nws = false)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, QueryHelpers.AddQueryString(url, parameters));
        if (nws)
        {
            request.Headers.UserAgent.ParseAdd("graphqlapi-travel-weather-demo");
            request.Headers.Accept.ParseAdd("application/geo+json");
        }
        HttpResponseMessage response;
        try { response = await http.SendAsync(request, cancellationToken); }
        catch (HttpRequestException e) { throw new WeatherProviderException($"Weather data source is unreachable ({new Uri(url).Host})", e); }
        catch (OperationCanceledException e) when (!cancellationToken.IsCancellationRequested)
        { throw new WeatherProviderException($"Weather data source is unreachable ({new Uri(url).Host})", e); }
        using (response)
        {
            if (!response.IsSuccessStatusCode)
                throw new WeatherProviderException($"Weather data source returned HTTP {(int)response.StatusCode} ({new Uri(url).Host})");
            return await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(cancellationToken), cancellationToken: cancellationToken);
        }
    }

    private async Task<IReadOnlyList<Location>> Geocode(string name, string? country, int count, CancellationToken ct)
    {
        var args = new Dictionary<string, string?> { ["name"] = name.Trim(), ["count"] = count.ToString(CultureInfo.InvariantCulture), ["language"] = "en", ["format"] = "json" };
        if (country is not null) args["countryCode"] = country.Trim().ToUpperInvariant();
        using var data = await GetJson(Geocoding, args, ct);
        if (!data.RootElement.TryGetProperty("results", out var results)) return Array.Empty<Location>();
        return results.EnumerateArray().Select(r => new Location(
            r.GetProperty("name").GetString()!, r.GetProperty("country_code").GetString()!, r.GetProperty("timezone").GetString()!,
            r.TryGetProperty("admin1", out var region) ? region.GetString() : null,
            r.GetProperty("latitude").GetDouble(), r.GetProperty("longitude").GetDouble())).ToArray();
    }

    private static Dictionary<string, string?> Point(Location location) => new()
    {
        ["latitude"] = location.Latitude!.Value.ToString(CultureInfo.InvariantCulture),
        ["longitude"] = location.Longitude!.Value.ToString(CultureInfo.InvariantCulture)
    };

    public Task<IReadOnlyList<Location>> Locations(CancellationToken ct) => seed.Locations(ct);
    public Task<IReadOnlyList<Location>> SearchLocations(string name, int count, CancellationToken ct) => Geocode(name, null, count, ct);
    public async Task<Location> Location(string city, string? country, CancellationToken ct) =>
        (await Geocode(city, country, 1, ct)).FirstOrDefault()
        ?? throw new WeatherProviderException($"No location found for {(country is null ? city : $"{city}, {country}")}");

    public async Task<CurrentWeather> Current(Location location, CancellationToken ct)
    {
        var args = Point(location);
        args["current"] = "temperature_2m,weather_code,wind_speed_10m";
        args["timezone"] = location.Timezone;
        using var data = await GetJson(ForecastUrl, args, ct);
        var current = data.RootElement.GetProperty("current");
        return new(current.GetProperty("temperature_2m").GetDouble(), Condition(current.GetProperty("weather_code").GetInt32()), current.GetProperty("wind_speed_10m").GetDouble());
    }

    public async Task<IReadOnlyList<ForecastDay>> Forecast(Location location, int days, CancellationToken ct)
    {
        var args = Point(location);
        args["daily"] = "weather_code,temperature_2m_max,temperature_2m_min";
        args["timezone"] = location.Timezone;
        args["forecast_days"] = (days + 1).ToString(CultureInfo.InvariantCulture);
        using var data = await GetJson(ForecastUrl, args, ct);
        var daily = data.RootElement.GetProperty("daily");
        var dates = daily.GetProperty("time");
        return Enumerable.Range(1, Math.Min(days, Math.Max(0, dates.GetArrayLength() - 1))).Select(i => new ForecastDay(
            dates[i].GetString()!, daily.GetProperty("temperature_2m_min")[i].GetDouble(),
            daily.GetProperty("temperature_2m_max")[i].GetDouble(), Condition(daily.GetProperty("weather_code")[i].GetInt32()))).ToArray();
    }

    public async Task<AirQuality?> AirQuality(Location location, CancellationToken ct)
    {
        var args = Point(location);
        args["current"] = "us_aqi";
        using var data = await GetJson(AirUrl, args, ct);
        if (!data.RootElement.TryGetProperty("current", out var current) || current.ValueKind == JsonValueKind.Null ||
            !current.TryGetProperty("us_aqi", out var aqi) || aqi.ValueKind == JsonValueKind.Null) return null;
        var value = (int)Math.Floor(aqi.GetDouble() + 0.5);
        return new(value, AqiCategory(value));
    }

    public async Task<IReadOnlyList<WeatherAlert>> Alerts(Location location, CancellationToken ct)
    {
        if (location.Country != "US") return Array.Empty<WeatherAlert>();
        var point = FormattableString.Invariant($"{location.Latitude!.Value:F4},{location.Longitude!.Value:F4}");
        using var data = await GetJson(AlertsUrl, new() { ["point"] = point }, ct, true);
        if (!data.RootElement.TryGetProperty("features", out var features)) return Array.Empty<WeatherAlert>();
        return features.EnumerateArray().Select(f =>
        {
            var p = f.GetProperty("properties");
            return new WeatherAlert(p.GetProperty("event").GetString()!, p.GetProperty("severity").GetString()!,
                OptionalString(p, "onset") ?? p.GetProperty("effective").GetString()!,
                OptionalString(p, "ends") ?? p.GetProperty("expires").GetString()!);
        }).ToArray();
    }

    private static string? OptionalString(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind != JsonValueKind.Null ? value.GetString() : null;

    public static string Condition(int code) => code switch
    {
        0 => "Clear", 1 => "Mainly clear", 2 => "Partly cloudy", 3 => "Cloudy", 45 => "Fog", 48 => "Freezing fog",
        51 => "Light drizzle", 53 => "Drizzle", 55 => "Heavy drizzle", 56 or 57 => "Freezing drizzle",
        61 => "Light rain", 63 => "Rain", 65 => "Heavy rain", 66 or 67 => "Freezing rain",
        71 => "Light snow", 73 => "Snow", 75 => "Heavy snow", 77 => "Snow grains", 80 or 81 => "Showers",
        82 => "Heavy showers", 85 or 86 => "Snow showers", 95 => "Thunderstorms",
        96 or 99 => "Thunderstorms with hail", _ => "Unknown"
    };

    public static string AqiCategory(int aqi) => aqi switch
    {
        <= 50 => "Good", <= 100 => "Moderate", <= 150 => "Unhealthy for sensitive groups",
        <= 200 => "Unhealthy", <= 300 => "Very unhealthy", _ => "Hazardous"
    };
}
