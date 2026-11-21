const http = require('node:http');
const { graphql } = require('graphql');
const { schema, createRoot } = require('./schema');

const maxBodyBytes = 64 * 1024;

function createServer(providers) {
  const rootValue = createRoot(providers);
  return http.createServer((req, res) => {
    function send(status, body) {
      res.writeHead(status, { 'content-type': 'application/json; charset=utf-8' });
      res.end(JSON.stringify(body));
    }

    if (req.url.split('?')[0] !== '/graphql') {
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
  createServer().listen(port, () => {
    console.log(`GraphQL server listening on port ${port}`);
  });
}

module.exports = { createServer };
