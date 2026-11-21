# Master Prompt: Travel Weather GraphQL API

You are a senior full-stack engineer. Build a working travel-weather GraphQL API that combines weather information from multiple sources into a single, client-shaped response. Work within the existing repository, follow its conventions, and document any necessary assumptions. If the repository does not establish a stack, choose a simple, maintained stack and explain the choice in the README.

## Goal

Let a travel-planning client request location details, current conditions, a forecast, air quality, and weather alerts in one GraphQL operation. A smaller client must be able to request only the fields it needs without fetching unrelated provider data.

## GraphQL contract

Expose at least the following schema, adding the supporting types and scalar definitions needed to make it executable:

```graphql
type Query {
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
}

type CurrentWeather {
  temperatureC: Float!
  condition: String!
  windSpeedKmh: Float!
}

type ForecastDay {
  date: String!
  minTemperatureC: Float!
  maxTemperatureC: Float!
  condition: String!
}

type AirQuality {
  aqi: Int!
  category: String!
}

type WeatherAlert {
  title: String!
  severity: String!
  startsAt: String!
  endsAt: String!
}
```

Use ISO 8601 dates and timestamps, an IANA timezone identifier, Celsius temperatures, and wind speeds in km/h. Document which AQI standard is used. Normalize provider-specific values to the contract; do not imply that different AQI standards are interchangeable.

The following operation must work:

```graphql
query {
  travelWeather(city: "Reykjavik", country: "IS") {
    location { name country timezone }
    current { temperatureC condition windSpeedKmh }
    forecast(days: 5) { date minTemperatureC maxTemperatureC condition }
    airQuality { aqi category }
    alerts { title severity startsAt endsAt }
  }
}
```

Also support a widget requesting only:

```graphql
query {
  travelWeather(city: "Reykjavik", country: "IS") {
    current { temperatureC condition }
  }
}
```

## Implementation requirements

- Resolve the city and optional country to a location before requesting weather data. Define and document how ambiguous or unknown locations are handled; never silently select an arbitrary ambiguous match.
- Integrate real, documented providers covering current conditions, daily forecasts, air quality, and alerts. One provider may cover multiple capabilities. Check actual provider coverage and availability rather than assuming every location has every data type.
- Separate provider adapters from GraphQL resolvers so providers can be replaced and mocked. Fetch a data category only when its field is requested, and avoid duplicate upstream calls within a request when practical.
- Validate inputs, including a nonblank city, a documented country format, and a positive forecast length within the provider's supported range. Return actionable GraphQL errors for invalid inputs or unavailable required data.
- Treat air quality as optional: return `null` when it is genuinely unavailable, and surface provider failures through GraphQL errors. Return an empty alerts list only when the provider successfully reports no alerts, not when an upstream request fails.
- Respect the schema's nullability and GraphQL error propagation. In particular, a failure in a non-null field can propagate through `TravelWeather!` and null the root response; do not present incomplete required data as success. Document this behavior.
- Configure provider credentials through environment variables, include a safe example environment file if credentials are needed, and never commit secrets. Set reasonable upstream timeouts and report failures without leaking credentials.
- Keep the implementation focused: do not add authentication, persistence, or a frontend unless existing project requirements make them necessary.

## Verification and documentation

- Add focused automated tests with mocked provider responses for the full travel-planning query, the current-only widget, input validation, location resolution, optional air-quality behavior, and upstream failures. Verify that unrequested data categories are not fetched and that empty alerts are distinguishable from a provider failure.
- Run the relevant tests and any existing lint or build checks. Do not rely on live provider availability for automated tests.
- Update the README with prerequisites, setup, environment variables, how to run the API and tests, endpoint details, example queries, provider attribution and coverage limitations, units, validation rules, and error/nullability behavior.
- Deliver complete runnable code, not pseudocode. Report any provider limitations or unverified behavior explicitly.
