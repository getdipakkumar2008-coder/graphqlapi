const test = require('node:test');
const assert = require('node:assert/strict');
const { graphql } = require('graphql');
const { schema, createRoot } = require('../schema');
const { createServer } = require('../server');

function execute(source, providers, variableValues) {
  return graphql({ schema, source, rootValue: createRoot(providers), variableValues });
}

test('composes the travel planning query from sample providers', async () => {
  const result = await execute(`query ($city: String!, $country: String, $days: Int!) {
    travelWeather(city: $city, country: $country) {
      location { name country timezone }
      current { temperatureC condition windSpeedKmh }
      forecast(days: $days) { date minTemperatureC maxTemperatureC condition }
      airQuality { aqi category }
      alerts { title severity startsAt endsAt }
    }
  }`, undefined, { city: 'Reykjavik', country: 'IS', days: 5 });
  assert.equal(result.errors, undefined);
  assert.deepEqual(JSON.parse(JSON.stringify(result.data)), {
    travelWeather: {
      location: { name: 'Reykjavik', country: 'IS', timezone: 'Atlantic/Reykjavik' },
      current: { temperatureC: 2, condition: 'Cloudy', windSpeedKmh: 18 },
      forecast: [
        { date: '2026-01-01', minTemperatureC: -2, maxTemperatureC: 2, condition: 'Cloudy' },
        { date: '2026-01-02', minTemperatureC: -1, maxTemperatureC: 3, condition: 'Rain' },
        { date: '2026-01-03', minTemperatureC: 0, maxTemperatureC: 4, condition: 'Cloudy' },
        { date: '2026-01-04', minTemperatureC: -3, maxTemperatureC: 1, condition: 'Snow' },
        { date: '2026-01-05', minTemperatureC: -2, maxTemperatureC: 2, condition: 'Clear' },
      ],
      airQuality: { aqi: 24, category: 'Good' },
      alerts: [],
    },
  });
});

test('only calls selected providers and returns only selected fields', async () => {
  const calls = [];
  const location = { name: 'Reykjavik', country: 'IS', timezone: 'Atlantic/Reykjavik' };
  const providers = {
    location: async (city, country) => {
      calls.push(['location', city, country]);
      return location;
    },
    current: async (place) => {
      calls.push(['current', place]);
      return { temperatureC: 2, condition: 'Cloudy', windSpeedKmh: 18 };
    },
    forecast: () => { throw new Error('unexpected forecast call'); },
    airQuality: () => { throw new Error('unexpected air quality call'); },
    alerts: () => { throw new Error('unexpected alerts call'); },
  };
  const result = await execute('{ travelWeather(city: "Reykjavik") { current { temperatureC condition } } }', providers);
  assert.equal(result.errors, undefined);
  assert.deepEqual(JSON.parse(JSON.stringify(result.data)), {
    travelWeather: { current: { temperatureC: 2, condition: 'Cloudy' } },
  });
  assert.deepEqual(calls, [['location', 'Reykjavik', undefined], ['current', location]]);
});

test('validates forecast length and sample locations', async () => {
  for (const days of [0, -1, 6]) {
    const result = await execute(`{ travelWeather(city: "Reykjavik") { forecast(days: ${days}) { date } } }`);
    assert.match(result.errors[0].message, /between 1 and 5/);
  }
  const result = await execute('{ travelWeather(city: "Reykjavik", country: "GB") { location { name } } }');
  assert.match(result.errors[0].message, /only available for Reykjavik/);
  const oneDay = await execute('{ travelWeather(city: "reykjavik") { forecast(days: 1) { date } } }');
  assert.equal(oneDay.data.travelWeather.forecast.length, 1);
});

test('supports nullable air quality and provider alerts', async () => {
  const providers = {
    location: async () => ({ name: 'Example', country: 'XX', timezone: 'UTC' }),
    airQuality: async () => null,
    alerts: async () => [{ title: 'Wind', severity: 'Moderate', startsAt: '2026-01-01', endsAt: '2026-01-02' }],
  };
  const result = await execute('{ travelWeather(city: "Example") { airQuality { aqi } alerts { title severity startsAt endsAt } } }', providers);
  assert.equal(result.errors, undefined);
  assert.equal(result.data.travelWeather.airQuality, null);
  assert.equal(result.data.travelWeather.alerts[0].title, 'Wind');
});

test('serves GraphQL requests and rejects malformed or oversized input', async (t) => {
  const server = createServer();
  await new Promise((resolve) => server.listen(0, '127.0.0.1', resolve));
  t.after(() => new Promise((resolve) => server.close(resolve)));
  const url = `http://127.0.0.1:${server.address().port}/graphql`;
  const response = await fetch(url, {
    method: 'POST',
    body: JSON.stringify({ query: '{ travelWeather(city: "Reykjavik") { current { condition } } }' }),
  });
  assert.equal(response.status, 200);
  assert.equal((await response.json()).data.travelWeather.current.condition, 'Cloudy');
  assert.equal((await fetch(url)).status, 405);
  assert.equal((await fetch(url, { method: 'POST', body: '{' })).status, 400);
  assert.equal((await fetch(url, { method: 'POST', body: JSON.stringify({ query: 1 }) })).status, 400);
  assert.equal((await fetch(url, { method: 'POST', body: 'x'.repeat(65537) })).status, 413);
});
