const test = require('node:test');
const assert = require('node:assert/strict');
const { graphql } = require('graphql');
const { schema, createRoot } = require('../schema');
const { createLiveProviders } = require('../liveWeather');

const responses = {
  'geocoding-api.open-meteo.com': {
    results: [{ name: 'Seattle', country_code: 'US', admin1: 'Washington', timezone: 'America/Los_Angeles', latitude: 47.60621, longitude: -122.33207 }],
  },
  'api.open-meteo.com': {
    current: { temperature_2m: 7.4, weather_code: 61, wind_speed_10m: 12.1 },
    daily: {
      time: ['2026-11-21', '2026-11-22', '2026-11-23'],
      weather_code: [0, 3, 95],
      temperature_2m_min: [1, 2, 3],
      temperature_2m_max: [8, 9, 10],
    },
  },
  'air-quality-api.open-meteo.com': { current: { us_aqi: 72.4 } },
  'api.weather.gov': {
    features: [{ properties: { event: 'Wind Advisory', severity: 'Moderate', onset: '2026-11-21T10:00:00-08:00', ends: null, expires: '2026-11-21T22:00:00-08:00' } }],
  },
};

function fakeFetch(calls, overrides = {}) {
  return async (url, init) => {
    const parsed = new URL(url);
    calls.push({ host: parsed.host, params: Object.fromEntries(parsed.searchParams), headers: init.headers });
    const body = overrides[parsed.host] || responses[parsed.host];
    if (body instanceof Error) throw body;
    if (typeof body === 'number') return { ok: false, status: body };
    return { ok: true, json: async () => body };
  };
}

function run(source, providers, variableValues) {
  return graphql({ schema, source, rootValue: createRoot(providers), variableValues });
}

test('maps Open-Meteo and NWS responses into the travel weather schema', async () => {
  const calls = [];
  const result = await run(`query ($city: String!, $country: String, $days: Int!) {
    travelWeather(city: $city, country: $country) {
      location { name country region timezone latitude longitude }
      current { temperatureC condition windSpeedKmh }
      forecast(days: $days) { date minTemperatureC maxTemperatureC condition }
      airQuality { aqi category }
      alerts { title severity startsAt endsAt }
    }
  }`, createLiveProviders({ fetch: fakeFetch(calls) }), { city: 'Seattle', country: 'us', days: 2 });
  assert.equal(result.errors, undefined);
  assert.deepEqual(JSON.parse(JSON.stringify(result.data.travelWeather)), {
    location: { name: 'Seattle', country: 'US', region: 'Washington', timezone: 'America/Los_Angeles', latitude: 47.60621, longitude: -122.33207 },
    current: { temperatureC: 7.4, condition: 'Light rain', windSpeedKmh: 12.1 },
    forecast: [
      { date: '2026-11-22', minTemperatureC: 2, maxTemperatureC: 9, condition: 'Cloudy' },
      { date: '2026-11-23', minTemperatureC: 3, maxTemperatureC: 10, condition: 'Thunderstorms' },
    ],
    airQuality: { aqi: 72, category: 'Moderate' },
    alerts: [{ title: 'Wind Advisory', severity: 'Moderate', startsAt: '2026-11-21T10:00:00-08:00', endsAt: '2026-11-21T22:00:00-08:00' }],
  });
  const geocode = calls.find((c) => c.host === 'geocoding-api.open-meteo.com');
  assert.equal(geocode.params.countryCode, 'US');
  const daily = calls.find((c) => c.host === 'api.open-meteo.com' && c.params.daily);
  assert.equal(daily.params.forecast_days, '3');
  const nws = calls.find((c) => c.host === 'api.weather.gov');
  assert.equal(nws.params.point, '47.6062,-122.3321');
  assert.ok(nws.headers['User-Agent']);
});

test('skips NWS outside the US and reports unknown cities and upstream failures', async () => {
  const calls = [];
  const geocode = { results: [{ name: 'Paris', country_code: 'FR', timezone: 'Europe/Paris', latitude: 48.85, longitude: 2.35 }] };
  const paris = await run('{ travelWeather(city: "Paris") { alerts { title } } }',
    createLiveProviders({ fetch: fakeFetch(calls, { 'geocoding-api.open-meteo.com': geocode }) }));
  assert.deepEqual(JSON.parse(JSON.stringify(paris.data.travelWeather.alerts)), []);
  assert.ok(!calls.some((c) => c.host === 'api.weather.gov'));

  const missing = await run('{ travelWeather(city: "Nowhere", country: "XX") { location { name } } }',
    createLiveProviders({ fetch: fakeFetch([], { 'geocoding-api.open-meteo.com': {} }) }));
  assert.match(missing.errors[0].message, /No location found for Nowhere, XX/);

  const down = await run('{ travelWeather(city: "Seattle") { current { condition } } }',
    createLiveProviders({ fetch: fakeFetch([], { 'api.open-meteo.com': 503 }) }));
  assert.match(down.errors[0].message, /HTTP 503 \(api\.open-meteo\.com\)/);

  const offline = await run('{ searchLocations(name: "Seattle") { name } }',
    createLiveProviders({ fetch: fakeFetch([], { 'geocoding-api.open-meteo.com': new Error('ENOTFOUND') }) }));
  assert.match(offline.errors[0].message, /unreachable/);
});
