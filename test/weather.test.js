const test = require('node:test');
const assert = require('node:assert/strict');
const { graphql } = require('graphql');
const { schema, createRoot } = require('../schema');
const { createServer } = require('../server');
const { createSeedProviders } = require('../weather');

const fixedNow = () => new Date('2026-06-15T12:00:00Z');

function execute(source, providers, variableValues) {
  return graphql({ schema, source, rootValue: createRoot(providers), variableValues });
}

test('composes the travel planning query from seed providers', async () => {
  const result = await execute(`query ($city: String!, $country: String, $days: Int!) {
    travelWeather(city: $city, country: $country) {
      location { name country timezone }
      current { temperatureC condition windSpeedKmh }
      forecast(days: $days) { date minTemperatureC maxTemperatureC condition }
      airQuality { aqi category }
      alerts { title severity startsAt endsAt }
    }
  }`, createSeedProviders(undefined, fixedNow), { city: 'Reykjavik', country: 'IS', days: 5 });
  assert.equal(result.errors, undefined);
  assert.deepEqual(JSON.parse(JSON.stringify(result.data)), {
    travelWeather: {
      location: { name: 'Reykjavik', country: 'IS', timezone: 'Atlantic/Reykjavik' },
      current: { temperatureC: 2, condition: 'Cloudy', windSpeedKmh: 18 },
      forecast: [
        { date: '2026-06-16', minTemperatureC: -2, maxTemperatureC: 2, condition: 'Cloudy' },
        { date: '2026-06-17', minTemperatureC: -1, maxTemperatureC: 3, condition: 'Rain' },
        { date: '2026-06-18', minTemperatureC: 0, maxTemperatureC: 4, condition: 'Cloudy' },
        { date: '2026-06-19', minTemperatureC: -3, maxTemperatureC: 1, condition: 'Snow' },
        { date: '2026-06-20', minTemperatureC: -2, maxTemperatureC: 2, condition: 'Clear' },
      ],
      airQuality: { aqi: 24, category: 'Good' },
      alerts: [],
    },
  });
});

test('lists seeded locations and serves future forecasts in each local timezone', async () => {
  const providers = createSeedProviders(undefined, fixedNow);
  const list = await execute('{ locations { name country } }', providers);
  assert.ok(list.data.locations.length > 1);
  for (const { name, country } of list.data.locations) {
    const result = await execute(`query ($city: String!, $country: String) {
      travelWeather(city: $city, country: $country) { location { name } forecast(days: 1) { date } }
    }`, providers, { city: name, country });
    assert.equal(result.errors, undefined);
    assert.equal(result.data.travelWeather.location.name, name);
  }
  // At 20:00 UTC it is already the next day in Sydney but not in New York.
  const lateUtc = createSeedProviders(undefined, () => new Date('2026-06-15T20:00:00Z'));
  const sydney = await execute('{ travelWeather(city: "sydney", country: "au") { forecast(days: 1) { date } alerts { startsAt endsAt } } }', lateUtc);
  assert.equal(sydney.data.travelWeather.forecast[0].date, '2026-06-17');
  assert.deepEqual(JSON.parse(JSON.stringify(sydney.data.travelWeather.alerts)), [{ startsAt: '2026-06-19', endsAt: '2026-06-20' }]);
  const newYork = await execute('{ travelWeather(city: "New York") { forecast(days: 1) { date } } }', lateUtc);
  assert.equal(newYork.data.travelWeather.forecast[0].date, '2026-06-16');
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
  assert.match(result.errors[0].message, /No weather data for Reykjavik, GB/);
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
