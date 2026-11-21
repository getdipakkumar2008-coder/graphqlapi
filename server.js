const http = require('node:http');
const { graphql } = require('graphql');
const { schema, createRoot } = require('./schema');
const { createSeedProviders } = require('./weather');
const { createLiveProviders } = require('./liveWeather');

const maxBodyBytes = 64 * 1024;

const travelWeatherQuery = 'query TravelWeather($city: String!, $country: String, $days: Int!) { travelWeather(city: $city, country: $country) { location { name country timezone } current { temperatureC condition windSpeedKmh } forecast(days: $days) { date minTemperatureC maxTemperatureC condition } airQuality { aqi category } alerts { title severity startsAt endsAt } } }';

const openApiSpec = {
  openapi: '3.0.3',
  info: { title: 'Travel weather GraphQL API', version: '1.0.0' },
  paths: {
    '/graphql': {
      post: {
        summary: 'Execute a GraphQL query',
        requestBody: {
          required: true,
          content: {
            'application/json': {
              schema: {
                type: 'object',
                required: ['query'],
                properties: {
                  query: { type: 'string' },
                  variables: { type: 'object' },
                  operationName: { type: 'string' },
                },
              },
              examples: {
                locations: {
                  summary: 'List featured locations',
                  value: { query: 'query Locations { locations { name country timezone } }' },
                },
                searchLocations: {
                  summary: 'Search any city worldwide (edit the variables)',
                  value: {
                    query: 'query Search($name: String!, $count: Int) { searchLocations(name: $name, count: $count) { name country region timezone latitude longitude } }',
                    operationName: 'Search',
                    variables: { name: 'Paris', count: 5 },
                  },
                },
                travelWeather: {
                  summary: 'Weather for a location (edit the variables)',
                  value: {
                    query: travelWeatherQuery,
                    operationName: 'TravelWeather',
                    variables: { city: 'London', country: 'GB', days: 5 },
                  },
                },
              },
            },
          },
        },
        responses: {
          200: { description: 'GraphQL result (data and/or errors)' },
          400: { description: 'Invalid request' },
          413: { description: 'Request body too large' },
        },
      },
    },
  },
};

const swaggerHtml = `<!doctype html>
<html><head><meta charset="utf-8"><title>Swagger UI</title>
<link rel="stylesheet" href="https://unpkg.com/swagger-ui-dist@5/swagger-ui.css"></head>
<body><div id="ui"></div>
<script src="https://unpkg.com/swagger-ui-dist@5/swagger-ui-bundle.js"></script>
<script>SwaggerUIBundle({ url: '/openapi.json', dom_id: '#ui' });</script>
</body></html>`;

function createServer(providers) {
  const rootValue = createRoot(providers);
  return http.createServer((req, res) => {
    function send(status, body) {
      res.writeHead(status, { 'content-type': 'application/json; charset=utf-8' });
      res.end(JSON.stringify(body));
    }

    const pathname = req.url.split('?')[0];
    if (req.method === 'GET' && pathname === '/openapi.json') {
      send(200, openApiSpec);
      return;
    }
    if (req.method === 'GET' && (pathname === '/docs' || pathname === '/')) {
      res.writeHead(200, { 'content-type': 'text/html; charset=utf-8' });
      res.end(swaggerHtml);
      return;
    }

    if (pathname !== '/graphql') {
      send(404, { error: 'Not found' });
      return;
    }
    if (req.method !== 'POST') {
      send(405, { error: 'Use POST for GraphQL requests' });
      return;
    }

    let size = 0;
    const chunks = [];
    req.on('data', (chunk) => {
      size += chunk.length;
      if (size > maxBodyBytes) {
        if (!res.headersSent) send(413, { error: 'Request body too large' });
        return;
      }
      chunks.push(chunk);
    });
    req.on('end', async () => {
      if (res.headersSent) return;
      let payload;
      try {
        payload = JSON.parse(Buffer.concat(chunks).toString('utf8'));
      } catch {
        send(400, { error: 'Invalid JSON' });
        return;
      }
      if (!payload || typeof payload !== 'object' || Array.isArray(payload) ||
          typeof payload.query !== 'string' ||
          (payload.variables != null && (typeof payload.variables !== 'object' || Array.isArray(payload.variables))) ||
          (payload.operationName != null && typeof payload.operationName !== 'string')) {
        send(400, { error: 'Invalid GraphQL request' });
        return;
      }
      try {
        const result = await graphql({
          schema,
          source: payload.query,
          rootValue,
          variableValues: payload.variables,
          operationName: payload.operationName,
        });
        send(200, result);
      } catch {
        send(500, { error: 'Unable to execute GraphQL request' });
      }
    });
  });
}

if (require.main === module) {
  const port = process.env.PORT || 4000;
  const source = (process.env.WEATHER_SOURCE || 'live').toLowerCase();
  if (source !== 'live' && source !== 'seed') {
    console.error('WEATHER_SOURCE must be "live" or "seed"');
    process.exit(1);
  }
  const providers = source === 'live' ? createLiveProviders() : createSeedProviders();
  createServer(providers).listen(port, () => {
    console.log(`GraphQL server listening on port ${port} (${source} weather data)`);
  });
}

module.exports = { createServer };
