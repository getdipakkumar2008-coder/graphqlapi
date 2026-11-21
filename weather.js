const seed = require('./data/seed.json');

const dayMs = 24 * 60 * 60 * 1000;

function localDate(now, timezone) {
  return new Intl.DateTimeFormat('en-CA', {
    timeZone: timezone, year: 'numeric', month: '2-digit', day: '2-digit',
  }).format(now);
}

function addDays(isoDate, days) {
  return new Date(Date.parse(`${isoDate}T00:00:00Z`) + days * dayMs).toISOString().slice(0, 10);
}

function normalize(value) {
  return value.trim().toLowerCase();
}

// Dates are derived from "today" in each location's timezone so seed forecasts are always upcoming.
function createSeedProviders(data = seed, now = () => new Date()) {
  const records = data.locations;
  const find = (location) => records.find((r) => r.name === location.name && r.country === location.country);
  const today = (record) => localDate(now(), record.timezone);
  const toLocation = ({ name, country, timezone }) => ({ name, country, timezone });

  return {
    async locations() {
      return records.map(toLocation);
    },
    async searchLocations(name, count) {
      return records.filter((r) => normalize(r.name).includes(normalize(name))).slice(0, count).map(toLocation);
    },
    async location(city, country) {
      const matches = records.filter((r) => normalize(r.name) === normalize(city) &&
        (country == null || normalize(r.country) === normalize(country)));
      if (matches.length === 0) {
        const where = country == null ? city : `${city}, ${country}`;
        throw new Error(`No weather data for ${where}. Query "locations" for available cities.`);
      }
      if (matches.length > 1) {
        throw new Error(`Multiple locations named ${city}; specify a country`);
      }
      return toLocation(matches[0]);
    },
    async current(location) {
      return find(location).current;
    },
    async forecast(location, days) {
      const record = find(location);
      const start = today(record);
      return record.forecast.slice(0, days).map((day, i) => ({ date: addDays(start, i + 1), ...day }));
    },
    async airQuality(location) {
      return find(location).airQuality;
    },
    async alerts(location) {
      const record = find(location);
      const start = today(record);
      return record.alerts.map(({ title, severity, startDayOffset, endDayOffset }) => ({
        title,
        severity,
        startsAt: addDays(start, startDayOffset),
        endsAt: addDays(start, endDayOffset),
      }));
    },
  };
}

const sampleProviders = createSeedProviders();

module.exports = { createSeedProviders, sampleProviders };
