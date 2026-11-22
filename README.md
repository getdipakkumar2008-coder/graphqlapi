# Travel weather GraphQL API

A small travel-planning example showing how GraphQL combines location, current
weather, forecast, air quality, and alerts into a client-shaped response. Each
weather field calls its own provider only when selected.

The original Node.js implementation is at the repository root. An equivalent
ASP.NET Core implementation is in [`dotnet/WeatherApi`](dotnet/WeatherApi);
see [specification.md](specification.md), [architecture.md](architecture.md),
and [plan.md](plan.md).

## Run the ASP.NET Core API

Requires the .NET 10 SDK. From the repository root:

```sh
dotnet run --project dotnet/WeatherApi                 # live data
WEATHER_SOURCE=seed dotnet run --project dotnet/WeatherApi  # offline sample data
dotnet test dotnet/WeatherApi.Tests
```

In PowerShell, set `$env:WEATHER_SOURCE='seed'` before running the application.
The .NET API also defaults to port 4000, configurable via `PORT`; stop the Node
server or use a different port if running both. The endpoints and examples below
apply to either implementation.

## Data sources

By default (`WEATHER_SOURCE=live`) the API fetches real-time data from free
services that need no API key or signup:

| Field | Source |
| --- | --- |
| `location`, `searchLocations` | [Open-Meteo Geocoding API](https://open-meteo.com/en/docs/geocoding-api) |
| `current`, `forecast` | [Open-Meteo Forecast API](https://open-meteo.com/en/docs) |
| `airQuality` (US AQI) | [Open-Meteo Air Quality API](https://open-meteo.com/en/docs/air-quality-api) |
| `alerts` | [US National Weather Service](https://www.weather.gov/documentation/services-web-api) (US locations only) |

The live providers are in `liveWeather.js`. Any city worldwide works. With
`country` omitted, the most relevant geocoding match is used. Forecasts start
tomorrow. `alerts` is always empty outside the US because there is no free
global alerts feed, so an empty list does not mean there are no warnings.
Open-Meteo's free tier is for non-commercial use and requires attribution
("Weather data by Open-Meteo.com", CC BY 4.0).

Set `WEATHER_SOURCE=seed` to work offline with the sample data in
[`data/seed.json`](data/seed.json) (`weather.js`). It covers Reykjavik IS,
London GB, New York US, Tokyo JP, Sydney AU and Mumbai IN, with forecast dates
calculated from today in each city's timezone. `locations` returns these
featured cities in both modes. Use `searchLocations` to find any city in live
mode.

## Run

Requires Node.js 18 or newer.

```sh
npm install
npm start                      # live data
WEATHER_SOURCE=seed npm start  # offline sample data (PowerShell: $env:WEATHER_SOURCE='seed'; npm start)
```

Open `http://localhost:4000/docs` for Swagger UI, or send a JSON POST to
`http://localhost:4000/graphql`. Set `PORT` to change the port.

Find a city with `searchLocations(name: $name)` (or list featured cities with
`{ locations { name country timezone } }`), then pass the city as GraphQL
variables instead of hardcoding it in the query:

```sh
curl -X POST http://localhost:4000/graphql \
  -H 'content-type: application/json' \
  -d '{"query":"query ($city: String!, $country: String, $days: Int!) { travelWeather(city: $city, country: $country) { location { name country timezone } current { temperatureC condition windSpeedKmh } forecast(days: $days) { date minTemperatureC maxTemperatureC condition } airQuality { aqi category } alerts { title severity startsAt endsAt } } }","variables":{"city":"London","country":"GB","days":5}}'
```

The optional `country` is an ISO country code. Forecasts support 1–5 days. Air
quality can be `null` when a provider has no data. GraphQL variables and
operation names are supported via the JSON `variables` and `operationName`
properties.

A smaller client can select only what it needs:

```graphql
query ($city: String!) {
  travelWeather(city: $city) {
    current { temperatureC condition }
  }
}
```

## Test

```sh
npm test
```