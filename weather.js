const location = {
  name: 'Reykjavik',
  country: 'IS',
  timezone: 'Atlantic/Reykjavik',
};

const forecast = [
  { date: '2026-01-01', minTemperatureC: -2, maxTemperatureC: 2, condition: 'Cloudy' },
  { date: '2026-01-02', minTemperatureC: -1, maxTemperatureC: 3, condition: 'Rain' },
  { date: '2026-01-03', minTemperatureC: 0, maxTemperatureC: 4, condition: 'Cloudy' },
  { date: '2026-01-04', minTemperatureC: -3, maxTemperatureC: 1, condition: 'Snow' },
  { date: '2026-01-05', minTemperatureC: -2, maxTemperatureC: 2, condition: 'Clear' },
];

const sampleProviders = {
  async location(city, country) {
    if (city.trim().toLowerCase() !== 'reykjavik' ||
        (country != null && country.trim().toUpperCase() !== 'IS')) {
      throw new Error('Sample data is only available for Reykjavik, IS');
    }
    return location;
  },
  async current() {
    return { temperatureC: 2, condition: 'Cloudy', windSpeedKmh: 18 };
  },
  async forecast(_location, days) {
    return forecast.slice(0, days);
  },
  async airQuality() {
    return { aqi: 24, category: 'Good' };
  },
  async alerts() {
    return [];
  },
};

module.exports = { sampleProviders };
