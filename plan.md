# Implementation plan

1. Analyze the source branch's GraphQL schema, HTTP routes, providers, seed
   data and tests. **Complete.**
2. Create an ASP.NET Core / GraphQL.NET service with the same schema, argument
   limits, lazy field resolution and request handling. **Complete.**
3. Implement seed and live providers, including local forecast dates,
   Open-Meteo mappings and US-only NWS alerts. Reuse the original seed data.
   **Complete.**
4. Provide Swagger/OpenAPI, usage instructions, specification and
   architecture documentation. **Complete.**
5. Verify the .NET implementation with integration tests for GraphQL and HTTP
   behavior and mocked live providers; build and run the original tests.
   **Complete.**

Run `dotnet test dotnet/WeatherApi.Tests` for the .NET tests and `npm test`
for the original implementation. Live provider availability and upstream data
are external dependencies; tests use deterministic fixtures instead.
