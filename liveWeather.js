const seed = require('./data/seed.json');

const geocodingUrl = 'https://geocoding-api.open-meteo.com/v1/search';
const forecastUrl = 'https://api.open-meteo.com/v1/forecast';
const airQualityUrl = 'https://air-quality-api.open-meteo.com/v1/air-quality';
const nwsAlertsUrl = 'https://api.weather.gov/alerts/active';
const userAgent = 'graphqlapi-travel-weather-demo';

// WMO weather interpretation codes used by Open-Meteo.
const wmoConditions = {
  0: 'Clear', 1: 'Mainly clear', 2: 'Partly cloudy', 3: 'Cloudy',
  45: 'Fog', 48: 'Freezing fog',
  51: 'Light drizzle', 53: 'Drizzle', 55: 'Heavy drizzle', 56: 'Freezing drizzle', 57: 'Freezing drizzle',
  61: 'Light rain', 63: 'Rain', 65: 'Heavy rain', 66: 'Freezing rain', 67: 'Freezing rain',
  71: 'Light snow', 73: 'Snow', 75: 'Heavy snow', 77: 'Snow grains',
  80: 'Showers', 81: 'Showers', 82: 'Heavy showers', 85: 'Snow showers', 86: 'Snow showers',
  95: 'Thunderstorms', 96: 'Thunderstorms with hail', 99: 'Thunderstorms with hail',
};

function condition(code) {
  return wmoConditions[code] || 'Unknown';
}

function aqiCategory(aqi) {
  if (aqi <= 50) return 'Good';
  if (aqi <= 100) return 'Moderate';
  if (aqi <= 150) return 'Unhealthy for sensitive groups';
  if (aqi <= 200) return 'Unhealthy';
  if (aqi <= 300) return 'Very unhealthy';
  return 'Hazardous';
}

function createLiveProviders({ fetch = globalThis.fetch, timeoutMs = 8000 } = {}) {
  async function getJson(base, params, headers) {
    const url = `${base}?${new URLSearchParams(params)}`;
    let response;
    try {
      response = await fetch(url, { headers, signal: AbortSignal.timeout(timeoutMs) });
    } catch {
      throw new Error(`Weather data source is unreachable (${new URL(base).host})`);
    }
    if (!response.ok) {
      throw new Error(`Weather data source returned HTTP ${response.status} (${new URL(base).host})`);
    }
    return response.json();
  }

  async function geocode(name, country, count) {
    const params = { name: name.trim(), count, language: 'en', format: 'json' };
    if (country != null) params.countryCode = country.trim().toUpperCase();
    const data = await getJson(geocodingUrl, params);
    return (data.results || []).map((r) => ({
      name: r.name,
      country: r.country_code,
      timezone: r.timezone,
      region: r.admin1 || null,
      latitude: r.latitude,
      longitude: r.longitude,
    }));
  }

  const point = (location) => ({ latitude: location.latitude, longitude: location.longitude });

  return {
    async locations() {
      return seed.locations.map(({ name, country, timezone }) => ({ name, country, timezone }));
    },
    async searchLocations(name, count) {
      return geocode(name, null, count);
    },
    async location(city, country) {
      const [match] = await geocode(city, country, 1);
      if (!match) {
        const where = country == null ? city : `${city}, ${country}`;
        throw new Error(`No location found for ${where}`);
      }
      return match;
    },
    async current(location) {
      const data = await getJson(forecastUrl, {
        ...point(location),
        current: 'temperature_2m,weather_code,wind_speed_10m',
        timezone: location.timezone,
      });
      return {
        temperatureC: data.current.temperature_2m,
        condition: condition(data.current.weather_code),
        windSpeedKmh: data.current.wind_speed_10m,
      };
    },
    async forecast(location, days) {
      // Request one extra day and drop today so forecasts start tomorrow, matching the seed provider.
      const data = await getJson(forecastUrl, {
        ...point(location),
        daily: 'weather_code,temperature_2m_max,temperature_2m_min',
        timezone: location.timezone,
        forecast_days: days + 1,
      });
      const daily = data.daily;
      return daily.time.slice(1, days + 1).map((date, i) => ({
        date,
        minTemperatureC: daily.temperature_2m_min[i + 1],
        maxTemperatureC: daily.temperature_2m_max[i + 1],
        condition: condition(daily.weather_code[i + 1]),
      }));
    },
    async airQuality(location) {
      const data = await getJson(airQualityUrl, { ...point(location), current: 'us_aqi' });
      const aqi = data.current && data.current.us_aqi;
      if (aqi == null) return null;
      return { aqi: Math.round(aqi), category: aqiCategory(aqi) };
    },
    async alerts(location) {
      // The free NWS alerts API only covers the United States; other countries have no free global feed.
      if (location.country !== 'US') return [];
      const data = await getJson(nwsAlertsUrl, {
        point: `${location.latitude.toFixed(4)},${location.longitude.toFixed(4)}`,
      }, { 'User-Agent': userAgent, Accept: 'application/geo+json' });
      return (data.features || []).map(({ properties: p }) => ({
        title: p.event,
        severity: p.severity,
        startsAt: p.onset || p.effective,
        endsAt: p.ends || p.expires,
      }));
    },
  };
}

module.exports = { createLiveProviders, condition, aqiCategory };
