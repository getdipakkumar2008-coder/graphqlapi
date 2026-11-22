using GraphQL;
using GraphQL.Types;

namespace WeatherApi;

public sealed class WeatherSchema : Schema
{
    public WeatherSchema(IServiceProvider services, WeatherQuery query) : base(services) => Query = query;
}

public sealed class WeatherQuery : ObjectGraphType
{
    public WeatherQuery(IWeatherProvider provider)
    {
        Name = "Query";
        Field<NonNullGraphType<ListGraphType<NonNullGraphType<LocationType>>>>("locations")
            .ResolveAsync(async context => await Resolve(() => provider.Locations(context.CancellationToken)));
        Field<NonNullGraphType<ListGraphType<NonNullGraphType<LocationType>>>>("searchLocations")
            .Argument<NonNullGraphType<StringGraphType>>("name")
            .Argument<IntGraphType>("count", argument => argument.DefaultValue = 5)
            .ResolveAsync(async context =>
            {
                if (context.Arguments?.TryGetValue("count", out var argument) == true && argument.Value is null)
                    throw new ExecutionError("Search count must be between 1 and 10");
                var count = context.GetArgument<int>("count", 5);
                if (count is < 1 or > 10) throw new ExecutionError("Search count must be between 1 and 10");
                return await Resolve(() => provider.SearchLocations(context.GetArgument<string>("name"), count, context.CancellationToken));
            });
        Field<NonNullGraphType<TravelWeatherType>>("travelWeather")
            .Argument<NonNullGraphType<StringGraphType>>("city")
            .Argument<StringGraphType>("country")
            .ResolveAsync(async context => await Resolve(() => provider.Location(
                context.GetArgument<string>("city"), context.GetArgument<string?>("country"), context.CancellationToken)));
    }

    internal static async Task<T> Resolve<T>(Func<Task<T>> action)
    {
        try { return await action(); }
        catch (WeatherProviderException e) { throw new ExecutionError(e.Message, e); }
    }
}

public sealed class TravelWeatherType : ObjectGraphType<Location>
{
    public TravelWeatherType(IWeatherProvider provider)
    {
        Name = "TravelWeather";
        Field<NonNullGraphType<LocationType>>("location").Resolve(context => context.Source);
        Field<NonNullGraphType<CurrentWeatherType>>("current")
            .ResolveAsync(async context => await WeatherQuery.Resolve(() => provider.Current(context.Source, context.CancellationToken)));
        Field<NonNullGraphType<ListGraphType<NonNullGraphType<ForecastDayType>>>>("forecast")
            .Argument<NonNullGraphType<IntGraphType>>("days")
            .ResolveAsync(async context =>
            {
                var days = context.GetArgument<int>("days");
                if (days is < 1 or > 5) throw new ExecutionError("Forecast days must be between 1 and 5");
                return await WeatherQuery.Resolve(() => provider.Forecast(context.Source, days, context.CancellationToken));
            });
        Field<AirQualityType>("airQuality")
            .ResolveAsync(async context => await WeatherQuery.Resolve(() => provider.AirQuality(context.Source, context.CancellationToken)));
        Field<NonNullGraphType<ListGraphType<NonNullGraphType<WeatherAlertType>>>>("alerts")
            .ResolveAsync(async context => await WeatherQuery.Resolve(() => provider.Alerts(context.Source, context.CancellationToken)));
    }
}

public sealed class LocationType : ObjectGraphType<Location>
{
    public LocationType()
    {
        Name = "Location";
        Field<NonNullGraphType<StringGraphType>>("name").Resolve(c => c.Source.Name);
        Field<NonNullGraphType<StringGraphType>>("country").Resolve(c => c.Source.Country);
        Field<NonNullGraphType<StringGraphType>>("timezone").Resolve(c => c.Source.Timezone);
        Field<StringGraphType>("region").Resolve(c => c.Source.Region);
        Field<FloatGraphType>("latitude").Resolve(c => c.Source.Latitude);
        Field<FloatGraphType>("longitude").Resolve(c => c.Source.Longitude);
    }
}

public sealed class CurrentWeatherType : ObjectGraphType<CurrentWeather>
{
    public CurrentWeatherType()
    {
        Name = "CurrentWeather";
        Field<NonNullGraphType<FloatGraphType>>("temperatureC").Resolve(c => c.Source.TemperatureC);
        Field<NonNullGraphType<StringGraphType>>("condition").Resolve(c => c.Source.Condition);
        Field<NonNullGraphType<FloatGraphType>>("windSpeedKmh").Resolve(c => c.Source.WindSpeedKmh);
    }
}

public sealed class ForecastDayType : ObjectGraphType<ForecastDay>
{
    public ForecastDayType()
    {
        Name = "ForecastDay";
        Field<NonNullGraphType<StringGraphType>>("date").Resolve(c => c.Source.Date);
        Field<NonNullGraphType<FloatGraphType>>("minTemperatureC").Resolve(c => c.Source.MinTemperatureC);
        Field<NonNullGraphType<FloatGraphType>>("maxTemperatureC").Resolve(c => c.Source.MaxTemperatureC);
        Field<NonNullGraphType<StringGraphType>>("condition").Resolve(c => c.Source.Condition);
    }
}

public sealed class AirQualityType : ObjectGraphType<AirQuality>
{
    public AirQualityType()
    {
        Name = "AirQuality";
        Field<NonNullGraphType<IntGraphType>>("aqi").Resolve(c => c.Source.Aqi);
        Field<NonNullGraphType<StringGraphType>>("category").Resolve(c => c.Source.Category);
    }
}

public sealed class WeatherAlertType : ObjectGraphType<WeatherAlert>
{
    public WeatherAlertType()
    {
        Name = "WeatherAlert";
        Field<NonNullGraphType<StringGraphType>>("title").Resolve(c => c.Source.Title);
        Field<NonNullGraphType<StringGraphType>>("severity").Resolve(c => c.Source.Severity);
        Field<NonNullGraphType<StringGraphType>>("startsAt").Resolve(c => c.Source.StartsAt);
        Field<NonNullGraphType<StringGraphType>>("endsAt").Resolve(c => c.Source.EndsAt);
    }
}
