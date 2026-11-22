using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using WeatherApi;
using Xunit;

namespace WeatherApiTests;

public class WeatherTests
{
    private static SeedWeatherProvider Seed() => new(Path.Combine(AppContext.BaseDirectory, "data", "seed.json"), new FixedClock());

    private static WebApplicationFactory<Program> Factory(IWeatherProvider provider) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder => builder.ConfigureServices(services =>
        {
            services.RemoveAll<IWeatherProvider>();
            services.AddSingleton(provider);
        }));

    private static async Task<JsonDocument> Query(HttpClient client, string query, object? variables = null, string? operationName = null)
    {
        var response = await client.PostAsJsonAsync("/graphql", new { query, variables, operationName });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task SeedQuerySupportsVariablesNullAndLocalForecastDates()
    {
        using var factory = Factory(Seed());
        using var client = factory.CreateClient();
        using var result = await Query(client, """
            query Weather($city: String!, $country: String, $days: Int!) {
              travelWeather(city: $city, country: $country) {
                location { name country timezone region latitude longitude }
                current { temperatureC condition windSpeedKmh }
                forecast(days: $days) { date minTemperatureC maxTemperatureC condition }
                airQuality { aqi category } alerts { title severity startsAt endsAt }
              }
            }
            """, new { city = "sydney", country = "au", days = 2 }, "Weather");
        Assert.False(result.RootElement.TryGetProperty("errors", out _), result.RootElement.ToString());
        var weather = result.RootElement.GetProperty("data").GetProperty("travelWeather");
        Assert.Equal("Sydney", weather.GetProperty("location").GetProperty("name").GetString());
        Assert.Equal(JsonValueKind.Null, weather.GetProperty("location").GetProperty("latitude").ValueKind);
        Assert.Equal("2026-06-17", weather.GetProperty("forecast")[0].GetProperty("date").GetString());
        Assert.Equal("2026-06-19", weather.GetProperty("alerts")[0].GetProperty("startsAt").GetString());
        Assert.Equal(2, weather.GetProperty("forecast").GetArrayLength());

        using var mumbai = await Query(client, "{ travelWeather(city: \"Mumbai\") { airQuality { aqi } } }");
        Assert.Equal(JsonValueKind.Null, mumbai.RootElement.GetProperty("data").GetProperty("travelWeather").GetProperty("airQuality").ValueKind);
    }

    [Fact]
    public async Task GraphQlValidatesAndOnlyFetchesSelectedFields()
    {
        var handler = new FakeHandler();
        using var http = new HttpClient(handler);
        using var factory = Factory(new LiveWeatherProvider(http, Seed()));
        using var client = factory.CreateClient();
        using var selected = await Query(client, "{ travelWeather(city: \"Seattle\") { current { condition } } }");
        Assert.False(selected.RootElement.TryGetProperty("errors", out _), selected.RootElement.ToString());
        Assert.Equal("Light rain", selected.RootElement.GetProperty("data").GetProperty("travelWeather").GetProperty("current").GetProperty("condition").GetString());
        Assert.Equal(2, handler.Requests.Count);
        Assert.DoesNotContain(handler.Requests, r => r.Contains("air-quality") || r.Contains("weather.gov"));
        using var badDays = await Query(client, "{ travelWeather(city: \"Seattle\") { forecast(days: 6) { date } } }");
        Assert.Contains("between 1 and 5", badDays.RootElement.GetProperty("errors")[0].GetProperty("message").GetString());
        using var badCount = await Query(client, "{ searchLocations(name: \"x\", count: 0) { name } }");
        Assert.Contains("between 1 and 10", badCount.RootElement.GetProperty("errors")[0].GetProperty("message").GetString());
        using var nullCount = await Query(client, "{ searchLocations(name: \"x\", count: null) { name } }");
        Assert.Contains("between 1 and 10", nullCount.RootElement.GetProperty("errors")[0].GetProperty("message").GetString());
        using var invalid = await Query(client, "{ nonexistent }");
        Assert.True(invalid.RootElement.TryGetProperty("errors", out _));
    }

    [Fact]
    public async Task LiveProvidersMapUpstreamsAndReportFailures()
    {
        var handler = new FakeHandler();
        using var http = new HttpClient(handler);
        using var factory = Factory(new LiveWeatherProvider(http, Seed()));
        using var client = factory.CreateClient();
        using var result = await Query(client, """
            { travelWeather(city: "Seattle", country: "us") {
                location { name region latitude longitude } current { temperatureC condition windSpeedKmh }
                forecast(days: 2) { date condition } airQuality { aqi category } alerts { title startsAt endsAt }
            } }
            """);
        Assert.False(result.RootElement.TryGetProperty("errors", out _), result.RootElement.ToString());
        var weather = result.RootElement.GetProperty("data").GetProperty("travelWeather");
        Assert.Equal("Washington", weather.GetProperty("location").GetProperty("region").GetString());
        Assert.Equal("2026-11-22", weather.GetProperty("forecast")[0].GetProperty("date").GetString());
        Assert.Equal("Thunderstorms", weather.GetProperty("forecast")[1].GetProperty("condition").GetString());
        Assert.Equal(72, weather.GetProperty("airQuality").GetProperty("aqi").GetInt32());
        Assert.Equal("Moderate", weather.GetProperty("airQuality").GetProperty("category").GetString());
        Assert.Equal("2026-11-21T22:00:00-08:00", weather.GetProperty("alerts")[0].GetProperty("endsAt").GetString());
        Assert.Contains(handler.Requests, r => r.Contains("countryCode=US"));
        Assert.Contains(handler.Requests, r => r.Contains("forecast_days=3"));
        Assert.Contains(handler.Requests, r => Uri.UnescapeDataString(r).Contains("point=47.6062,-122.3321"));
        Assert.True(handler.SawNwsHeaders);

        handler.FailForecast = true;
        using var failed = await Query(client, "{ travelWeather(city: \"Seattle\") { current { condition } } }");
        Assert.Contains("HTTP 503 (api.open-meteo.com)", failed.RootElement.GetProperty("errors")[0].GetProperty("message").GetString());
        handler.Missing = true;
        using var missing = await Query(client, "{ travelWeather(city: \"Nowhere\") { location { name } } }");
        Assert.Contains("No location found for Nowhere", missing.RootElement.GetProperty("errors")[0].GetProperty("message").GetString());
        handler.Missing = false;
        handler.Offline = true;
        using var offline = await Query(client, "{ searchLocations(name: \"Seattle\") { name } }");
        Assert.Contains("unreachable", offline.RootElement.GetProperty("errors")[0].GetProperty("message").GetString());
    }

    [Fact]
    public async Task FeaturedAndSearchLocationsWorkWithoutWeatherCalls()
    {
        var handler = new FakeHandler();
        using var http = new HttpClient(handler);
        var provider = new LiveWeatherProvider(http, Seed());
        using var factory = Factory(provider);
        using var client = factory.CreateClient();
        using var locations = await Query(client, "{ locations { name country } searchLocations(name: \"Seattle\") { region } }");
        Assert.False(locations.RootElement.TryGetProperty("errors", out _), locations.RootElement.ToString());
        Assert.Equal(6, locations.RootElement.GetProperty("data").GetProperty("locations").GetArrayLength());
        Assert.Contains(handler.Requests, r => r.Contains("count=5"));
        Assert.Single(handler.Requests);
        Assert.Empty(await provider.Alerts(new Location("Paris", "FR", "Europe/Paris", null, 48.85, 2.35), CancellationToken.None));
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task HttpRejectsInvalidAndOversizedRequestsAndServesDocs()
    {
        using var factory = Factory(Seed());
        using var client = factory.CreateClient();
        Assert.Equal(HttpStatusCode.MethodNotAllowed, (await client.GetAsync("/graphql")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsync("/graphql", new StringContent("{"))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/graphql", new { query = 1 })).StatusCode);
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, (await client.PostAsync("/graphql", new StringContent(new string('x', 65537)))).StatusCode);
        using var spec = JsonDocument.Parse(await client.GetStringAsync("/openapi.json"));
        Assert.Equal("3.0.3", spec.RootElement.GetProperty("openapi").GetString());
        Assert.Contains("SwaggerUIBundle", await client.GetStringAsync("/docs"));
    }

    private sealed class FixedClock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => DateTimeOffset.Parse("2026-06-15T20:00:00Z");
    }

    private sealed class FakeHandler : HttpMessageHandler
    {
        public List<string> Requests { get; } = [];
        public bool SawNwsHeaders { get; private set; }
        public bool FailForecast { get; set; }
        public bool Missing { get; set; }
        public bool Offline { get; set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var uri = request.RequestUri!;
            Requests.Add(uri.ToString());
            if (Offline) throw new HttpRequestException("offline");
            if (uri.Host == "api.open-meteo.com" && FailForecast)
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
            if (uri.Host == "api.weather.gov")
                SawNwsHeaders = request.Headers.UserAgent.Count > 0 && request.Headers.Accept.Any(a => a.MediaType == "application/geo+json");
            var json = uri.Host switch
            {
                "geocoding-api.open-meteo.com" when Missing => "{}",
                "geocoding-api.open-meteo.com" => """{"results":[{"name":"Seattle","country_code":"US","admin1":"Washington","timezone":"America/Los_Angeles","latitude":47.60621,"longitude":-122.33207}]}""",
                "api.open-meteo.com" => """{"current":{"temperature_2m":7.4,"weather_code":61,"wind_speed_10m":12.1},"daily":{"time":["2026-11-21","2026-11-22","2026-11-23"],"weather_code":[0,3,95],"temperature_2m_min":[1,2,3],"temperature_2m_max":[8,9,10]}}""",
                "air-quality-api.open-meteo.com" => """{"current":{"us_aqi":72.4}}""",
                "api.weather.gov" => """{"features":[{"properties":{"event":"Wind Advisory","severity":"Moderate","onset":"2026-11-21T10:00:00-08:00","ends":null,"expires":"2026-11-21T22:00:00-08:00"}}]}""",
                _ => throw new InvalidOperationException($"Unexpected host {uri.Host}")
            };
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") });
        }
    }
}
