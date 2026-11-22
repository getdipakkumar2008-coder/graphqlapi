# Travel weather API specification

## Scope

Reimplement the `feature/live-weather-data` travel weather service in ASP.NET
Core and GraphQL while retaining its schema, provider behavior, and HTTP
interface. The existing Node implementation remains available. The .NET
implementation targets .NET 10 and uses GraphQL.NET.

## GraphQL contract

`POST /graphql` accepts a JSON object with a required string `query`, an
optional object or null `variables`, and an optional string or null
`operationName`. GraphQL returns only requested fields, supports variables,
aliases and standard GraphQL validation, and reports execution errors in the
GraphQL response.

```graphql
type Query {
  locations: [Location!]!
  searchLocations(name: String!, count: Int = 5): [Location!]!
  travelWeather(city: String!, country: String): TravelWeather!
}
type TravelWeather {
  location: Location!
  current: CurrentWeather!
  forecast(days: Int!): [ForecastDay!]!
  airQuality: AirQuality
  alerts: [WeatherAlert!]!
}
type Location {
  name: String!
  country: String!
  timezone: String!
  region: String
  latitude: Float
  longitude: Float
}
type CurrentWeather { temperatureC: Float! condition: String! windSpeedKmh: Float! }
type ForecastDay { date: String! minTemperatureC: Float! maxTemperatureC: Float! condition: String! }
type AirQuality { aqi: Int! category: String! }
type WeatherAlert { title: String! severity: String! startsAt: String! endsAt: String! }
```

Search count must be 1–10; forecast days must be 1–5. Forecasts begin
tomorrow in the location's timezone. Location resolution precedes weather
fields; current, forecast, air quality and alerts are fetched only if selected.

## Data and configuration

`WEATHER_SOURCE` defaults to `live`, or can be set to `seed` (case-insensitive);
other values prevent startup. `PORT` defaults to 4000.

In live mode, Open-Meteo geocoding finds locations worldwide; an optional
country is passed as an uppercase ISO code, and the most relevant result is
used. Open-Meteo supplies current conditions, daily forecasts and US AQI.
Weather codes and AQI values are mapped to the source implementation's labels.
The National Weather Service supplies active alerts for US locations only.
Outside the US, alerts are empty and do not imply absence of warnings. Weather
data by Open-Meteo.com (CC BY 4.0); the free tier is for non-commercial use.
Providers do not require API keys. Requests time out after eight seconds and
provider connectivity and HTTP errors are exposed as GraphQL errors.

In seed mode, `data/seed.json` supplies six featured cities, case-insensitive
exact city/country lookup, case-insensitive substring search, weather and
alerts. Dates are relative to today in each location's timezone. `locations`
returns those six featured cities in both modes. Air quality may be null.

## HTTP and documentation

`GET /` and `/docs` serve Swagger UI; `GET /openapi.json` serves its OpenAPI
description. The UI loads assets from unpkg.com. `/graphql` accepts POST only.
Malformed JSON and invalid request shapes receive HTTP 400; request bodies
larger than 65,536 bytes receive 413. GraphQL parse, validation and execution
results are returned with HTTP 200. No authentication is provided.
