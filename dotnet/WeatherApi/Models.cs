namespace WeatherApi;

public record Location(string Name, string Country, string Timezone, string? Region = null, double? Latitude = null, double? Longitude = null);
public record CurrentWeather(double TemperatureC, string Condition, double WindSpeedKmh);
public record ForecastDay(string Date, double MinTemperatureC, double MaxTemperatureC, string Condition);
public record AirQuality(int Aqi, string Category);
public record WeatherAlert(string Title, string Severity, string StartsAt, string EndsAt);

public interface IWeatherProvider
{
    Task<IReadOnlyList<Location>> Locations(CancellationToken cancellationToken);
    Task<IReadOnlyList<Location>> SearchLocations(string name, int count, CancellationToken cancellationToken);
    Task<Location> Location(string city, string? country, CancellationToken cancellationToken);
    Task<CurrentWeather> Current(Location location, CancellationToken cancellationToken);
    Task<IReadOnlyList<ForecastDay>> Forecast(Location location, int days, CancellationToken cancellationToken);
    Task<AirQuality?> AirQuality(Location location, CancellationToken cancellationToken);
    Task<IReadOnlyList<WeatherAlert>> Alerts(Location location, CancellationToken cancellationToken);
}

public sealed class WeatherProviderException(string message, Exception? inner = null) : Exception(message, inner);
