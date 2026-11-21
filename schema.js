const { buildSchema, GraphQLError } = require('graphql');
const { sampleProviders } = require('./weather');

const schema = buildSchema(`
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
`);

function createRoot(providers = sampleProviders) {
  return {
    async travelWeather({ city, country }) {
      const location = await providers.location(city, country);
      return {
        location,
        current: () => providers.current(location),
        forecast: ({ days }) => {
          if (days < 1 || days > 5) {
            throw new GraphQLError('Forecast days must be between 1 and 5');
          }
          return providers.forecast(location, days);
        },
        airQuality: () => providers.airQuality(location),
        alerts: () => providers.alerts(location),
      };
    },
  };
}

module.exports = { schema, createRoot };
