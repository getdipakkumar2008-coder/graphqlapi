# Travel weather GraphQL API

A small travel-planning example showing how GraphQL combines location, current
weather, forecast, air quality, and alerts into a client-shaped response. Each
weather field calls its own provider only when selected.

**This is an offline demonstration, not a live weather service.** The providers
in `weather.js` return fixed sample data for Reykjavik, Iceland only. The dates
and conditions are not current, and the empty alerts list does not indicate
that there are no real weather alerts. Other locations return an error. Replace
the sample providers with real data sources before using this for travel
decisions.

## Run

Requires Node.js 18 or newer.

```sh
npm install
npm start
```

Send a JSON POST to `http://localhost:4000/graphql`. Set `PORT` to change the
port. For example:

```sh
curl -X POST http://localhost:4000/graphql \
  -H 'content-type: application/json' \
  -d '{"query":"query { travelWeather(city: \"Reykjavik\", country: \"IS\") { location { name country timezone } current { temperatureC condition windSpeedKmh } forecast(days: 5) { date minTemperatureC maxTemperatureC condition } airQuality { aqi category } alerts { title severity startsAt endsAt } } }"}'
```

The optional `country` is an ISO country code; the sample accepts `IS`.
Forecasts support 1–5 days. Air quality can be `null` when a provider has no
data. GraphQL variables and operation names are supported via the JSON
`variables` and `operationName` properties.

A smaller client can select only what it needs:

```graphql
query {
  travelWeather(city: "Reykjavik") {
    current { temperatureC condition }
  }
}
```

## Test

```sh
npm test
```